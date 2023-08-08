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

    public ValueTask<Memory<byte>> ReceiveFromAsync(DemuxSocketWrapper socketWrapper, SocketFlags socketFlags, EndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        return m_udpDeMultiplexer.ReceiveFromAsync(socketWrapper, socketFlags, remoteEndPoint, cancellationToken);
    }



    public ValueTask AddListener(IPEndPoint endPoint, DemuxSocketWrapper wrapper)
    {
        m_udpDeMultiplexer.AddListener(endPoint, wrapper);
        return ValueTask.CompletedTask; 
    }

    public ValueTask RemoveListener(IPEndPoint endPoint)
    {
        m_udpDeMultiplexer.RemoveListener(endPoint);
        return ValueTask.CompletedTask;
    }
}