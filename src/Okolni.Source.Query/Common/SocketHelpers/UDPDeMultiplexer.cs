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

    public event IQueryConnectionPool.PoolError Error;

    /// <inheritdoc />
    public event IQueryConnectionPool.PoolMessage Message;


    public async Task Start(Socket socket, IPEndPoint endPoint, CancellationToken token)
    {
#if DEBUG
        Console.WriteLine($"Starting up Demux Worker - {Environment.CurrentManagedThreadId}");
#endif
        _buffer = GC.AllocateArray<byte>(length: 65527, pinned: true);
        try
        {
            // Timeouts are handled by each waiting receive's own cancellation registration (see ReceiveFromAsync),
            // so this loop only has to receive and hand packets out.
            while (!token.IsCancellationRequested)
            {
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(_buffer, SocketFlags.None, endPoint, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    // e.g. ICMP port unreachable surfacing on some platforms. The affected query just times out.
                    Message?.Invoke($"Socket error while receiving, continuing: {ex.SocketErrorCode}");
                    continue;
                }

                Deliver(received);
            }
        }
        finally
        {
            await Cleanup();
            socket?.Dispose();
        }
    }

    private void Deliver(SocketReceiveFromResult received)
    {
        var newBuffer = ArrayPoolInterface.Rent(received.ReceivedBytes);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, received.ReceivedBytes);
        var packet = new ArrayPoolMemory(newBuffer, received.ReceivedBytes);
        lock (_lock)
        {
            if (Connections.TryGetValue(received.RemoteEndPoint, out var demuxConnection))
            {
#if DEBUG
                Console.WriteLine(
                    $"Delivered packet to {received.RemoteEndPoint} - {Convert.ToBase64String(newBuffer.Take(10).ToArray())}");
#endif
                // TrySetResult, since a timeout can complete the waiting receive at any moment
                if (demuxConnection.ReceiveFrom == null || !demuxConnection.ReceiveFrom.TrySetResult(packet))
                    demuxConnection.Queue.Enqueue(packet);
            }
            // This endpoint isn't registered (yet / anymore), e.g. a late reply after a timeout
            else
            {
#if DEBUG
                Console.WriteLine(
                    $"Found nothing listening... on {received.RemoteEndPoint}, {Connections.Count} - {Convert.ToBase64String(newBuffer.Take(10).ToArray())}");
#endif
                ArrayPoolInterface.Return(newBuffer);
            }
        }
    }


    public int GetWaitingConnections()
    {
        lock (_lock)
        {
            return Connections.Values.Count(connection => connection.ReceiveFrom is { Task.IsCompleted: false });
        }
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

        // Each wait times itself out, rather than the receive loop scanning every listener after every packet
        await using var registration = cancellationToken.Register(static state =>
                ((TaskCompletionSource<ArrayPoolMemory>)state!).TrySetException(
                    new OperationCanceledException("Operation timed out")),
            socketWrapper.ReceiveFrom);
        try
        {
            return await newTask;
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