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

    public async Task<TmnfChallengeInfo> GetCurrentChallengeInfoAsync()
    {
        var response = await CallAsync("GetCurrentChallengeInfo");

        var structElement = response.Descendants("struct").FirstOrDefault()
            ?? throw new InvalidOperationException(
                "GetCurrentChallengeInfo returned no struct.");

        return ParseChallengeInfo(structElement);
    }

    public async Task<List<TmnfChallengeInfo>> GetChallengeListAsync()
    {
        var response = await CallAsync(
            "GetChallengeList",
            XmlRpcInt(1000),
            XmlRpcInt(0));

        var result = new List<TmnfChallengeInfo>();

        foreach (var structElement in response.Descendants("struct"))
        {
            result.Add(ParseChallengeInfo(structElement));
        }

        return result;
    }

    public async Task<bool> AddChallengeAsync(string fileName)
    {
        var response = await CallAsync(
            "AddChallenge",
            XmlRpcString(fileName));

        return ReadBooleanResult(response);
    }

    public async Task<bool> ChooseNextChallengeAsync(string fileName)
    {
        var response = await CallAsync(
            "ChooseNextChallenge",
            XmlRpcString(fileName));

        return ReadBooleanResult(response);
    }

    public async Task<bool> NextChallengeAsync()
    {
        var response = await CallAsync("NextChallenge");
        return ReadBooleanResult(response);
    }

    public async Task<bool> RestartChallengeAsync()
    {
        var response = await CallAsync("RestartChallenge");
        return ReadBooleanResult(response);
    }

    public async Task<List<TmnfPlayerInfo>> GetPlayerListAsync()
    {
        var response = await CallAsync(
            "GetPlayerList",
            XmlRpcInt(100),
            XmlRpcInt(0));

        var players = new List<TmnfPlayerInfo>();

        foreach (var structElement in response.Descendants("struct"))
        {
            var values = ReadStruct(structElement);

            var login = ReadString(values, "Login");
            if (string.IsNullOrWhiteSpace(login))
                continue;

            players.Add(
                new TmnfPlayerInfo(
                    Login: login,
                    NickName: ReadString(values, "NickName") ?? "",
                    PlayerId: ReadInt(values, "PlayerId")));
        }

        return players;
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

        var xmlText = xml.ToString(SaveOptions.DisableFormatting);
        var xmlBytes = Encoding.UTF8.GetBytes(xmlText);

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
                var header = await ReadExactAsync(4);

                responseSize =
                    BinaryPrimitives.ReadUInt32LittleEndian(header);

                responseHandle = _requestHandle;
            }
            else
            {
                var header = await ReadExactAsync(8);

                responseSize =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        header.AsSpan(0, 4));

                responseHandle =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        header.AsSpan(4, 4));
            }

            if (responseSize == 0)
                throw new IOException("Server returned an empty packet.");

            if (responseSize > 4 * 1024 * 1024)
                throw new IOException(
                    $"Server response too large: {responseSize} bytes.");

            var responseBytes =
                await ReadExactAsync((int)responseSize);

            var responseXml =
                Encoding.UTF8.GetString(responseBytes);

            // Ignore callbacks that arrive while waiting for this RPC response.
            if (_protocolVersion == 2 &&
                (responseHandle & 0x80000000) == 0)
            {
                continue;
            }

            if (responseHandle != _requestHandle)
                continue;

            var document = XDocument.Parse(responseXml);

            var fault = document.Descendants("fault").FirstOrDefault();

            if (fault is not null)
                throw new InvalidOperationException(
                    $"XML-RPC fault: {responseXml}");

            return document;
        }
    }

    public static XElement XmlRpcString(string value)
    {
        return new XElement("string", value);
    }

    public static XElement XmlRpcInt(int value)
    {
        return new XElement("int", value);
    }

    private static bool ReadBooleanResult(XDocument document)
    {
        var value =
            document.Descendants("boolean").FirstOrDefault()?.Value;

        return value == "1" ||
               value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static TmnfChallengeInfo ParseChallengeInfo(XElement structElement)
    {
        var values = ReadStruct(structElement);

        return new TmnfChallengeInfo(
            Name: ReadString(values, "Name") ?? "",
            UId: ReadString(values, "UId") ?? "",
            FileName: ReadString(values, "FileName") ?? "",
            Author: ReadString(values, "Author") ?? "",
            Environnement: ReadString(values, "Environnement") ?? "",
            GoldTime: ReadInt(values, "GoldTime"),
            SilverTime: ReadInt(values, "SilverTime"),
            BronzeTime: ReadInt(values, "BronzeTime"),
            AuthorTime: ReadInt(values, "AuthorTime"));
    }

    private static Dictionary<string, object?> ReadStruct(XElement structElement)
    {
        var result =
            new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var member in structElement.Elements("member"))
        {
            var name = member.Element("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result[name] =
                ReadXmlRpcValue(member.Element("value"));
        }

        return result;
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        return values.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }

    private static int? ReadInt(
        IReadOnlyDictionary<string, object?> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is int number)
            return number;

        return int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static object? ReadXmlRpcValue(XElement? value)
    {
        if (value is null)
            return null;

        if (!value.HasElements)
            return value.Value;

        var child = value.Elements().FirstOrDefault();

        if (child is null)
            return value.Value;

        return child.Name.LocalName switch
        {
            "string" => child.Value,

            "int" or "i4" =>
                int.TryParse(child.Value, out var intValue)
                    ? intValue
                    : child.Value,

            "boolean" =>
                child.Value == "1" ||
                child.Value.Equals(
                    "true",
                    StringComparison.OrdinalIgnoreCase),

            "double" =>
                double.TryParse(
                    child.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var doubleValue)
                    ? doubleValue
                    : child.Value,

            "struct" => ReadStruct(child),

            "array" =>
                child.Element("data")?
                    .Elements("value")
                    .Select(ReadXmlRpcValue)
                    .ToList()
                ?? new List<object?>(),

            _ => child.Value
        };
    }

    private async Task<byte[]> ReadExactAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read =
                await _stream!.ReadAsync(
                    buffer.AsMemory(offset, count - offset),
                    cancellationToken);

            if (read == 0)
                throw new IOException("Connection closed by server.");

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

public sealed record TmnfPlayerInfo(
    string Login,
    string NickName,
    int? PlayerId);

public sealed record TmnfChallengeInfo(
    string Name,
    string UId,
    string FileName,
    string Author,
    string Environnement,
    int? GoldTime,
    int? SilverTime,
    int? BronzeTime,
    int? AuthorTime);
