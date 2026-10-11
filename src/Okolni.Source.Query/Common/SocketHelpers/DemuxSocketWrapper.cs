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
        private bool _registered;

        public CancellationToken CancellationToken;

        public TaskCompletionSource<ArrayPoolMemory> ReceiveFrom;

        public Queue<ArrayPoolMemory> Queue { get; private set; } =  new Queue<ArrayPoolMemory>();

        public DemuxSocketWrapper(DemuxSocket demuxSocket, IPEndPoint endPoint)
        {
            // The shared socket is dual mode and reports senders as IPv6 mapped addresses, so listen on that form.
            // Use a copy so the caller's endpoint isn't changed
            _demuxSocket = demuxSocket;
            _endPoint = new IPEndPoint(endPoint.Address.MapToIPv6(), endPoint.Port);
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
            _registered = true;
        }

        public async ValueTask DisposeAsync()
        {
            // If Setup failed (another query already listens on this endpoint), the listener isn't ours to remove
            if (!_registered)
                return;
            _registered = false;
            await _demuxSocket.RemoveListener(_endPoint);
        }
    }
}
