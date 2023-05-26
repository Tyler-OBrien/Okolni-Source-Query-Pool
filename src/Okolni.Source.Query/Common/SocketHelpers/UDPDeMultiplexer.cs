using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Okolni.Source.Query.Common.SocketHelpers;

/*
 * This is really work in progress
 * The basic idea was just to use a single socket instead of potentially thousands (depending on how many queries you are making)... Since we are just sending out a few UDP Packets and getting a few back, we don't really need a dedicated socket or anything, really short life time & short packet lengths.
 * In order to make that work, we needed a way to still allow async (not callbacks or anything), receive the right responses to the right endpoints
 * I don't think there's any native way to do this with the .NET Socket API. I tried giving it a certain IP Endpoint in the ReceiveFromAsync method, but it would still get responses made for other endpoints
 * There's probably a better way to do this, and this is really messy right now, but it does seem to work without issue.
 */
public class DemuxConnection
{
    public DemuxConnection(TaskCompletionSource<Memory<byte>> receiveFrom,
        bool bufferDirty, bool generated)
    {
        ReceiveFrom = receiveFrom;
        CancellationToken = CancellationToken.None;
        BufferDirty = bufferDirty;
        Generated = generated;
    }

    public DemuxConnection(TaskCompletionSource<Memory<byte>> receiveFrom, bool bufferDirty, bool generated, CancellationToken cancellation)
    {
        ReceiveFrom = receiveFrom;
        CancellationToken = cancellation;
        BufferDirty = bufferDirty;
        Generated = generated;
    }

    public TaskCompletionSource<Memory<byte>> ReceiveFrom;
    public CancellationToken CancellationToken;
    public bool BufferDirty;
    public bool Generated;
}

public class DemuxConnections
{
    public DemuxConnections()
    {
    }


    public Queue<DemuxConnection> Connections = new();
}

public class UDPDeMultiplexer
{
    private readonly Dictionary<EndPoint, DemuxConnections> Connections = new();

    private readonly object mutex = new();

    private int _waitingConnections;


    public ValueTask<Memory<byte>> ReceiveAsync(
        SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        Socket socket,
        CancellationToken cancellationToken = default)
    {
        lock (mutex)
        {
            return InternalReceive(remoteEndPoint, cancellationToken);
        }
    }

    internal async ValueTask<Memory<byte>> InternalReceive(
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        if (remoteEndPoint is IPEndPoint ipEndpoint) ipEndpoint.Address = ipEndpoint.Address.MapToIPv6();


        if (Connections.TryGetValue(remoteEndPoint, out var demuxConnections))
        {
            if (TryDequeue(demuxConnections, out var value))
            {
                // Dirty, meaning it has content for us..
                if (value.BufferDirty)
                {
#if DEBUG
                    Console.WriteLine(
                        $"Found instantly for {remoteEndPoint}...exiting early... {Convert.ToBase64String(value.ReceiveFrom.Task.Result.ToArray().Take(10).ToArray())}");
#endif
                    RemoveIfEmpty(remoteEndPoint, demuxConnections);
                    return await new ValueTask<Memory<byte>>(value.ReceiveFrom.Task);
                }

                // If it isn't dirty, it's waiting just like us, and we should add it back.
                Enqueue(demuxConnections, value);
            }

            // If we couldn't get anything from the queue, or we could but it wasn't dirty, we should enqueue our own item.
            var taskCompletionSource = new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(demuxConnections, new DemuxConnection(taskCompletionSource, false, false,
                cancellationToken));
#if DEBUG
            Console.WriteLine($"New Listener: {remoteEndPoint} - {remoteEndPoint.Serialize()}");
#endif

            return await new ValueTask<Memory<byte>>(taskCompletionSource.Task);
        }

        // If there's no queue yet, we should create it and add it.
        var tcs = new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newDemuxConnections = new DemuxConnections();
        Enqueue(newDemuxConnections, new DemuxConnection(tcs, false, false, cancellationToken));
        Connections.Add(remoteEndPoint, newDemuxConnections);
#if DEBUG
        Console.WriteLine($"New Listener: {remoteEndPoint} - {remoteEndPoint.Serialize()}");
#endif

        return await new ValueTask<Memory<byte>>(tcs.Task);
    }


    public async Task Start(Socket socket, IPEndPoint endPoint, CancellationToken token)
    {
#if DEBUG
        Console.WriteLine($"Starting up Demux Worker - {Environment.CurrentManagedThreadId}");
#endif
        Task delayTask = Task.Delay(500, token).HandleOperationCancelled();
        Task<SocketReceiveFromResult> udpClientReceiveTask = null;
        ValueTask<SocketReceiveFromResult> udpClientReceiveValueTask = new ValueTask<SocketReceiveFromResult>();
        byte[] buffer = new byte[65527];
        bool usedResponse = true;
        bool cleanedUpResponses = true;

        while (true)
        {
            if (token.IsCancellationRequested)
            {
                Cleanup();
                return;
            }
            // We might have timed out from the delayTask, but still are waiting for a new packet.
            if (usedResponse)
            {
                udpClientReceiveTask?.Dispose();
                udpClientReceiveValueTask =
                    socket.ReceiveFromAsync(buffer, SocketFlags.None, endPoint,
                        token);
                udpClientReceiveTask = udpClientReceiveValueTask.AsTask().HandleOperationCancelled();
                usedResponse = false;
            }

            if (udpClientReceiveValueTask.IsCompleted == false)
            {
                if (cleanedUpResponses)
                {
                    delayTask = Task.Delay(500, token).HandleOperationCancelled();
                    cleanedUpResponses = false;
                }

                await Task.WhenAny(udpClientReceiveTask, delayTask);
            }

            // If there is a new packet to be recieved
            if (udpClientReceiveTask.IsCompletedSuccessfully)
            {
                usedResponse = true;
                var udpClientReceive = await udpClientReceiveTask;
                var newBuffer = new byte[udpClientReceive.ReceivedBytes];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, udpClientReceive.ReceivedBytes);
                lock (mutex)
                {
                    // Check if we have a queue created
                    if (Connections.TryGetValue(udpClientReceive.RemoteEndPoint,
                            out var demuxConnections))
                    {
#if DEBUG
                        Console.WriteLine(
                            $"Delivered packet to {udpClientReceive.RemoteEndPoint} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif
                        // Check if we can dequeue an item
                        var shouldEnqueue = true;
                        if (TryDequeue(demuxConnections, out var demuxConnection))
                        {
                            // This was already written to..
                            if (demuxConnection.BufferDirty)
                                // Re-queue, this is already written to, and is awaiting a listener...
                                Enqueue(demuxConnections, demuxConnection);

                            // If it wasn't written to yet..
                            if (!demuxConnection.BufferDirty)
                            {
                                shouldEnqueue = false;
                                demuxConnection.BufferDirty = true;
                                demuxConnection.ReceiveFrom.SetResult(newBuffer);
                                RemoveIfEmpty(udpClientReceive.RemoteEndPoint, demuxConnections);
                            }
                        }

                        // If we couldn't dequeue an item or the dequeued item was dirty... we need to enqueue our own
                        if (shouldEnqueue)
                        {
                            var tcs = new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
                            tcs.SetResult(newBuffer);
                            Enqueue(demuxConnections, new DemuxConnection(tcs, true, true));
                        }
                    }
                    // This endpoint isn't registered yet, create a new queue..
                    else
                    {
#if DEBUG
                        Console.WriteLine(
                            $"Found nothing listening... on {udpClientReceive.RemoteEndPoint}, {Connections.Count} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif
                        var tcs = new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
                        tcs.SetResult(newBuffer);

                        var newDemuxConnections = new DemuxConnections();
                        Enqueue(newDemuxConnections, new DemuxConnection(tcs, true, true));
                        Connections.Add(udpClientReceive.RemoteEndPoint, newDemuxConnections);
                    }
                }
            }

            // It would be nice to have a better solution then this, recreating the queue each time.

            if (delayTask.IsCompletedSuccessfully)
                lock (mutex)
                {
                    cleanedUpResponses = true;
                    if (Connections.Keys.Any())
                        foreach (var keyPair in Connections.Keys.ToList())
                            if (Connections.TryGetValue(keyPair, out var demuxConnections))
                            {
                                var connections = new List<DemuxConnection>();

                                while (TryDequeue(demuxConnections, out var value))
                                    if (value.CancellationToken != CancellationToken.None &&
                                        value.CancellationToken.IsCancellationRequested)
                                    {
#if DEBUG
                                        Console.WriteLine($"Timed out waiting packet from {keyPair}");
#endif
                                        value.ReceiveFrom.TrySetException(
                                            new OperationCanceledException("Operation timed out"));
                                    }
                                    else
                                    {
                                        connections.Add(value);
                                    }

                                foreach (var connection in connections)
                                    Enqueue(demuxConnections, connection);
                                RemoveIfEmpty(keyPair, demuxConnections);
                            }
                }
        }
    }

    // These should be inside of the semaphore
    private void Enqueue(DemuxConnections queue, DemuxConnection item)
    {
        queue.Connections.Enqueue(item);
        Interlocked.Increment(ref _waitingConnections);
    }

    private bool TryDequeue(DemuxConnections queue, out DemuxConnection item)
    {
        if (queue.Connections.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _waitingConnections);
            return true;
        }

        return false;
    }

    private void RemoveIfEmpty(EndPoint endPoint, DemuxConnections demuxConnections)
    {
        if (demuxConnections.Connections.Count == 0) Connections.Remove(endPoint);
    }

    public int GetWaitingConnections()
    {
        return _waitingConnections;
    }

    private void Cleanup()
    {
        lock (mutex)
        {
            if (Connections.Keys.Any())
                foreach (var keyPair in Connections.Keys.ToList())
                    if (Connections.TryGetValue(keyPair, out var demuxConnections))
                        while (TryDequeue(demuxConnections, out var value))
                            value.ReceiveFrom.TrySetException(
                                new OperationCanceledException("Operation was cancelled by the pool being disposed"));
            Connections.Clear();
        }
    }
}