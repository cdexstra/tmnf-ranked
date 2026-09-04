namespace TMNFRanked.Controller;

public abstract record TmnfMatchEvent(
    DateTimeOffset ReceivedAt);

public sealed record PlayerConnectedEvent(
    DateTimeOffset ReceivedAt,
    string Login,
    bool IsSpectator)
    : TmnfMatchEvent(ReceivedAt);

public sealed record PlayerDisconnectedEvent(
    DateTimeOffset ReceivedAt,
    string Login)
    : TmnfMatchEvent(ReceivedAt);

public sealed record PlayerCheckpointEvent(
    DateTimeOffset ReceivedAt,
    int PlayerUid,
    string Login,
    int TimeOrScore,
    int CurLap,
    int CheckpointIndex)
    : TmnfMatchEvent(ReceivedAt);

public sealed record PlayerFinishedEvent(
    DateTimeOffset ReceivedAt,
    int PlayerUid,
    string Login,
    int TimeOrScore)
    : TmnfMatchEvent(ReceivedAt);

public sealed record RoundBeganEvent(
    DateTimeOffset ReceivedAt)
    : TmnfMatchEvent(ReceivedAt);

public sealed record RoundEndedEvent(
    DateTimeOffset ReceivedAt)
    : TmnfMatchEvent(ReceivedAt);

public sealed record ChallengeBeganEvent(
    DateTimeOffset ReceivedAt)
    : TmnfMatchEvent(ReceivedAt);

public sealed record ChallengeEndedEvent(
    DateTimeOffset ReceivedAt)
    : TmnfMatchEvent(ReceivedAt);

public sealed record ServerStatusChangedEvent(
    DateTimeOffset ReceivedAt,
    int StatusCode,
    string StatusText)
    : TmnfMatchEvent(ReceivedAt);

public sealed record ManualFlowTransitionEvent(
    DateTimeOffset ReceivedAt,
    string Transition)
    : TmnfMatchEvent(ReceivedAt);

public sealed record UnknownTmnfCallbackEvent(
    DateTimeOffset ReceivedAt,
    string MethodName,
    IReadOnlyList<object?> Parameters)
    : TmnfMatchEvent(ReceivedAt);
