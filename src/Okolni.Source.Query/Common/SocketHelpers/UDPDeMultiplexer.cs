using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Pool.Common.SocketHelpers;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Query.Common.SocketHelpers;

/*
 * This is really work in progress
 * The basic idea was just to use a single socket instead of potentially thousands (depending on how many queries you are making)... Since we are just sending out a few UDP Packets and getting a few back, we don't really need a dedicated socket or anything, really short life time & short packet lengths.
 * In order to make that work, we needed a way to still allow async (not callbacks or anything), receive the right responses to the right endpoints
 * I don't think there's any native way to do this with the .NET Socket API. I tried giving it a certain IP Endpoint in the ReceiveFromAsync method, but it would still get responses made for other endpoints
 * There's probably a better way to do this, and this is really messy right now, but it does seem to work without issue.
 */

public class UDPDeMultiplexer
{
    private readonly Dictionary<EndPoint, DemuxSocketWrapper> Connections = new();

    private object _lock = new object();

    private byte[] _buffer;


    private int _waitingConnections;

    public event IQueryConnectionPool.PoolError Error;

    /// <inheritdoc />
    public event IQueryConnectionPool.PoolMessage Message;


    public async Task Start(Socket socket, IPEndPoint endPoint, CancellationToken token)
    {
#if DEBUG
        Console.WriteLine($"Starting up Demux Worker - {Environment.CurrentManagedThreadId}");
#endif
        var delayTask = Task.Delay(500, token).HandleOperationCancelled();
        Task<SocketReceiveFromResult> udpClientReceiveTask = null;
        var udpClientReceiveValueTask = new ValueTask<SocketReceiveFromResult>();
        _buffer  = GC.AllocateArray<byte>(length: 65527, pinned: true);
        var usedResponse = true;
        var cleanedUpResponses = true;

        while (true)
        {
            if (token.IsCancellationRequested)
            {
                await Cleanup();
                socket?.Dispose();
                return;
            }

            // We might have timed out from the delayTask, but still are waiting for a new packet.
            if (usedResponse)
            {
                udpClientReceiveTask?.Dispose();
                udpClientReceiveValueTask =
                    socket.ReceiveFromAsync(_buffer, SocketFlags.None, endPoint,
                        token);
                udpClientReceiveTask = udpClientReceiveValueTask.AsTask().HandleOperationCancelled();
                usedResponse = false;
            }

            if (udpClientReceiveTask.IsCompleted == false)
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
                
                var newBuffer = ArrayPoolInterface.Rent(udpClientReceive.ReceivedBytes);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, udpClientReceive.ReceivedBytes);
                lock (_lock)
                {
                    if (Connections.TryGetValue(udpClientReceive.RemoteEndPoint,
                            out var demuxConnections))
                    {
                        {
                            try
                            {
#if DEBUG
                            Console.WriteLine(
                                $"Delivered packet to {udpClientReceive.RemoteEndPoint} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif

                                if (demuxConnections.ReceiveFrom != null &&
                                    demuxConnections.ReceiveFrom.Task.IsCompleted == false)
                                {
                                    demuxConnections.ReceiveFrom.SetResult(new ArrayPoolMemory(newBuffer, udpClientReceive.ReceivedBytes));
                                }
                                else
                                {
                                    demuxConnections.Queue.Enqueue(new ArrayPoolMemory(newBuffer, udpClientReceive.ReceivedBytes));
                                }
                            }
                            catch (Exception ex)
                            {
                                Error?.Invoke(ex);
                            }
                        }
                    }
                    // This endpoint isn't registered yet
                    else
                    {
#if DEBUG
                    Console.WriteLine(
                        $"Found nothing listening... on {udpClientReceive.RemoteEndPoint}, {Connections.Count} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif
                        ArrayPoolInterface.Return(newBuffer);
                    }
                }
            }


            if (delayTask.IsCompletedSuccessfully)
                cleanedUpResponses = true;
            lock(_lock)
            {
                foreach (var keyPair in Connections)
                    try
                    {
                        var demuxConnections = keyPair.Value;
                        if (demuxConnections.ReceiveFrom is { Task.IsCompleted: false } &&
                            demuxConnections.CancellationToken != CancellationToken.None &&
                            demuxConnections.CancellationToken.IsCancellationRequested)
                        {
#if DEBUG
                                    Console.WriteLine($"Timed out waiting packet from {keyPair}");
#endif
                            demuxConnections.ReceiveFrom.TrySetException(
                                new OperationCanceledException("Operation timed out"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(ex);
                    }
            }
        }
    }


    public int GetWaitingConnections()
    {
        return _waitingConnections;
    }

    private async ValueTask Cleanup()
    {
        lock(_lock)
        {
            GCHandle.Alloc(_buffer, GCHandleType.Pinned).Free();
            foreach (var keyPair in Connections)
                try
                {
                    var demuxConnections = keyPair.Value;
                    if (demuxConnections.ReceiveFrom != null &&
                        demuxConnections.ReceiveFrom.Task.IsCompleted == false)
                        demuxConnections.ReceiveFrom.TrySetException(
                            new OperationCanceledException(
                                "Operation was cancelled by the pool being disposed"));

                    if (demuxConnections.Queue != null)
                        foreach (var rentedArray in demuxConnections.Queue)
                            ArrayPoolInterface.Return(rentedArray.RawRequest);
                    if (demuxConnections.ReceiveFrom != null && demuxConnections.ReceiveFrom.Task.IsCompletedSuccessfully)
                        ArrayPoolInterface.Return(demuxConnections.ReceiveFrom.Task.Result.RawRequest);

                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex);
                }

            Connections.Clear();
        }
    }

    public async ValueTask<ArrayPoolMemory> ReceiveFromAsync(DemuxSocketWrapper socketWrapper, SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        Task<ArrayPoolMemory> newTask = null;
        var tryEnter = false;
        lock(_lock)
        {
            if (socketWrapper.Queue.TryDequeue(out var result))
            {
#if DEBUG
                    Console.WriteLine($"Received Packet from Queue for {remoteEndPoint}");
#endif
                return result;
            }

            if (socketWrapper.ReceiveFrom is { Task.IsCompleted: false })
                socketWrapper.ReceiveFrom.TrySetException(
                    new OperationCanceledException(
                        "Cancelled due to another ReceiveFrom being queued for this SocketWrapper"));

            socketWrapper.CancellationToken = cancellationToken;
            socketWrapper.ReceiveFrom =
                new TaskCompletionSource<ArrayPoolMemory>(TaskCreationOptions.RunContinuationsAsynchronously);
            newTask = socketWrapper.ReceiveFrom.Task;
#if DEBUG
                Console.WriteLine($"Starting Task for receiving Packet from {remoteEndPoint}");
#endif
        }

        try
        {
            return await new ValueTask<ArrayPoolMemory>(newTask);
        }
        finally
        {
            socketWrapper.ReceiveFrom = null;
            socketWrapper.CancellationToken = CancellationToken.None;
        }
    }

    public void AddListener(IPEndPoint endPoint, DemuxSocketWrapper wrapper)
    {
        lock(_lock)
        {
            if (!Connections.TryAdd(endPoint, wrapper))
                throw new InvalidOperationException("Only one listener per endpoint active at one time...");
        }
    }

    public void RemoveListener(IPEndPoint endPoint)
    {
        lock(_lock)
        {
            if (Connections.Remove(endPoint, out var currentValue) == false)
                throw new InvalidOperationException("Failed to remove endpoint listener, already gone?");
            else
            {
                if (currentValue.Queue != null)
                    foreach (var rentedArray in currentValue.Queue)
                        ArrayPoolInterface.Return(rentedArray.RawRequest);
                if (currentValue.ReceiveFrom != null && currentValue.ReceiveFrom.Task.IsCompletedSuccessfully)
                        ArrayPoolInterface.Return(currentValue.ReceiveFrom.Task.Result.RawRequest);
            }
        }
    }
}