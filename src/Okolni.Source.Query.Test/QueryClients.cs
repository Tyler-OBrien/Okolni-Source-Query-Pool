using System.Net;
using Okolni.Source.Query.Responses;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Query.Test;

/// <summary>
///     Runs the same scenarios through both the single-socket <see cref="QueryConnection" /> and the shared-socket
///     <see cref="QueryConnectionPool" />.
/// </summary>
public abstract class QueryClient : IAsyncDisposable
{
    public const int TimeoutMs = 500;

    public static TheoryData<string> Kinds => ["connection", "pool"];

    public static QueryClient Create(string kind)
    {
        return kind switch
        {
            "connection" => new ConnectionClient(),
            "pool" => new PoolClient(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public abstract Task<InfoResponse> GetInfoAsync(IPEndPoint endPoint, int maxRetries = 10);
    public abstract Task<PlayerResponse> GetPlayersAsync(IPEndPoint endPoint, int maxRetries = 10);
    public abstract Task<RuleResponse> GetRulesAsync(IPEndPoint endPoint, int maxRetries = 10);
    public abstract ValueTask DisposeAsync();

    private sealed class ConnectionClient : QueryClient
    {
        private readonly List<QueryConnection> _connections = new();

        private QueryConnection Connect(IPEndPoint endPoint)
        {
            var connection = new QueryConnection
            {
                Host = endPoint.Address.ToString(),
                Port = endPoint.Port,
                ReceiveTimeout = TimeoutMs,
                SendTimeout = TimeoutMs
            };
            connection.Setup();
            _connections.Add(connection);
            return connection;
        }

        public override Task<InfoResponse> GetInfoAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Connect(endPoint).GetInfoAsync(maxRetries);

        public override Task<PlayerResponse> GetPlayersAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Connect(endPoint).GetPlayersAsync(maxRetries);

        public override Task<RuleResponse> GetRulesAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Connect(endPoint).GetRulesAsync(maxRetries);

        public override ValueTask DisposeAsync()
        {
            foreach (var connection in _connections)
                connection.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PoolClient : QueryClient
    {
        public PoolClient()
        {
            Pool = new QueryConnectionPool();
            Pool.ReceiveTimeout = TimeoutMs;
            Pool.SendTimeout = TimeoutMs;
            Pool.Setup();
        }

        public QueryConnectionPool Pool { get; }

        public override Task<InfoResponse> GetInfoAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Pool.GetInfoAsync(endPoint, maxRetries);

        public override Task<PlayerResponse> GetPlayersAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Pool.GetPlayersAsync(endPoint, maxRetries);

        public override Task<RuleResponse> GetRulesAsync(IPEndPoint endPoint, int maxRetries = 10) =>
            Pool.GetRulesAsync(endPoint, maxRetries);

        public override async ValueTask DisposeAsync() => await Pool.DisposeAsync(5);
    }
}
