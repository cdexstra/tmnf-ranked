namespace TMNFRanked.Controller;

/// <summary>
/// Converts raw GbxRemote callbacks into typed events that the Ranked
/// match controller can reason about without parsing XML-RPC values itself.
/// </summary>
public sealed class TmnfMatchEventTranslator : IDisposable
{
    private readonly TmnfGbxRemoteClient _client;
    private bool _disposed;

    public event EventHandler<TmnfMatchEvent>? MatchEventReceived;

    public TmnfMatchEventTranslator(TmnfGbxRemoteClient client)
    {
        _client = client;
        _client.CallbackReceived += OnCallbackReceived;
    }

    private void OnCallbackReceived(
        object? sender,
        TmnfGbxCallback callback)
    {
        var matchEvent =
            Translate(
                callback,
                DateTimeOffset.UtcNow);

        if (matchEvent is null)
            return;

        MatchEventReceived?.Invoke(
            this,
            matchEvent);
    }

    private static TmnfMatchEvent? Translate(
        TmnfGbxCallback callback,
        DateTimeOffset receivedAt)
    {
        var p = callback.Parameters;

        switch (callback.MethodName)
        {
            case "TrackMania.PlayerConnect":
                if (p.Count >= 2 &&
                    TryString(p[0], out var connectLogin) &&
                    TryBool(p[1], out var isSpectator))
                {
                    return new PlayerConnectedEvent(
                        receivedAt,
                        connectLogin,
                        isSpectator);
                }

                break;

            case "TrackMania.PlayerDisconnect":
                if (p.Count >= 1 &&
                    TryString(p[0], out var disconnectLogin))
                {
                    return new PlayerDisconnectedEvent(
                        receivedAt,
                        disconnectLogin);
                }

                break;

            case "TrackMania.PlayerCheckpoint":
                if (p.Count >= 5 &&
                    TryInt(p[0], out var checkpointPlayerUid) &&
                    TryString(p[1], out var checkpointLogin) &&
                    TryInt(p[2], out var timeOrScore) &&
                    TryInt(p[3], out var curLap) &&
                    TryInt(p[4], out var checkpointIndex))
                {
                    // TMNF sometimes emits internal UID=0 callbacks during
                    // transitions. Those are not real Ranked player events.
                    if (checkpointPlayerUid <= 0 ||
                        IsInternalLogin(checkpointLogin))
                    {
                        return null;
                    }

                    return new PlayerCheckpointEvent(
                        receivedAt,
                        checkpointPlayerUid,
                        checkpointLogin,
                        timeOrScore,
                        curLap,
                        checkpointIndex);
                }

                break;

            case "TrackMania.PlayerFinish":
                if (p.Count >= 3 &&
                    TryInt(p[0], out var finishPlayerUid) &&
                    TryString(p[1], out var finishLogin) &&
                    TryInt(p[2], out var finishTimeOrScore))
                {
                    // Known TMNF noise around round transitions:
                    // UID 0 + unnamed_<ip>_<port> + time 0.
                    if (finishPlayerUid <= 0 ||
                        IsInternalLogin(finishLogin))
                    {
                        return null;
                    }

                    return new PlayerFinishedEvent(
                        receivedAt,
                        finishPlayerUid,
                        finishLogin,
                        finishTimeOrScore);
                }

                break;

            case "TrackMania.BeginRound":
                return new RoundBeganEvent(receivedAt);

            case "TrackMania.EndRound":
                return new RoundEndedEvent(receivedAt);

            case "TrackMania.BeginChallenge":
            case "TrackMania.BeginRace":
                return new ChallengeBeganEvent(receivedAt);

            case "TrackMania.EndChallenge":
            case "TrackMania.EndRace":
                return new ChallengeEndedEvent(receivedAt);

            case "TrackMania.StatusChanged":
                if (p.Count >= 2 &&
                    TryInt(p[0], out var statusCode) &&
                    TryString(p[1], out var statusText))
                {
                    return new ServerStatusChangedEvent(
                        receivedAt,
                        statusCode,
                        statusText);
                }

                break;

            case "TrackMania.ManualFlowControlTransition":
                if (p.Count >= 1 &&
                    TryString(p[0], out var transition))
                {
                    return new ManualFlowTransitionEvent(
                        receivedAt,
                        transition);
                }

                break;
        }

        return new UnknownTmnfCallbackEvent(
            receivedAt,
            callback.MethodName,
            callback.Parameters);
    }

    private static bool IsInternalLogin(string login)
    {
        return login.StartsWith(
            "unnamed_",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryString(
        object? value,
        out string result)
    {
        if (value is string text)
        {
            result = text;
            return true;
        }

        result = "";
        return false;
    }

    private static bool TryBool(
        object? value,
        out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }

        if (value is int integer)
        {
            result = integer != 0;
            return true;
        }

        if (bool.TryParse(value?.ToString(), out var parsed))
        {
            result = parsed;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryInt(
        object? value,
        out int result)
    {
        if (value is int integer)
        {
            result = integer;
            return true;
        }

        return int.TryParse(
            value?.ToString(),
            out result);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.CallbackReceived -= OnCallbackReceived;
    }
}
