using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Okolni.Source.Query.Test.Fakes;

/// <summary>
///     A minimal Source game server answering A2S queries on 127.0.0.1, so tests never touch the internet.
/// </summary>
public sealed class FakeA2SServer : IAsyncDisposable
{
    private const int ChallengeValue = 0x1234ABCD;

    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _wrongHeadersSent;

    public FakeA2SServer()
    {
        _loop = Task.Run(RunAsync);
    }

    public IPEndPoint EndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public FakeServerInfo Info { get; set; } = new();
    public List<FakePlayer> Players { get; set; } = [new("alice", 10, 60f), new("bob", 3, 125.5f)];
    public Dictionary<string, string> Rules { get; set; } = new() { ["pve"] = "False", ["fps"] = "60" };

    /// <summary>Reply to A2S_INFO with a challenge first, as newer Source servers do.</summary>
    public bool InfoRequiresChallenge { get; set; }

    /// <summary>Never reply to anything.</summary>
    public bool Silent { get; set; }

    /// <summary>If set, responses larger than this are split into multi-packet fragments of this payload size.</summary>
    public int? MultiPacketSize { get; set; }

    /// <summary>Send multi-packet fragments in reverse order, to check reassembly orders by packet number.</summary>
    public bool ReverseFragments { get; set; }

    /// <summary>
    ///     Answer this many player/rules requests with an A2S_INFO packet first (seen from some DDoS-protected hosts).
    /// </summary>
    public int WrongHeaderResponses { get; set; }

    public ConcurrentQueue<byte[]> ReceivedRequests { get; } = new();

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            ReceivedRequests.Enqueue(received.Buffer);
            if (Silent)
                continue;

            foreach (var packet in Respond(received.Buffer))
                await _udp.SendAsync(packet, received.RemoteEndPoint, _cts.Token);
        }
    }

    private IEnumerable<byte[]> Respond(byte[] request)
    {
        if (request.Length < 5)
            return [];

        var header = request[4];
        switch (header)
        {
            case A2SPackets.InfoRequestHeader:
                // Info request is 25 bytes, plus 4 for the challenge when we asked for one
                if (InfoRequiresChallenge && !HasValidChallenge(request, 25))
                    return [A2SPackets.Challenge(ChallengeValue)];
                return Split(A2SPackets.Info(Info));

            case A2SPackets.PlayerRequestHeader:
            case A2SPackets.RulesRequestHeader:
                if (!HasValidChallenge(request, 5))
                    return [A2SPackets.Challenge(ChallengeValue)];
                if (Interlocked.Increment(ref _wrongHeadersSent) <= WrongHeaderResponses)
                    return [A2SPackets.Info(Info)];
                return Split(header == A2SPackets.PlayerRequestHeader
                    ? A2SPackets.Players(Players)
                    : A2SPackets.Rules(Rules));

            default:
                return [];
        }
    }

    private static bool HasValidChallenge(byte[] request, int challengeOffset)
    {
        return request.Length >= challengeOffset + 4 &&
               BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(challengeOffset, 4)) == ChallengeValue;
    }

    private IEnumerable<byte[]> Split(byte[] packet)
    {
        if (MultiPacketSize is not { } size || packet.Length <= size)
            return [packet];

        var fragments = A2SPackets.SplitMultiPacket(packet, size, Random.Shared.Next(1, int.MaxValue));
        if (ReverseFragments)
            fragments.Reverse();
        return fragments;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _udp.Dispose();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
            // Shutting down
        }

        _cts.Dispose();
    }
}
