using System;
using System.Collections.Concurrent;
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
public struct DemuxConnection
{
    public DemuxConnection(TaskCompletionSource<SocketReceiveFromResult> receiveFrom,
        Memory<byte> buffer, bool bufferDirty, bool generated)
    {
        ReceiveFrom = receiveFrom;
        CancellationToken = CancellationToken.None;
        MemoryBuffer = buffer;
        BufferDirty = bufferDirty;
        Generated = generated;
    }

    public DemuxConnection(TaskCompletionSource<SocketReceiveFromResult> receiveFrom,
        Memory<byte> buffer, bool bufferDirty, bool generated, CancellationToken cancellation)
    {
        ReceiveFrom = receiveFrom;
        CancellationToken = cancellation;
        MemoryBuffer = buffer;
        BufferDirty = bufferDirty;
        Generated = generated;
    }

    public TaskCompletionSource<SocketReceiveFromResult> ReceiveFrom;
    public CancellationToken CancellationToken;
    public Memory<byte> MemoryBuffer;
    public bool BufferDirty;
    public bool Generated;
}

public struct DemuxConnections
{
    public DemuxConnections()
    {
    }


    public ConcurrentQueue<DemuxConnection> Connections = new();
}

public class UDPDeMultiplexer
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly Dictionary<EndPoint, DemuxConnections> Connections = new();

    private int _waitingConnections;


    public async ValueTask<SocketReceiveFromResult> ReceiveAsync(Memory<byte> buffer,
        SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        Socket socket,
        CancellationToken cancellationToken = default)
    {
        var alreadyFree = false;
        try
        {
            await _semaphoreSlim.WaitAsync(cancellationToken);
            if (remoteEndPoint is IPEndPoint ipEndpoint) ipEndpoint.Address = ipEndpoint.Address.MapToIPv6();


            if (Connections.TryGetValue(remoteEndPoint, out var demuxConnections))
            {
                if (TryDequeue(demuxConnections.Connections, out var value))
                {
                    // Dirty, meaning it has content for us..
                    if (value.BufferDirty)
                    {
#if DEBUG
                        Console.WriteLine(
                            $"Found instantly for {remoteEndPoint}...exiting early... {Convert.ToBase64String(value.MemoryBuffer.ToArray().Take(10).ToArray())}");
#endif
                        RemoveIfEmpty(remoteEndPoint, demuxConnections);
                        value.MemoryBuffer.CopyTo(buffer);
                        return await value.ReceiveFrom.Task;
                    }
                    else
                    {
                        // If it isn't dirty, it's waiting just like us, and we should add it back.
                        Enqueue(demuxConnections.Connections, value);
                    }
                }

                // If we couldn't get anything from the queue, or we could but it wasn't dirty, we should enqueue our own item.
                var taskCompletionSource = new TaskCompletionSource<SocketReceiveFromResult>();
                Enqueue(demuxConnections.Connections, new DemuxConnection(taskCompletionSource, buffer, false, false,
                    cancellationToken));
#if DEBUG
                Console.WriteLine($"New Listener: {remoteEndPoint} - {remoteEndPoint.Serialize()}");
#endif
                _semaphoreSlim.Release();
                alreadyFree = true;
                return await taskCompletionSource.Task;
            }

            // If there's no queue yet, we should create it and add it.
            var tcs = new TaskCompletionSource<SocketReceiveFromResult>();
            var newDemuxConnections = new DemuxConnections();
            Enqueue(newDemuxConnections.Connections, new DemuxConnection(tcs, buffer, false, false, cancellationToken));
            Connections.Add(remoteEndPoint, newDemuxConnections);
#if DEBUG
            Console.WriteLine($"New Listener: {remoteEndPoint} - {remoteEndPoint.Serialize()}");
#endif
            _semaphoreSlim.Release();
            alreadyFree = true;
            return await tcs.Task;
        }
        finally
        {
            if (!alreadyFree)
                _semaphoreSlim.Release();
        }
    }


    public async Task Start(Socket socket, IPEndPoint endPoint, CancellationToken token)
    {
#if DEBUG
        Console.WriteLine($"Starting up Demux Worker - {Environment.CurrentManagedThreadId}");
#endif
        Task delayTask = null;
        Task<SocketReceiveFromResult> udpClientReceiveTask = null;
        Memory<byte> buffer = null;

        while (true)
        {
            if (token.IsCancellationRequested) break;
            // We might have timed out from the delayTask, but still are waiting for a new packet.
            if (udpClientReceiveTask == null || udpClientReceiveTask.IsCompleted)
            {
                buffer = new byte[65527];
                udpClientReceiveTask = socket.ReceiveFromAsync(buffer, SocketFlags.None, endPoint,
                    token).AsTask().HandleOperationCancelled();
            }


            if (delayTask == null || delayTask.IsCompleted)
                delayTask = Task.Delay(500, token).HandleOperationCancelled();

            await Task.WhenAny(udpClientReceiveTask, delayTask);

            // If there is a new packet to be recieved
            if (udpClientReceiveTask.IsCompletedSuccessfully)
            {
                var udpClientReceive = await udpClientReceiveTask;
                try
                {
                    await _semaphoreSlim.WaitAsync(CancellationToken.None);
                    // Check if we have a queue created
                    if (Connections.TryGetValue(udpClientReceive.RemoteEndPoint,
                            out var demuxConnections))
                    {
#if DEBUG
                        Console.WriteLine(
                            $"Delivered packet to {udpClientReceive.RemoteEndPoint} - {Convert.ToBase64String(buffer.ToArray().Take(10).ToArray())}");
#endif
                        // Check if we can dequeue an item
                        var shouldEnqueue = true;
                        if (TryDequeue(demuxConnections.Connections, out var demuxConnection))
                        {
                            // This was already written to..
                            if (demuxConnection.BufferDirty)
                                // Re-queue, this is already written to, and is awaiting a listener...
                                Enqueue(demuxConnections.Connections, demuxConnection);

                            // If it wasn't written to yet..
                            if (!demuxConnection.BufferDirty)
                            {
                                shouldEnqueue = false;
                                demuxConnection.BufferDirty = true;
                                buffer.CopyTo(demuxConnection.MemoryBuffer);
                                demuxConnection.ReceiveFrom.SetResult(udpClientReceive);
                                RemoveIfEmpty(udpClientReceive.RemoteEndPoint, demuxConnections);
                            }
                        }

                        // If we couldn't dequeue an item or the dequeued item was dirty... we need to enqueue our own
                        if (shouldEnqueue)
                        {
                            var tcs = new TaskCompletionSource<SocketReceiveFromResult>();
                            tcs.SetResult(udpClientReceive);
                            Enqueue(demuxConnections.Connections, new DemuxConnection(tcs,
                                buffer, true, true));
                        }
                    }
                    // This endpoint isn't registered yet, create a new queue..
                    else
                    {
#if DEBUG
                        Console.WriteLine(
                            $"Found nothing listening... on {udpClientReceive.RemoteEndPoint}, {Connections.Count} - {Convert.ToBase64String(buffer.ToArray().Take(10).ToArray())}");
#endif
                        var tcs = new TaskCompletionSource<SocketReceiveFromResult>();
                        tcs.SetResult(udpClientReceive);

                        var newDemuxConnections = new DemuxConnections();
                        Enqueue(newDemuxConnections.Connections, new DemuxConnection(tcs, buffer, true, true));
                        Connections.Add(udpClientReceive.RemoteEndPoint, newDemuxConnections);
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }

            // It would be nice to have a better solution then this, recreating the queue each time.
            if (delayTask.IsCompletedSuccessfully)
                foreach (var keyPair in Connections.Keys.ToList())
                    if (Connections.TryGetValue(keyPair, out var demuxConnections))
                    {
                        var connections = new List<DemuxConnection>();
                        while (TryDequeue(demuxConnections.Connections, out var value))
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

                        foreach (var connection in connections) Enqueue(demuxConnections.Connections, connection);
                        RemoveIfEmpty(keyPair, demuxConnections);
                    }
        }
    }

    // These should be inside of the semaphore
    private void Enqueue(ConcurrentQueue<DemuxConnection> queue, DemuxConnection item)
    {
        queue.Enqueue(item);
        Interlocked.Increment(ref _waitingConnections);
    }

    private bool TryDequeue(ConcurrentQueue<DemuxConnection> queue, out DemuxConnection item)
    {
        if (queue.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _waitingConnections);
            return true;
        }

        return false;
    }

    private void RemoveIfEmpty(EndPoint endPoint, DemuxConnections demuxConnections)
    {
        if (demuxConnections.Connections.IsEmpty) Connections.Remove(endPoint);
    }

    public int GetWaitingConnections()
    {
        return _waitingConnections;
    }
}