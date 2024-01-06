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

        public TaskCompletionSource<ArrayPoolMemory> ReceiveFrom;

        public Queue<ArrayPoolMemory> Queue { get; private set; } =  new Queue<ArrayPoolMemory>();

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

        public ValueTask<ArrayPoolMemory> ReceiveFromAsync(SocketFlags socketFlags, EndPoint remoteEndPoint,
            CancellationToken cancellationToken = default)
        {
            return _demuxSocket.ReceiveFromAsync(this, socketFlags, remoteEndPoint, cancellationToken);
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
