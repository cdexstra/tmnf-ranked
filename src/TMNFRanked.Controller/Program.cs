using TMNFRanked.Controller;

Console.WriteLine("TMNF Ranked Controller");
Console.WriteLine("Typed match-event foundation test");
Console.WriteLine();

try
{
    await using var client =
        new TmnfGbxRemoteClient();

    using var translator =
        new TmnfMatchEventTranslator(client);

    client.ConnectionFaulted += (_, exception) =>
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[CONNECTION FAULT] {exception.Message}");
    };

    translator.MatchEventReceived += (_, matchEvent) =>
    {
        PrintMatchEvent(matchEvent);
    };

    await client.ConnectAsync(
        "127.0.0.1",
        5000);

    var authenticated =
        await client.AuthenticateAsync(
            "SuperAdmin",
            "SuperAdmin");

    if (!authenticated)
    {
        Console.WriteLine(
            "Authentication failed.");

        return;
    }

    Console.WriteLine(
        "Authentication succeeded.");

    var callbacksEnabled =
        await client.EnableCallbacksAsync(true);

    if (!callbacksEnabled)
    {
        Console.WriteLine(
            "EnableCallbacks(true) returned false.");

        return;
    }

    Console.WriteLine(
        "Callbacks enabled.");

    var players =
        await client.GetPlayerListAsync();

    Console.WriteLine();
    Console.WriteLine(
        $"Known real players: {players.Count}");

    foreach (var player in players)
    {
        Console.WriteLine(
            $"- {player.Login} | ID {player.PlayerId}");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Drive a run and finish it.");

    Console.WriteLine(
        "The console should now show CLEAN typed match events.");

    Console.WriteLine();
    Console.WriteLine(
        "Important: the old fake UID=0 / unnamed_* finish callbacks");
    Console.WriteLine(
        "should NOT appear anymore.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter at any time to make a normal RPC call.");
    Console.WriteLine(
        "Press Q to quit.");

    while (true)
    {
        var key =
            Console.ReadKey(
                intercept: true);

        if (key.Key == ConsoleKey.Q)
            break;

        if (key.Key == ConsoleKey.Enter)
        {
            var map =
                await client.GetCurrentChallengeInfoAsync();

            Console.WriteLine();
            Console.WriteLine(
                $"[RPC] Current map = {map.Name}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        "Disabling callbacks...");

    await client.EnableCallbacksAsync(false);
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("Controller stopped.");

static void PrintMatchEvent(
    TmnfMatchEvent matchEvent)
{
    switch (matchEvent)
    {
        case PlayerConnectedEvent e:
            Console.WriteLine(
                $"[MATCH] PLAYER CONNECTED | {e.Login} | spectator={e.IsSpectator}");
            break;

        case PlayerDisconnectedEvent e:
            Console.WriteLine(
                $"[MATCH] PLAYER DISCONNECTED | {e.Login}");
            break;

        case PlayerCheckpointEvent e:
            Console.WriteLine(
                $"[MATCH] CHECKPOINT | {e.Login} | {e.TimeOrScore} ms | CP={e.CheckpointIndex}");
            break;

        case PlayerFinishedEvent e:
            Console.WriteLine(
                $"[MATCH] FINISH | {e.Login} | {e.TimeOrScore} ms");
            break;

        case RoundBeganEvent:
            Console.WriteLine(
                "[MATCH] ROUND BEGIN");
            break;

        case RoundEndedEvent:
            Console.WriteLine(
                "[MATCH] ROUND END");
            break;

        case ChallengeBeganEvent:
            Console.WriteLine(
                "[MATCH] CHALLENGE BEGIN");
            break;

        case ChallengeEndedEvent:
            Console.WriteLine(
                "[MATCH] CHALLENGE END");
            break;

        case ServerStatusChangedEvent e:
            Console.WriteLine(
                $"[MATCH] SERVER STATUS | {e.StatusCode} | {e.StatusText}");
            break;

        case ManualFlowTransitionEvent e:
            Console.WriteLine(
                $"[MATCH] FLOW BLOCKED | {e.Transition}");
            break;

        case UnknownTmnfCallbackEvent e:
            Console.WriteLine(
                $"[RAW] {e.MethodName}");
            break;

        default:
            Console.WriteLine(
                $"[MATCH] {matchEvent.GetType().Name}");
            break;
    }
}
