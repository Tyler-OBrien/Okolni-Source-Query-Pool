using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Query.Pool.Common.SocketHelpers;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Query.Common.SocketHelpers;

public class DemuxSocket
{
    private readonly UDPDeMultiplexer m_udpDeMultiplexer;
    private readonly Socket m_socket;
    private readonly OnSendCallBack m_sendCallBack;
    private readonly OnReceiveCallBack m_receiveCallback;

    public delegate void OnSendCallBack();
    public delegate void OnReceiveCallBack();


    public DemuxSocket(Socket socket, UDPDeMultiplexer udpDeMultiplexer)
    {
        m_socket = socket;
        m_udpDeMultiplexer = udpDeMultiplexer;
    }

    public DemuxSocket(Socket socket, UDPDeMultiplexer udpDeMultiplexer, OnReceiveCallBack onReceiveCallBack)
    {
        m_socket = socket;
        m_udpDeMultiplexer = udpDeMultiplexer;
        m_receiveCallback = onReceiveCallBack;
    }

    public DemuxSocket(Socket socket, UDPDeMultiplexer udpDeMultiplexer, OnSendCallBack onSendCallBack, OnReceiveCallBack onReceiveCallBack)
    {
        m_socket = socket;
        m_udpDeMultiplexer = udpDeMultiplexer;
        m_receiveCallback = onReceiveCallBack;
        m_sendCallBack = onSendCallBack;
    }

    /// <inheritdoc />
    public ValueTask<int> SendToAsync(
        ReadOnlyMemory<byte> buffer,
        SocketFlags socketFlags,
        EndPoint remoteEP,
        CancellationToken cancellationToken = default)
    {
#if DEBUG
        Console.WriteLine($"Sending  {Convert.ToBase64String(buffer.ToArray().Take(10).ToArray())} to {remoteEP}");
#endif
        m_sendCallBack?.Invoke();
        return m_socket.SendToAsync(buffer, socketFlags, remoteEP, cancellationToken);
    }




    public async ValueTask AddListener(IPEndPoint endPoint, DemuxSocketWrapper wrapper)
    {
        if (m_udpDeMultiplexer.Connections.ContainsKey(endPoint))
            throw new InvalidOperationException("Only one listener per endpoint active at one time...");

        m_udpDeMultiplexer.Connections[endPoint] = wrapper;
    }

    public async ValueTask RemoveListener(IPEndPoint endPoint)
    {
        if (m_udpDeMultiplexer.Connections.TryRemove(endPoint, out _) == false)
            throw new InvalidOperationException("Failed to remove endpoint listener, already gone?");
    }
}