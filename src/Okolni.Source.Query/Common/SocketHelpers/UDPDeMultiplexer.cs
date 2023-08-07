using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Query.Pool.Common.SocketHelpers;

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
    public readonly ConcurrentDictionary<EndPoint, DemuxSocketWrapper> Connections = new();


    private int _waitingConnections;


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
                await Cleanup();
                socket?.Dispose();
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
                var newBuffer = new byte[udpClientReceive.ReceivedBytes];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, udpClientReceive.ReceivedBytes);

                // Check if we have a queue created
                if (Connections.TryGetValue(udpClientReceive.RemoteEndPoint,
                        out var demuxConnections))
                {

                    if (await demuxConnections.SempahoreSlim.WaitAsync(1000))
                    {
                        try
                        {
#if DEBUG
                            Console.WriteLine(
                                $"Delivered packet to {udpClientReceive.RemoteEndPoint} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif

                            if (demuxConnections.ReceiveFrom != null &&
                                demuxConnections.ReceiveFrom.Task.IsCompletedSuccessfully == false)
                            {
                                demuxConnections.ReceiveFrom.SetResult(newBuffer);
                            }
                            else
                            {
                                demuxConnections.Queue.Enqueue(newBuffer);
                            }
                        }
                        finally
                        {
                            demuxConnections.SempahoreSlim.Release();
                        }
                    }
                    else
                    {
#if DEBUG
                        Console.WriteLine($"Warn: Could not acquire log for {udpClientReceive.RemoteEndPoint}");
#endif
                    }
                }
                // This endpoint isn't registered yet
                else
                {
#if DEBUG
                    Console.WriteLine(
                        $"Found nothing listening... on {udpClientReceive.RemoteEndPoint}, {Connections.Count} - {Convert.ToBase64String(newBuffer.ToArray().Take(10).ToArray())}");
#endif

                }
            }


            if (delayTask.IsCompletedSuccessfully)
                cleanedUpResponses = true;
            if (Connections.Keys.Any())
                foreach (var keyPair in Connections.Keys.ToList())
                    if (Connections.TryGetValue(keyPair, out var demuxConnections))
                    {
                        if (await demuxConnections.SempahoreSlim.WaitAsync(1000))
                        {
                            try
                            {
                                if (demuxConnections.ReceiveFrom != null && demuxConnections.ReceiveFrom.Task.IsCompleted == false && demuxConnections.CancellationToken != CancellationToken.None &&
                                    demuxConnections.CancellationToken.IsCancellationRequested)
                                {
#if DEBUG
                                    Console.WriteLine($"Timed out waiting packet from {keyPair}");
#endif
                                    demuxConnections.ReceiveFrom.TrySetException(
                                        new OperationCanceledException("Operation timed out"));
                                }
                            }
                            finally
                            {
                                demuxConnections.SempahoreSlim.Release();
                            }
                        }
                        else
                        {
#if DEBUG
                            Console.WriteLine($"Failed acquiring lock for {keyPair} after 1000ms");
#endif
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
        if (Connections.Keys.Any())
            foreach (var keyPair in Connections.Keys.ToList())
                if (Connections.TryGetValue(keyPair, out var demuxConnections))
                {
                    if (await demuxConnections.SempahoreSlim.WaitAsync(500))
                    {
                        try
                        {
                            if (demuxConnections.ReceiveFrom != null && demuxConnections.ReceiveFrom.Task.IsCompleted == false)
                                demuxConnections.ReceiveFrom.TrySetException(
                                    new OperationCanceledException(
                                        "Operation was cancelled by the pool being disposed"));
                        }
                        finally
                        {
                            demuxConnections.SempahoreSlim.Release();
                        }
                    }
                    else
                    {
#if DEBUG
                        Console.WriteLine($"Failed acquiring lock for {keyPair} after 500ms for cleanup");
#endif
                    }
                }

        Connections.Clear();
    }
}