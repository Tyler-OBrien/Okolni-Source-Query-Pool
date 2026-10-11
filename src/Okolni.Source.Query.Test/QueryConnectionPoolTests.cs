using Okolni.Source.Common;
using System.Net;
using Okolni.Source.Query.Source;
using Okolni.Source.Query.Test.Fakes;

namespace Okolni.Source.Query.Test;

public class QueryConnectionPoolTests
{
    private static QueryConnectionPool CreatePool()
    {
        var pool = new QueryConnectionPool();
        pool.ReceiveTimeout = QueryClient.TimeoutMs;
        pool.SendTimeout = QueryClient.TimeoutMs;
        pool.Setup();
        return pool;
    }

    [Fact]
    public async Task ConcurrentQueries_EachGetTheirOwnServersResponse()
    {
        var servers = Enumerable.Range(0, 20)
            .Select(i => new FakeA2SServer
            {
                Info = new FakeServerInfo { Name = $"server-{i}" },
                Rules = new Dictionary<string, string> { ["id"] = i.ToString() },
                InfoRequiresChallenge = i % 2 == 0
            })
            .ToList();
        var pool = CreatePool();
        try
        {
            var infos = await Task.WhenAll(servers.Select(s => pool.GetInfoAsync(s.EndPoint)));
            var rules = await Task.WhenAll(servers.Select(s => pool.GetRulesAsync(s.EndPoint)));

            for (var i = 0; i < servers.Count; i++)
            {
                Assert.Equal($"server-{i}", infos[i].Name);
                Assert.Equal(i.ToString(), rules[i].Rules["id"]);
            }
        }
        finally
        {
            await pool.DisposeAsync(5);
            foreach (var server in servers)
                await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Query_DoesNotModifyCallersEndPoint()
    {
        await using var server = new FakeA2SServer();
        var endPoint = new IPEndPoint(IPAddress.Loopback, server.EndPoint.Port);
        var pool = CreatePool();
        try
        {
            await pool.GetInfoAsync(endPoint);

            Assert.Equal(IPAddress.Loopback, endPoint.Address);
        }
        finally
        {
            await pool.DisposeAsync(5);
        }
    }

    [Fact]
    public async Task TimeoutsSetBeforeSetup_AreKept()
    {
        var pool = new QueryConnectionPool();
        pool.ReceiveTimeout = 4321;
        pool.SendTimeout = 1234;
        pool.Setup();
        try
        {
            Assert.Equal(4321, pool.ReceiveTimeout);
            Assert.Equal(1234, pool.SendTimeout);
        }
        finally
        {
            await pool.DisposeAsync(5);
        }
    }

    [Fact]
    public async Task WaitingForResponse_CountsPendingQueries()
    {
        await using var server = new FakeA2SServer { Silent = true };
        var pool = CreatePool();
        try
        {
            Assert.Equal(0, pool.WaitingForResponse);
            var query = pool.GetInfoAsync(server.EndPoint, maxRetries: 1);

            await WaitUntil(() => pool.WaitingForResponse == 1);
            Assert.Equal(1, pool.Running);

            await Assert.ThrowsAsync<SourceQueryException>(() => query);
            Assert.Equal(0, pool.WaitingForResponse);
            Assert.Equal(0, pool.Running);
        }
        finally
        {
            await pool.DisposeAsync(5);
        }
    }

    [Fact]
    public async Task Setup_CanOnlyBeCalledOnce()
    {
        var pool = CreatePool();
        try
        {
            Assert.Throws<InvalidOperationException>(() => pool.Setup());
        }
        finally
        {
            await pool.DisposeAsync(5);
        }
    }

    internal static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
