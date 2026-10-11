using System.Diagnostics;
using Okolni.Source.Common;
using Okolni.Source.Query.Test.Fakes;

namespace Okolni.Source.Query.Test;

public class QueryTests
{
    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Info_ParsesAllFields(string kind)
    {
        await using var server = new FakeA2SServer
        {
            Info = new FakeServerInfo
            {
                Name = "Unicode ✓ Server", Map = "Barren", AppId = 0, Players = 50, MaxPlayers = 200, Bots = 2,
                Version = "1.0.0"
            }
        };
        await using var client = QueryClient.Create(kind);

        var info = await client.GetInfoAsync(server.EndPoint);

        Assert.Equal(Constants.A2S_INFO_RESPONSE, info.Header);
        Assert.Equal(17, info.Protocol);
        Assert.Equal("Unicode ✓ Server", info.Name);
        Assert.Equal("Barren", info.Map);
        Assert.Equal("rust", info.Folder);
        Assert.Equal("Rust", info.Game);
        Assert.Equal(50, info.Players);
        Assert.Equal(200, info.MaxPlayers);
        Assert.Equal(2, info.Bots);
        Assert.Equal(Enums.ServerType.Dedicated, info.ServerType);
        Assert.Equal(Enums.Environment.Linux, info.Environment);
        Assert.Equal(Enums.Visibility.Public, info.Visibility);
        Assert.True(info.VAC);
        Assert.Equal("1.0.0", info.Version);
        Assert.Null(info.EDF);
        Assert.False(info.HasPort);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Info_ParsesExtraDataFlags(string kind)
    {
        await using var server = new FakeA2SServer
        {
            Info = new FakeServerInfo
            {
                Port = 28015, SteamId = 90071992547409920UL, SourceTvPort = 27020, SourceTvName = "tv",
                Keywords = "pve,monthly", GameId = 252490
            }
        };
        await using var client = QueryClient.Create(kind);

        var info = await client.GetInfoAsync(server.EndPoint);

        Assert.Equal((ushort)28015, info.Port);
        Assert.Equal(90071992547409920UL, info.SteamID);
        Assert.Equal((ushort)27020, info.SourceTvPort);
        Assert.Equal("tv", info.SourceTvName);
        Assert.Equal("pve,monthly", info.KeyWords);
        Assert.Equal(252490UL, info.GameID);
        Assert.True(info.HasPort && info.HasSteamID && info.HasSourceTv && info.HasKeywords && info.HasGameID);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Info_AnswersChallengeWhenServerRequiresOne(string kind)
    {
        await using var server = new FakeA2SServer { InfoRequiresChallenge = true };
        await using var client = QueryClient.Create(kind);

        var info = await client.GetInfoAsync(server.EndPoint);

        Assert.Equal("Fake Server", info.Name);
        Assert.Equal(2, server.ReceivedRequests.Count);
        // The retry carries the server's challenge appended after the normal 25 byte request
        Assert.Equal(29, server.ReceivedRequests.Last().Length);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Players_ParsesNamesScoresAndDurations(string kind)
    {
        await using var server = new FakeA2SServer
        {
            Players = [new("alice", 10, 60f), new("", 0, 5f), new("chloé", 1234, 3600.5f)]
        };
        await using var client = QueryClient.Create(kind);

        var response = await client.GetPlayersAsync(server.EndPoint);

        Assert.Equal(Constants.A2S_PLAYER_RESPONSE, response.Header);
        Assert.Collection(response.Players,
            p =>
            {
                Assert.Equal("alice", p.Name);
                Assert.Equal(10u, p.Score);
                Assert.Equal(TimeSpan.FromSeconds(60), p.Duration);
            },
            p => Assert.Equal("", p.Name),
            p =>
            {
                Assert.Equal("chloé", p.Name);
                Assert.Equal(1234u, p.Score);
                Assert.Equal(TimeSpan.FromSeconds(3600.5), p.Duration);
            });
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Players_HandlesMoreThan255Players(string kind)
    {
        // Rust servers report the count byte mod 256, but list every player. Split, as real servers do for big lists.
        await using var server = new FakeA2SServer
        {
            Players = Enumerable.Range(0, 550).Select(i => new FakePlayer($"player{i}", i, i)).ToList(),
            MultiPacketSize = 1200
        };
        await using var client = QueryClient.Create(kind);

        var response = await client.GetPlayersAsync(server.EndPoint);

        Assert.Equal(550, response.Players.Count);
        Assert.Equal("player549", response.Players[^1].Name);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Rules_ParsesSimpleResponse(string kind)
    {
        await using var server = new FakeA2SServer();
        await using var client = QueryClient.Create(kind);

        var response = await client.GetRulesAsync(server.EndPoint);

        Assert.Equal(Constants.A2S_RULES_RESPONSE, response.Header);
        Assert.Equal(new Dictionary<string, string> { ["pve"] = "False", ["fps"] = "60" }, response.Rules);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task Rules_ReassemblesOutOfOrderMultiPacketResponse(string kind)
    {
        var rules = Enumerable.Range(0, 300).ToDictionary(i => $"description_{i:000}", i => new string('x', 40) + i);
        await using var server = new FakeA2SServer { Rules = rules, MultiPacketSize = 1000, ReverseFragments = true };
        await using var client = QueryClient.Create(kind);

        var response = await client.GetRulesAsync(server.EndPoint);

        Assert.Equal(rules, response.Rules);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task WrongResponseType_IsRetried(string kind)
    {
        await using var server = new FakeA2SServer { WrongHeaderResponses = 1 };
        await using var client = QueryClient.Create(kind);

        var response = await client.GetRulesAsync(server.EndPoint);

        Assert.Equal(Constants.A2S_RULES_RESPONSE, response.Header);
        Assert.True(response.Retries > 0);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task WrongResponseTypeEveryTime_FailsAfterMaxRetries(string kind)
    {
        await using var server = new FakeA2SServer { WrongHeaderResponses = int.MaxValue };
        await using var client = QueryClient.Create(kind);

        await Assert.ThrowsAsync<SourceQueryException>(() => client.GetPlayersAsync(server.EndPoint, maxRetries: 3));
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task SilentServer_TimesOutAfterRetries(string kind)
    {
        await using var server = new FakeA2SServer { Silent = true };
        await using var client = QueryClient.Create(kind);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<SourceQueryException>(() => client.GetInfoAsync(server.EndPoint, maxRetries: 2));

        Assert.Equal(2, server.ReceivedRequests.Count);
        Assert.InRange(stopwatch.ElapsedMilliseconds, QueryClient.TimeoutMs, QueryClient.TimeoutMs * 2 * 4);
    }
}
