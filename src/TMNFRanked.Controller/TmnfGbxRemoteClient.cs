using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace TMNFRanked.Controller;

public sealed class TmnfGbxRemoteClient : IAsyncDisposable
{
    private const int MaxPacketSize = 4 * 1024 * 1024;

    private readonly ConcurrentDictionary<uint, TaskCompletionSource<XDocument>> _pendingCalls = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    private uint _nextRequestHandle = 0x80000000;
    private bool _disposed;

    public event EventHandler<TmnfGbxCallback>? CallbackReceived;
    public event EventHandler<Exception>? ConnectionFaulted;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_stream is not null)
            throw new InvalidOperationException("Client is already connected.");

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _stream = _tcpClient.GetStream();

        var handshakeLengthBytes = await ReadExactAsync(_stream, 4, cancellationToken);
        var handshakeLength = BinaryPrimitives.ReadUInt32LittleEndian(handshakeLengthBytes);

        if (handshakeLength == 0 || handshakeLength > 64)
            throw new InvalidOperationException($"Invalid GbxRemote handshake length: {handshakeLength}");

        var handshakeBytes = await ReadExactAsync(_stream, checked((int)handshakeLength), cancellationToken);
        var handshake = Encoding.ASCII.GetString(handshakeBytes);

        Console.WriteLine($"Handshake: {handshake}");

        if (handshake != "GBXRemote 2")
            throw new NotSupportedException(
                $"TMNF Ranked currently requires GBXRemote 2. Server returned '{handshake}'.");

        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
    }

    public async Task<bool> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            "Authenticate",
            cancellationToken,
            XmlRpcString(username),
            XmlRpcString(password));

        return ReadBooleanResult(response);
    }

    public async Task<bool> EnableCallbacksAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            "EnableCallbacks",
            cancellationToken,
            XmlRpcBoolean(enabled));

        return ReadBooleanResult(response);
    }

    public async Task<TmnfChallengeInfo> GetCurrentChallengeInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("GetCurrentChallengeInfo", cancellationToken);

        var structElement = response.Descendants("struct").FirstOrDefault()
            ?? throw new InvalidOperationException("GetCurrentChallengeInfo returned no struct.");

        var values = ReadStruct(structElement);

        return new TmnfChallengeInfo(
            Name: ReadString(values, "Name") ?? "",
            UId: ReadString(values, "UId") ?? "",
            FileName: ReadString(values, "FileName") ?? "",
            Author: ReadString(values, "Author") ?? "",
            Environnement: ReadString(values, "Environnement") ?? "",
            AuthorTime: ReadInt(values, "AuthorTime"));
    }

    public async Task<List<TmnfPlayerInfo>> GetPlayerListAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            "GetPlayerList",
            cancellationToken,
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

    public async Task<bool> RestartChallengeAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("RestartChallenge", cancellationToken);
        return ReadBooleanResult(response);
    }

    public async Task<bool> SetChatTimeAsync(
        int milliseconds,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            "SetChatTime",
            cancellationToken,
            XmlRpcInt(milliseconds));

        return ReadBooleanResult(response);
    }

    public async Task<bool> ManualFlowControlEnableAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            "ManualFlowControlEnable",
            cancellationToken,
            XmlRpcBoolean(enabled));

        return ReadBooleanResult(response);
    }

    public async Task<int> ManualFlowControlIsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("ManualFlowControlIsEnabled", cancellationToken);

        var value =
            response.Descendants("int").FirstOrDefault()?.Value ??
            response.Descendants("i4").FirstOrDefault()?.Value;

        return int.TryParse(value, out var parsed) ? parsed : -1;
    }

    public async Task<string> ManualFlowControlGetCurTransitionAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("ManualFlowControlGetCurTransition", cancellationToken);
        return response.Descendants("string").FirstOrDefault()?.Value ?? "";
    }

    public async Task<bool> ManualFlowControlProceedAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("ManualFlowControlProceed", cancellationToken);
        return ReadBooleanResult(response);
    }

    public async Task<XDocument> CallAsync(
        string methodName,
        CancellationToken cancellationToken = default,
        params XElement[] parameters)
    {
        ThrowIfDisposed();
        EnsureConnected();

        var requestHandle = GetNextRequestHandle();

        var completion = new TaskCompletionSource<XDocument>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingCalls.TryAdd(requestHandle, completion))
            throw new InvalidOperationException($"Duplicate request handle 0x{requestHandle:X8}.");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pendingCalls.TryRemove(requestHandle, out var pending))
                pending.TrySetCanceled(cancellationToken);
        });

        try
        {
            var xml = BuildMethodCall(methodName, parameters);
            var xmlBytes = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));

            if (xmlBytes.Length > MaxPacketSize)
                throw new InvalidOperationException($"RPC request too large: {xmlBytes.Length} bytes.");

            var packet = new byte[8 + xmlBytes.Length];

            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(0, 4),
                checked((uint)xmlBytes.Length));

            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(4, 4),
                requestHandle);

            xmlBytes.CopyTo(packet.AsSpan(8));

            await _writeLock.WaitAsync(cancellationToken);

            try
            {
                EnsureConnected();
                await _stream!.WriteAsync(packet, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            return await completion.Task;
        }
        catch
        {
            _pendingCalls.TryRemove(requestHandle, out _);
            throw;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var header = await ReadExactAsync(_stream!, 8, cancellationToken);

                var packetSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                var handle = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));

                if (packetSize == 0)
                    throw new IOException("Server returned an empty GbxRemote packet.");

                if (packetSize > MaxPacketSize)
                    throw new IOException($"Server packet too large: {packetSize} bytes.");

                var payload = await ReadExactAsync(
                    _stream!,
                    checked((int)packetSize),
                    cancellationToken);

                var rawXml = Encoding.UTF8.GetString(payload);
                var document = XDocument.Parse(rawXml);

                if ((handle & 0x80000000) != 0)
                    RouteRpcResponse(handle, document, rawXml);
                else
                    DispatchCallback(handle, document);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            FailAllPendingCalls(ex);

            try
            {
                ConnectionFaulted?.Invoke(this, ex);
            }
            catch
            {
                // Consumer event handlers cannot be allowed to break cleanup.
            }
        }
    }

    private void RouteRpcResponse(uint handle, XDocument document, string rawXml)
    {
        if (!_pendingCalls.TryRemove(handle, out var completion))
            return;

        if (document.Descendants("fault").Any())
        {
            completion.TrySetException(
                new InvalidOperationException($"XML-RPC fault: {rawXml}"));
            return;
        }

        completion.TrySetResult(document);
    }

    private void DispatchCallback(uint handle, XDocument document)
    {
        var methodName =
            document.Root?.Element("methodName")?.Value
            ?? "(unknown callback)";

        var parameters =
            document.Root?
                .Element("params")?
                .Elements("param")
                .Select(param => ReadXmlRpcValue(param.Element("value")))
                .ToArray()
            ?? Array.Empty<object?>();

        var callback = new TmnfGbxCallback(
            Handle: handle,
            MethodName: methodName,
            Parameters: parameters,
            RawXml: document);

        try
        {
            CallbackReceived?.Invoke(this, callback);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Callback handler error for {methodName}: {ex.Message}");
        }
    }

    private uint GetNextRequestHandle()
    {
        lock (_pendingCalls)
        {
            _nextRequestHandle++;

            if ((_nextRequestHandle & 0x80000000) == 0)
                _nextRequestHandle = 0x80000001;

            return _nextRequestHandle;
        }
    }

    private static XDocument BuildMethodCall(
        string methodName,
        IReadOnlyCollection<XElement> parameters)
    {
        return new XDocument(
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
                                new XElement(
                                    "value",
                                    new XElement(parameter)))))));
    }

    private static bool ReadBooleanResult(XDocument document)
    {
        var value = document.Descendants("boolean").FirstOrDefault()?.Value;

        return value == "1" ||
               value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static XElement XmlRpcString(string value) =>
        new("string", value);

    public static XElement XmlRpcInt(int value) =>
        new("int", value);

    public static XElement XmlRpcBoolean(bool value) =>
        new("boolean", value ? "1" : "0");

    private static Dictionary<string, object?> ReadStruct(XElement structElement)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var member in structElement.Elements("member"))
        {
            var name = member.Element("name")?.Value;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            result[name] = ReadXmlRpcValue(member.Element("value"));
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

    private static object? ReadXmlRpcValue(XElement? valueElement)
    {
        if (valueElement is null)
            return null;

        if (!valueElement.HasElements)
            return valueElement.Value;

        var child = valueElement.Elements().FirstOrDefault();

        if (child is null)
            return valueElement.Value;

        return child.Name.LocalName switch
        {
            "string" => child.Value,

            "int" or "i4" =>
                int.TryParse(child.Value, out var integer)
                    ? integer
                    : child.Value,

            "boolean" =>
                child.Value == "1" ||
                child.Value.Equals("true", StringComparison.OrdinalIgnoreCase),

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

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, count - offset),
                cancellationToken);

            if (read == 0)
                throw new IOException("Connection closed by GbxRemote server.");

            offset += read;
        }

        return buffer;
    }

    private void FailAllPendingCalls(Exception exception)
    {
        foreach (var pair in _pendingCalls.ToArray())
        {
            if (_pendingCalls.TryRemove(pair.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private void EnsureConnected()
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected to a GbxRemote server.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_readLoopCts is not null)
            await _readLoopCts.CancelAsync();

        if (_readLoopTask is not null)
        {
            try
            {
                await _readLoopTask;
            }
            catch
            {
                // Any real read-loop failure was already surfaced.
            }
        }

        FailAllPendingCalls(
            new ObjectDisposedException(nameof(TmnfGbxRemoteClient)));

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        _readLoopCts?.Dispose();
        _writeLock.Dispose();
    }
}

public sealed record TmnfGbxCallback(
    uint Handle,
    string MethodName,
    IReadOnlyList<object?> Parameters,
    XDocument RawXml);

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
    int? AuthorTime);
