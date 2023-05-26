using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Query.Responses;

namespace Okolni.Source.Query.Source;

public interface IQueryConnectionPool : IDisposable
{
    public delegate void PoolError(Exception ex);

    public delegate void PoolMessage(string msg);

    /// <summary>
    ///     Timeout for sending requests (ms). Default is 1000ms * Keep in mind total execution time is timeout*retries*4 (1
    ///     send auth, 1 receive auth, 1 send challenge, 1 receive result) *
    /// </summary>
    int SendTimeout { get; set; }

    /// <summary>
    ///     Timeout for response from the server (ms). Default is 1000ms * Keep in mind total execution time is
    ///     timeout*retries*4 (1 send auth, 1 receive auth, 1 send challenge, 1 receive result) *
    /// </summary>
    int ReceiveTimeout { get; set; }


    /// <summary>
    ///     Number of Queries waiting for response from the server
    /// </summary>
    public int WaitingForResponse { get; }

    /// <summary>
    ///     Number of running Queries for this pool
    /// </summary>
    public int Running { get; }

    /// <summary>
    ///     Event emitted on any errors the background worker hits. Background worker used for receiving responses from
    ///     queries.
    /// </summary>
    public event PoolError Error;

    /// <summary>
    ///     Event emitted for any messages from the pool about status.
    /// </summary>
    public event PoolMessage Message;

    /// <summary>
    ///     Sets up the socket and background worker for the pool. Only call if you delayed init in the constructor of the
    ///     pool, otherwise will throw error.
    /// </summary>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    void Setup();


    /// <summary>
    ///     Gets the A2S_INFO_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    InfoResponse GetInfo(IPEndPoint endpoint, int maxRetries = 10);

    /// <summary>
    ///     Gets the A2S_PLAYERS_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    PlayerResponse GetPlayers(IPEndPoint endpoint, int maxRetries = 10);

    /// <summary>
    ///     Gets the A2S_RULES_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    RuleResponse GetRules(IPEndPoint endpoint, int maxRetries = 10);


    /// <summary>
    ///     Gets the A2S_INFO_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    Task<InfoResponse> GetInfoAsync(IPEndPoint endpoint, int maxRetries = 10);

    /// <summary>
    ///     Gets the A2S_PLAYERS_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    Task<PlayerResponse> GetPlayersAsync(IPEndPoint endpoint, int maxRetries = 10);

    /// <summary>
    ///     Gets the A2S_RULES_RESPONSE from the server
    /// </summary>
    /// <param name="maxRetries">How often the get info should be retried if the server responds with a challenge request</param>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="TimeoutException"></exception>
    Task<RuleResponse> GetRulesAsync(IPEndPoint endpoint, int maxRetries = 10);


    public Task DisposeAsync(int timeoutSeconds = 20) => DisposeAsync(CancellationToken.None, timeoutSeconds);
    public Task DisposeAsync(CancellationToken token, int timeoutSeconds = 20);
}