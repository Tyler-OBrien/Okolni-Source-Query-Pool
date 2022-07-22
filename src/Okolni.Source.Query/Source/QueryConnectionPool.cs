using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Common;
using Okolni.Source.Query.Common;
using Okolni.Source.Query.Common.SocketHelpers;
using Okolni.Source.Query.Responses;

namespace Okolni.Source.Query.Source;
/*
 * This is really work in progress
 * The basic idea was just to use a single socket instead of potentially thousands (depending on how many queries you are making)... Since we are just sending out a few UDP Packets and getting a few back, we don't really need a dedicated socket or anything, really short life time & short packet lengths.
 * In order to make that work, we needed a way to still allow async (not callbacks or anything), receive the right responses to the right endpoints
 * I don't think there's any native way to do this with the .NET Socket API. I tried giving it a certain IP Endpoint in the ReceiveFromAsync method, but it would still get responses made for other endpoints
 * There's probably a better way to do this, and this is really messy right now, but it does seem to work without issue.
 */
public class QueryConnectionPool : IQueryConnectionPool, IDisposable
{
    private readonly UDPDeMultiplexer m_demultiplexer;
    private readonly Socket m_sharedSocket;
    private readonly DemuxSocket m_socket;
    private readonly CancellationTokenSource m_cancellationTokenSource;

    private Task m_backgroundTask;

    private IPEndPoint m_endPoint;



    public event IQueryConnectionPool.PoolError Error;

    public event IQueryConnectionPool.PoolMessage Message;

    public QueryConnectionPool()
    {
        m_cancellationTokenSource = new CancellationTokenSource();
        // This only supports IPv4, but right now, so does Steam.
        m_sharedSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/questions/38191968/c-sharp-udp-an-existing-connection-was-forcibly-closed-by-the-remote-host
            //https://stackoverflow.com/questions/7201862/an-existing-connection-was-forcibly-closed-by-the-remote-host
            // Windows only issue, the UDP socket is also receiving ICMP messages and throwing exceptions when they are received. Some hosts may respond with ICMP Host unreachable if no listener is on that port.
            // Microsoft Article 263823 
            uint IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            m_sharedSocket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
        }
        m_endPoint = new IPEndPoint(IPAddress.Any, 0);
        //m_sharedSocket.Bind(m_endPoint);
        SendTimeout = 1000;
        ReceiveTimeout = 1000;
        m_demultiplexer = new UDPDeMultiplexer();
        m_socket = new DemuxSocket(m_sharedSocket, m_demultiplexer, EnsureBackgroundTaskIsRunning);
    }

    public int SendTimeout { get; set; }
    public int ReceiveTimeout { get; set; }


    internal void EnsureBackgroundTaskIsRunning()
    {
        if (m_backgroundTask == null || m_backgroundTask.IsCompleted || m_backgroundTask.IsCanceled ||
            m_backgroundTask.IsFaulted)
        {
            m_backgroundTask = m_demultiplexer.Start(m_sharedSocket, m_endPoint, m_cancellationTokenSource.Token)
                .ContinueWith(t =>
                    {
                        // If there's nothing listening, bubble up
                        if (Error == null)
                        {
                            throw new SourceQueryException(
                                "Unexpected issue with background service for QueryConnectionPool", t.Exception);
                        }

                        Error?.Invoke(t.Exception);
                    },
                    TaskContinuationOptions.OnlyOnFaulted)
                .ContinueWith(t => { Message?.Invoke($"Worker exited safely {t.Status}"); },
                    TaskContinuationOptions.OnlyOnRanToCompletion);
            Message?.Invoke($"Starting Background worker for UDP Socket {m_endPoint}");
        }
    }



    /// <summary>
    ///     Gets the servers general information
    /// </summary>
    /// <returns>InfoResponse containing all Infos</returns>
    /// <exception cref="SourceQueryException"></exception>
    public InfoResponse GetInfo(IPEndPoint endpoint, int maxRetries = 10)
    {
        return GetInfoAsync(endpoint, maxRetries).GetAwaiter().GetResult();
    }


    /// <summary>
    ///     Gets the servers general information
    /// </summary>
    /// <returns>InfoResponse containing all Infos</returns>
    /// <exception cref="SourceQueryException"></exception>
    public Task<InfoResponse> GetInfoAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        return QueryHelper.GetInfoAsync(endpoint, m_socket, SendTimeout, ReceiveTimeout, maxRetries);
    }


    /// <summary>
    ///     Gets all active players on a server
    /// </summary>
    /// <returns>PlayerResponse containing all players </returns>
    /// <exception cref="SourceQueryException"></exception>
    public PlayerResponse GetPlayers(IPEndPoint endpoint, int maxRetries = 10)
    {
        return GetPlayersAsync(endpoint, maxRetries).GetAwaiter().GetResult();
    }


    /// <summary>
    ///     Gets all active players on a server
    /// </summary>
    /// <returns>PlayerResponse containing all players </returns>
    /// <exception cref="SourceQueryException"></exception>
    public Task<PlayerResponse> GetPlayersAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        return QueryHelper.GetPlayersAsync(endpoint, m_socket, SendTimeout, ReceiveTimeout, maxRetries);
    }


    /// <summary>
    ///     Gets the rules of the server
    /// </summary>
    /// <returns>RuleResponse containing all rules as a Dictionary</returns>
    /// <exception cref="SourceQueryException"></exception>
    public RuleResponse GetRules(IPEndPoint endpoint, int maxRetries = 10)
    {
        return GetRulesAsync(endpoint, maxRetries).GetAwaiter().GetResult();
    }


    /// <summary>
    ///     Gets the rules of the server
    /// </summary>
    /// <returns>RuleResponse containing all rules as a Dictionary</returns>
    /// <exception cref="SourceQueryException"></exception>
    public Task<RuleResponse> GetRulesAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        return QueryHelper.GetRulesAsync(endpoint, m_socket, SendTimeout, ReceiveTimeout, maxRetries);
    }

    public void Dispose()
    {
        if (!m_cancellationTokenSource.IsCancellationRequested)
            m_cancellationTokenSource?.Cancel();
        m_sharedSocket?.Dispose();
    }
}