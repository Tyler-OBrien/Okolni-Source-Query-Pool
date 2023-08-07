using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Common;
using Okolni.Source.Query.Common;
using Okolni.Source.Query.Common.SocketHelpers;
using Okolni.Source.Query.Pool.Common.SocketHelpers;
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
    private readonly CancellationTokenSource m_cancellationTokenSource;
    private readonly UDPDeMultiplexer m_demultiplexer;
    private readonly Socket m_sharedSocket;
    private int _running;

    private Task m_backgroundTask;

    private IPEndPoint m_endPoint;

    private bool m_init;
    private DemuxSocket m_socket;


    public QueryConnectionPool(bool delayInit = true, CancellationToken token = default)
    {
        if (token != default && token != CancellationToken.None)
            m_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        else 
            m_cancellationTokenSource = new CancellationTokenSource();
        // This only supports IPv4, but right now, so does Steam.
        m_sharedSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        m_sharedSocket.Ttl = 255;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/questions/38191968/c-sharp-udp-an-existing-connection-was-forcibly-closed-by-the-remote-host
            //https://stackoverflow.com/questions/7201862/an-existing-connection-was-forcibly-closed-by-the-remote-host
            // Windows only issue, the UDP socket is also receiving ICMP messages and throwing exceptions when they are received. Some hosts may respond with ICMP Host unreachable if no listener is on that port.
            // Microsoft Article 263823 
            var IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            var SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            m_sharedSocket.IOControl((int)SIO_UDP_CONNRESET, new[] { Convert.ToByte(false) }, null);
            const int SIO_UDP_CONNRESET_2 = -1744830452;
            m_sharedSocket.IOControl(
                (IOControlCode)SIO_UDP_CONNRESET_2,
                new byte[] { 0, 0, 0, 0 },
                null
            );
        }

        m_demultiplexer = new UDPDeMultiplexer();

        if (delayInit == false) Init();
    }
    /// <inheritdoc />
    public int WaitingForResponse => m_demultiplexer.GetWaitingConnections();
    /// <inheritdoc />
    public int Running => _running;


    /// <inheritdoc />
    public void Setup()
    {
        if (m_init)
            throw new InvalidOperationException(
                "The socket, background worker and related resources have already been created. If you want to use this, pass true in the constructor for delayInit");

        Init();
    }

    /// <inheritdoc />
    public event IQueryConnectionPool.PoolError Error;

    /// <inheritdoc />

    public event IQueryConnectionPool.PoolMessage Message;

    public int SendTimeout { get; set; }
    public int ReceiveTimeout { get; set; }


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
    public async Task<InfoResponse> GetInfoAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        try
        {
            Interlocked.Increment(ref _running);
            return await QueryHelper.GetInfoAsync(endpoint, NewSocket(endpoint), SendTimeout, ReceiveTimeout, maxRetries);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
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
    public async Task<PlayerResponse> GetPlayersAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        try
        {
            Interlocked.Increment(ref _running);
            return await QueryHelper.GetPlayersAsync(endpoint, NewSocket(endpoint), SendTimeout, ReceiveTimeout, maxRetries);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
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
    public async Task<RuleResponse> GetRulesAsync(IPEndPoint endpoint, int maxRetries = 10)
    {
        try
        {
            Interlocked.Increment(ref _running);
            return await QueryHelper.GetRulesAsync(endpoint, NewSocket(endpoint), SendTimeout, ReceiveTimeout, maxRetries);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    [Obsolete("Use Async Dispose")]
    public void Dispose()
    {
        if (m_cancellationTokenSource is { IsCancellationRequested: false })
            m_cancellationTokenSource.Cancel();
        m_cancellationTokenSource?.Dispose();
    }

    public Task DisposeAsync(int timeoutSeconds = 20) => DisposeAsync(CancellationToken.None, timeoutSeconds);
    public async Task DisposeAsync(CancellationToken token, int timeoutSeconds = 20)
    {
        if (m_cancellationTokenSource is { IsCancellationRequested: false })
            m_cancellationTokenSource.Cancel();
        m_cancellationTokenSource?.Dispose();
        try
        {
            await m_backgroundTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), token);
        }
        finally
        {
            m_backgroundTask.Dispose();
        }
        Message?.Invoke("Disposed pool..");
    }

    public DemuxSocketWrapper NewSocket(IPEndPoint endPoint)
    {
        return new DemuxSocketWrapper(m_socket, endPoint);
    }


    private void Init()
    {
        m_endPoint = new IPEndPoint(IPAddress.Any, 0);
        m_sharedSocket.Bind(m_endPoint);
        SendTimeout = 1000;
        ReceiveTimeout = 1000;
        m_socket = new DemuxSocket(m_sharedSocket, m_demultiplexer);
        RunWorker();
        m_init = true;
    }


    internal void RunWorker()
    {
        m_backgroundTask = Task.Factory.StartNew(
            () =>
            {
                m_demultiplexer.Start(m_sharedSocket, m_endPoint,
                        m_cancellationTokenSource.Token).ContinueWith(t =>
                        {
                            // If there's nothing listening, bubble up
                            if (Error == null && t.Exception?.InnerException is not OperationCanceledException)
                                throw new SourceQueryException(
                                    "Unexpected issue with background service for QueryConnectionPool",
                                    t.Exception);

                            Error?.Invoke(t.Exception);
                        },
                        TaskContinuationOptions.OnlyOnFaulted)
                    .ContinueWith(t => { Message?.Invoke($"Worker exited safely {t.Status}"); },
                        TaskContinuationOptions.OnlyOnRanToCompletion);
                return m_backgroundTask;
            },
            m_cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Message?.Invoke($"Starting Background worker for UDP Socket {m_sharedSocket.LocalEndPoint}");
    }
}