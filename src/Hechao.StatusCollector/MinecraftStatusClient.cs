using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Hechao.StatusCollector;

public interface IMinecraftStatusClient
{
    Task<MinecraftServerStatus> QueryAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record MinecraftServerStatus(
    int OnlinePlayers,
    int MaxPlayers,
    string? SoftwareVersion,
    int? ProtocolVersion);

public sealed class MinecraftStatusClient : IMinecraftStatusClient
{
    private const int MaximumPacketBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<MinecraftServerStatus> QueryAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var operationToken = timeoutSource.Token;

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, operationToken);
        await using var stream = client.GetStream();

        var handshakePayload = BuildHandshakePayload(host, port);
        await WritePacketAsync(stream, handshakePayload, operationToken);
        await WritePacketAsync(stream, new byte[] { 0 }, operationToken);

        var packetLength = await ReadVarIntAsync(stream, operationToken);
        if (packetLength is < 1 or > MaximumPacketBytes)
        {
            throw new InvalidDataException("The Minecraft status packet length is invalid.");
        }

        var packet = new byte[packetLength];
        await stream.ReadExactlyAsync(packet, operationToken);
        var offset = 0;
        var packetId = ReadVarInt(packet, ref offset);
        if (packetId != 0)
        {
            throw new InvalidDataException("The Minecraft server returned an unexpected packet.");
        }

        var jsonLength = ReadVarInt(packet, ref offset);
        if (jsonLength < 1 || jsonLength > MaximumPacketBytes || offset + jsonLength != packet.Length)
        {
            throw new InvalidDataException("The Minecraft status JSON length is invalid.");
        }

        var json = StrictUtf8.GetString(packet.AsSpan(offset, jsonLength));
        return ParseStatus(json);
    }

    private static byte[] BuildHandshakePayload(string host, int port)
    {
        var hostBytes = StrictUtf8.GetBytes(host);
        if (hostBytes.Length is < 1 or > 255)
        {
            throw new InvalidDataException("The Minecraft handshake host is invalid.");
        }

        using var payload = new MemoryStream();
        WriteVarInt(payload, 0);
        WriteVarInt(payload, 0);
        WriteVarInt(payload, hostBytes.Length);
        payload.Write(hostBytes);

        Span<byte> portBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, checked((ushort)port));
        payload.Write(portBytes);
        WriteVarInt(payload, 1);
        return payload.ToArray();
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload.Span);
        await stream.WriteAsync(packet.ToArray(), cancellationToken);
    }

    internal static void WriteVarInt(Stream stream, int value)
    {
        var remaining = unchecked((uint)value);
        do
        {
            var next = (byte)(remaining & 0x7f);
            remaining >>= 7;
            if (remaining != 0)
            {
                next |= 0x80;
            }

            stream.WriteByte(next);
        }
        while (remaining != 0);
    }

    internal static async Task<int> ReadVarIntAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var value = 0;
        for (var index = 0; index < 5; index++)
        {
            var buffer = new byte[1];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            var current = buffer[0];
            value |= (current & 0x7f) << (7 * index);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("The Minecraft VarInt is too large.");
    }

    internal static int ReadVarInt(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var value = 0;
        for (var index = 0; index < 5; index++)
        {
            if (offset >= bytes.Length)
            {
                throw new EndOfStreamException();
            }

            var current = bytes[offset++];
            value |= (current & 0x7f) << (7 * index);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("The Minecraft VarInt is too large.");
    }

    private static MinecraftServerStatus ParseStatus(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var players = root.GetProperty("players");
        var onlinePlayers = players.GetProperty("online").GetInt32();
        var maxPlayers = players.GetProperty("max").GetInt32();
        if (onlinePlayers < 0 ||
            maxPlayers is < 1 or > 10000 ||
            onlinePlayers > maxPlayers)
        {
            throw new InvalidDataException("The Minecraft server returned invalid player counts.");
        }

        string? softwareVersion = null;
        int? protocolVersion = null;
        if (root.TryGetProperty("version", out var version))
        {
            if (version.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                softwareVersion = name.GetString();
                if (softwareVersion?.Any(char.IsControl) == true)
                {
                    softwareVersion = null;
                }
                else if (softwareVersion is { Length: > 120 })
                {
                    softwareVersion = softwareVersion[..120];
                }
            }

            if (version.TryGetProperty("protocol", out var protocol) &&
                protocol.TryGetInt32(out var parsedProtocol) &&
                parsedProtocol is >= 0 and <= 100000)
            {
                protocolVersion = parsedProtocol;
            }
        }

        return new MinecraftServerStatus(
            onlinePlayers,
            maxPlayers,
            softwareVersion,
            protocolVersion);
    }
}
