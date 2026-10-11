using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Pool.Common.SocketHelpers;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Okolni.Source.Query.Common.SocketHelpers;

public class SocketWrapper : ISocket
{
    private readonly Socket m_socket;

    public SocketWrapper(Socket socket)
    {
        m_socket = socket;
        m_socket.ReceiveTimeout = 5000;
        m_socket.SendTimeout = 5000;
    }

    /// <inheritdoc />
    public ValueTask<int> SendToAsync(
        ReadOnlyMemory<byte> buffer,
        SocketFlags socketFlags,
        EndPoint remoteEP,
        CancellationToken cancellationToken = default)
    {
        return m_socket.SendToAsync(buffer, socketFlags, remoteEP, cancellationToken);
    }


    /// <inheritdoc />
    public async ValueTask<ArrayPoolMemory> ReceiveFromAsync(
        SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPoolInterface.Rent(65527);
        try
        {
            var response = await m_socket.ReceiveFromAsync(buffer, socketFlags, remoteEndPoint, cancellationToken);
            var newBuffer = ArrayPoolInterface.Rent(response.ReceivedBytes);
            Buffer.BlockCopy(buffer, 0, newBuffer, 0, response.ReceivedBytes);
            return new ArrayPoolMemory(newBuffer, response.ReceivedBytes);
        }
        finally
        {
            // Also return the buffer when the receive times out or is cancelled
            ArrayPoolInterface.Return(buffer);
        }
    }

    // Not useful for this...
    public async ValueTask Setup()
    {
    }

    public async ValueTask DisposeAsync()
    {
    }
}