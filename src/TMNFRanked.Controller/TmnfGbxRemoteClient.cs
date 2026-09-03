using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace TMNFRanked.Controller;

public sealed class TmnfGbxRemoteClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    private uint _requestHandle = 0x80000000;
    private int _protocolVersion;

    public async Task ConnectAsync(string host, int port)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port);

        _stream = _tcpClient.GetStream();

        var handshakeLengthBytes = await ReadExactAsync(4);
        var handshakeLength = BinaryPrimitives.ReadUInt32LittleEndian(handshakeLengthBytes);

        if (handshakeLength > 64)
            throw new InvalidOperationException(
                $"Invalid GbxRemote handshake length: {handshakeLength}");

        var handshakeBytes = await ReadExactAsync((int)handshakeLength);
        var handshake = Encoding.ASCII.GetString(handshakeBytes);

        _protocolVersion = handshake switch
        {
            "GBXRemote 1" => 1,
            "GBXRemote 2" => 2,
            _ => throw new InvalidOperationException(
                $"Unsupported GbxRemote handshake: '{handshake}'")
        };

        Console.WriteLine($"Handshake: {handshake}");
    }

    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        var response = await CallAsync(
            "Authenticate",
            XmlRpcString(username),
            XmlRpcString(password));

        return ReadBooleanResult(response);
    }

    public async Task<bool> EnableCallbacksAsync(bool enabled)
    {
        var response = await CallAsync(
            "EnableCallbacks",
            XmlRpcBoolean(enabled));

        return ReadBooleanResult(response);
    }

    public async Task<bool> SetChatTimeAsync(int milliseconds)
    {
        var response = await CallAsync(
            "SetChatTime",
            XmlRpcInt(milliseconds));

        return ReadBooleanResult(response);
    }

    public async Task<bool> ManualFlowControlEnableAsync(bool enabled)
    {
        var response = await CallAsync(
            "ManualFlowControlEnable",
            XmlRpcBoolean(enabled));

        return ReadBooleanResult(response);
    }

    public async Task<int> ManualFlowControlIsEnabledAsync()
    {
        var response = await CallAsync("ManualFlowControlIsEnabled");

        var intValue =
            response.Descendants("int").FirstOrDefault()?.Value ??
            response.Descendants("i4").FirstOrDefault()?.Value;

        return int.TryParse(intValue, out var parsed)
            ? parsed
            : -1;
    }

    public async Task<string> ManualFlowControlGetCurTransitionAsync()
    {
        var response = await CallAsync("ManualFlowControlGetCurTransition");

        return response.Descendants("string").FirstOrDefault()?.Value ?? "";
    }

    public async Task<bool> ManualFlowControlProceedAsync()
    {
        var response = await CallAsync("ManualFlowControlProceed");
        return ReadBooleanResult(response);
    }

    public async Task<XDocument> CallAsync(
        string methodName,
        params XElement[] parameters)
    {
        EnsureConnected();

        var xml = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "methodCall",
                new XElement("methodName", methodName),
                new XElement(
                    "params",
                    parameters.Select(
                        parameter =>
                            new XElement(
                                "param",
                                new XElement("value", parameter))))));

        var xmlText =
            xml.ToString(SaveOptions.DisableFormatting);

        var xmlBytes =
            Encoding.UTF8.GetBytes(xmlText);

        _requestHandle++;

        if (_protocolVersion == 1)
        {
            var packet = new byte[4 + xmlBytes.Length];

            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(0, 4),
                (uint)xmlBytes.Length);

            xmlBytes.CopyTo(packet.AsSpan(4));

            await _stream!.WriteAsync(packet);
        }
        else
        {
            var packet = new byte[8 + xmlBytes.Length];

            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(0, 4),
                (uint)xmlBytes.Length);

            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(4, 4),
                _requestHandle);

            xmlBytes.CopyTo(packet.AsSpan(8));

            await _stream!.WriteAsync(packet);
        }

        await _stream.FlushAsync();

        while (true)
        {
            uint responseHandle;
            uint responseSize;

            if (_protocolVersion == 1)
            {
                var header =
                    await ReadExactAsync(4);

                responseSize =
                    BinaryPrimitives.ReadUInt32LittleEndian(header);

                responseHandle =
                    _requestHandle;
            }
            else
            {
                var header =
                    await ReadExactAsync(8);

                responseSize =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        header.AsSpan(0, 4));

                responseHandle =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        header.AsSpan(4, 4));
            }

            if (responseSize == 0)
                throw new IOException(
                    "Server returned an empty packet.");

            if (responseSize > 4 * 1024 * 1024)
                throw new IOException(
                    $"Server response too large: {responseSize} bytes.");

            var responseBytes =
                await ReadExactAsync((int)responseSize);

            var responseXml =
                Encoding.UTF8.GetString(responseBytes);

            // GbxRemote 2 callbacks have the high bit clear.
            // For this manual test we do not need to process them yet,
            // so simply consume and skip them while waiting for RPC replies.
            if (_protocolVersion == 2 &&
                (responseHandle & 0x80000000) == 0)
            {
                continue;
            }

            if (responseHandle != _requestHandle)
                continue;

            var document =
                XDocument.Parse(responseXml);

            var fault =
                document.Descendants("fault").FirstOrDefault();

            if (fault is not null)
                throw new InvalidOperationException(
                    $"XML-RPC fault: {responseXml}");

            return document;
        }
    }

    public static XElement XmlRpcInt(int value)
    {
        return new XElement("int", value);
    }

    public static XElement XmlRpcBoolean(bool value)
    {
        return new XElement(
            "boolean",
            value ? "1" : "0");
    }

    public static XElement XmlRpcString(string value)
    {
        return new XElement("string", value);
    }

    private static bool ReadBooleanResult(XDocument document)
    {
        var value =
            document.Descendants("boolean").FirstOrDefault()?.Value;

        return value == "1" ||
               value?.Equals(
                   "true",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task<byte[]> ReadExactAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var buffer =
            new byte[count];

        var offset = 0;

        while (offset < count)
        {
            var read =
                await _stream!.ReadAsync(
                    buffer.AsMemory(
                        offset,
                        count - offset),
                    cancellationToken);

            if (read == 0)
                throw new IOException(
                    "Connection closed by server.");

            offset += read;
        }

        return buffer;
    }

    private void EnsureConnected()
    {
        if (_stream is null)
            throw new InvalidOperationException(
                "Not connected to a GbxRemote server.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
            await _stream.DisposeAsync();

        _tcpClient?.Dispose();
    }
}
