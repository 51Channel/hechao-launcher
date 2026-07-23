using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hechao.StatusCollector.Tests;

public sealed class MinecraftStatusClientTests
{
    [Fact]
    public async Task QueryAsync_ReadsMinecraftStatusResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var serverTask = ServeStatusAsync(
            listener,
            """
            {"version":{"name":"Paper 1.21.11","protocol":774},"players":{"max":300,"online":42}}
            """);

        var client = new MinecraftStatusClient();
        var status = await client.QueryAsync(
            IPAddress.Loopback.ToString(),
            endpoint.Port,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        await serverTask;

        Assert.Equal(42, status.OnlinePlayers);
        Assert.Equal(300, status.MaxPlayers);
        Assert.Equal("Paper 1.21.11", status.SoftwareVersion);
        Assert.Equal(774, status.ProtocolVersion);
    }

    [Fact]
    public async Task QueryAsync_RejectsInvalidPlayerCounts()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var serverTask = ServeStatusAsync(
            listener,
            """{"players":{"max":20,"online":21}}""");

        var client = new MinecraftStatusClient();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.QueryAsync(
                IPAddress.Loopback.ToString(),
                endpoint.Port,
                TimeSpan.FromSeconds(2),
                CancellationToken.None));
        await serverTask;
    }

    private static async Task ServeStatusAsync(TcpListener listener, string json)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();

        var handshakeLength = await ReadVarIntAsync(stream);
        await ReadExactlyAsync(stream, handshakeLength);
        var statusRequestLength = await ReadVarIntAsync(stream);
        await ReadExactlyAsync(stream, statusRequestLength);

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0);
        WriteVarInt(payload, jsonBytes.Length);
        payload.Write(jsonBytes);

        using var packet = new MemoryStream();
        WriteVarInt(packet, checked((int)payload.Length));
        payload.Position = 0;
        await payload.CopyToAsync(packet);
        await stream.WriteAsync(packet.ToArray());
    }

    private static async Task<int> ReadVarIntAsync(Stream stream)
    {
        var value = 0;
        for (var index = 0; index < 5; index++)
        {
            var bytes = await ReadExactlyAsync(stream, 1);
            var current = bytes[0];
            value |= (current & 0x7f) << (7 * index);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException();
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length)
    {
        var result = new byte[length];
        await stream.ReadExactlyAsync(result);
        return result;
    }

    private static void WriteVarInt(Stream stream, int value)
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
}
