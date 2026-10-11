using System.Buffers.Binary;
using System.Text;

namespace Okolni.Source.Query.Test.Fakes;

/// <summary>
///     Builds Source A2S protocol packets, as a real game server would send them.
///     See https://developer.valvesoftware.com/wiki/Server_queries
/// </summary>
public static class A2SPackets
{
    public const byte InfoRequestHeader = 0x54;
    public const byte PlayerRequestHeader = 0x55;
    public const byte RulesRequestHeader = 0x56;

    private static readonly byte[] SimpleHeader = [0xFF, 0xFF, 0xFF, 0xFF];

    public static byte[] Challenge(int challenge)
    {
        var writer = new PacketWriter().Byte(0x41).Int(challenge);
        return Simple(writer);
    }

    public static byte[] Info(FakeServerInfo info)
    {
        var writer = new PacketWriter()
            .Byte(0x49)
            .Byte(info.Protocol)
            .String(info.Name)
            .String(info.Map)
            .String(info.Folder)
            .String(info.Game)
            .UShort(info.AppId)
            .Byte(info.Players)
            .Byte(info.MaxPlayers)
            .Byte(info.Bots)
            .Byte((byte)'d')
            .Byte((byte)'l')
            .Byte(0x00)
            .Byte(0x01)
            .String(info.Version);

        byte edf = 0;
        if (info.Port != null) edf |= 0x80;
        if (info.SteamId != null) edf |= 0x10;
        if (info.SourceTvPort != null) edf |= 0x40;
        if (info.Keywords != null) edf |= 0x20;
        if (info.GameId != null) edf |= 0x01;

        if (edf != 0)
        {
            writer.Byte(edf);
            if (info.Port != null) writer.UShort(info.Port.Value);
            if (info.SteamId != null) writer.ULong(info.SteamId.Value);
            if (info.SourceTvPort != null) writer.UShort(info.SourceTvPort.Value).String(info.SourceTvName ?? "");
            if (info.Keywords != null) writer.String(info.Keywords);
            if (info.GameId != null) writer.ULong(info.GameId.Value);
        }

        return Simple(writer);
    }

    public static byte[] Players(IReadOnlyList<FakePlayer> players)
    {
        // The count is a single byte, so it wraps for servers with more than 255 players (Rust does this)
        var writer = new PacketWriter().Byte(0x44).Byte((byte)(players.Count % 256));
        for (var i = 0; i < players.Count; i++)
            writer.Byte((byte)i).String(players[i].Name).Int(players[i].Score).Float(players[i].DurationSeconds);
        return Simple(writer);
    }

    public static byte[] Rules(IReadOnlyDictionary<string, string> rules)
    {
        var writer = new PacketWriter().Byte(0x45).Short((short)rules.Count);
        foreach (var (key, value) in rules)
            writer.String(key).String(value);
        return Simple(writer);
    }

    /// <summary>
    ///     Splits a full simple packet (including its FF FF FF FF header) into Source multi-packet fragments.
    /// </summary>
    public static List<byte[]> SplitMultiPacket(byte[] simplePacket, int maxPayloadSize, int id)
    {
        var chunks = simplePacket.Chunk(maxPayloadSize).ToList();
        var fragments = new List<byte[]>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var writer = new PacketWriter()
                .Bytes([0xFE, 0xFF, 0xFF, 0xFF])
                .Int(id)
                .Byte((byte)chunks.Count)
                .Byte((byte)i)
                .Short((short)maxPayloadSize)
                .Bytes(chunks[i]);
            fragments.Add(writer.ToArray());
        }

        return fragments;
    }

    private static byte[] Simple(PacketWriter payload)
    {
        return [.. SimpleHeader, .. payload.ToArray()];
    }

    private sealed class PacketWriter
    {
        private readonly List<byte> _bytes = new();

        public PacketWriter Byte(byte value)
        {
            _bytes.Add(value);
            return this;
        }

        public PacketWriter Bytes(IEnumerable<byte> value)
        {
            _bytes.AddRange(value);
            return this;
        }

        public PacketWriter Short(short value) => Write(2, span => BinaryPrimitives.WriteInt16LittleEndian(span, value));
        public PacketWriter UShort(ushort value) => Write(2, span => BinaryPrimitives.WriteUInt16LittleEndian(span, value));
        public PacketWriter Int(int value) => Write(4, span => BinaryPrimitives.WriteInt32LittleEndian(span, value));
        public PacketWriter ULong(ulong value) => Write(8, span => BinaryPrimitives.WriteUInt64LittleEndian(span, value));
        public PacketWriter Float(float value) => Write(4, span => BinaryPrimitives.WriteSingleLittleEndian(span, value));

        public PacketWriter String(string value)
        {
            _bytes.AddRange(Encoding.UTF8.GetBytes(value));
            _bytes.Add(0x00);
            return this;
        }

        public byte[] ToArray() => _bytes.ToArray();

        private delegate void SpanWriter(Span<byte> span);

        private PacketWriter Write(int size, SpanWriter write)
        {
            Span<byte> buffer = stackalloc byte[size];
            write(buffer);
            _bytes.AddRange(buffer.ToArray());
            return this;
        }
    }
}

public record FakeServerInfo
{
    public byte Protocol { get; init; } = 17;
    public string Name { get; init; } = "Fake Server";
    public string Map { get; init; } = "Procedural Map";
    public string Folder { get; init; } = "rust";
    public string Game { get; init; } = "Rust";
    public ushort AppId { get; init; }
    public byte Players { get; init; } = 12;
    public byte MaxPlayers { get; init; } = 100;
    public byte Bots { get; init; }
    public string Version { get; init; } = "2541";
    public ushort? Port { get; init; }
    public ulong? SteamId { get; init; }
    public ushort? SourceTvPort { get; init; }
    public string? SourceTvName { get; init; }
    public string? Keywords { get; init; }
    public ulong? GameId { get; init; }
}

public record FakePlayer(string Name, int Score, float DurationSeconds);
