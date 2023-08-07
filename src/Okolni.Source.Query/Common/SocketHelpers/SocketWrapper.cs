using System;
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
    public async ValueTask<Memory<byte>> ReceiveFromAsync(
        SocketFlags socketFlags,
        EndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        Memory<byte> buffer = new byte[65527];
        var response = await m_socket.ReceiveFromAsync(buffer, socketFlags, remoteEndPoint, cancellationToken);
        return buffer.Slice(0, response.ReceivedBytes);
    }

    // Not useful for this...
    public async ValueTask Setup()
    {
    }

    public async ValueTask DisposeAsync()
    {
    }
}