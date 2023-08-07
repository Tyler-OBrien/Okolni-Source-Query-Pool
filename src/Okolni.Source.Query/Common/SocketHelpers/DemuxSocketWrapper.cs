using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Query.Common.SocketHelpers;

namespace Okolni.Source.Query.Pool.Common.SocketHelpers
{
    public class DemuxSocketWrapper : ISocket
    {
        private readonly DemuxSocket _demuxSocket;
        private readonly IPEndPoint _endPoint;

        public CancellationToken CancellationToken;

        public TaskCompletionSource<Memory<byte>> ReceiveFrom;

        public Queue<Memory<byte>> Queue { get; private set; } =  new Queue<Memory<byte>>();

        public SemaphoreSlim SempahoreSlim = new SemaphoreSlim(1, 1);
        public DemuxSocketWrapper(DemuxSocket demuxSocket, IPEndPoint endPoint)
        {
            if (endPoint is IPEndPoint ipEndpoint) ipEndpoint.Address = ipEndpoint.Address.MapToIPv6();
            _demuxSocket = demuxSocket;
            _endPoint = endPoint;
        }

        public async ValueTask<int> SendToAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEP,
            CancellationToken cancellationToken = default)
        {
            return await _demuxSocket.SendToAsync(buffer, socketFlags, remoteEP, cancellationToken);
        }

        public async ValueTask<Memory<byte>> ReceiveFromAsync(SocketFlags socketFlags, EndPoint remoteEndPoint,
            CancellationToken cancellationToken = default)
        {
            Task<Memory<byte>> newTask = null;
            await SempahoreSlim.WaitAsync(cancellationToken);
            try
            {
                if (Queue.TryDequeue(out var result))
                {
#if DEBUG
                    Console.WriteLine($"Received Packet from Queue for {remoteEndPoint}");
#endif
                    return result;
                }

                if (ReceiveFrom is { Task.IsCompleted: false })
                    ReceiveFrom.TrySetException(
                        new OperationCanceledException(
                            "Cancelled due to another ReceiveFrom being queued for this SocketWrapper"));

                CancellationToken = cancellationToken;
                ReceiveFrom =
                    new TaskCompletionSource<Memory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
                newTask = ReceiveFrom.Task;
#if DEBUG
                Console.WriteLine($"Starting Task for receiving Packet from {remoteEndPoint}");
#endif
            }
            finally
            {
                SempahoreSlim.Release();
            }

            try
            {
                return await new ValueTask<Memory<byte>>(newTask);
            }
            finally
            {
                ReceiveFrom = null;
                CancellationToken = CancellationToken.None;
            }
        }

        public async ValueTask Setup()

        {
            await _demuxSocket.AddListener(_endPoint, this);
        }

        public async ValueTask DisposeAsync()
        {
          await _demuxSocket.RemoveListener(_endPoint);
        }
    }
}
