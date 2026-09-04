using TMNFRanked.Controller;

Console.WriteLine("TMNF Ranked Controller");
Console.WriteLine("GbxRemote read-loop foundation test");
Console.WriteLine();

try
{
    await using var client = new TmnfGbxRemoteClient();

    client.CallbackReceived += (_, callback) =>
    {
        Console.WriteLine(
            $"[CALLBACK] {callback.MethodName}" +
            FormatParameters(callback.Parameters));
    };

    client.ConnectionFaulted += (_, exception) =>
    {
        Console.WriteLine();
        Console.WriteLine($"[CONNECTION FAULT] {exception.Message}");
    };

    await client.ConnectAsync("127.0.0.1", 5000);

    var authenticated =
        await client.AuthenticateAsync("SuperAdmin", "SuperAdmin");

    if (!authenticated)
    {
        Console.WriteLine("Authentication failed.");
        return;
    }

    Console.WriteLine("Authentication succeeded.");

    var callbacksEnabled =
        await client.EnableCallbacksAsync(true);

    if (!callbacksEnabled)
    {
        Console.WriteLine("EnableCallbacks(true) returned false.");
        return;
    }

    Console.WriteLine("Callbacks enabled.");

    var map =
        await client.GetCurrentChallengeInfoAsync();

    Console.WriteLine();
    Console.WriteLine($"Current map: {map.Name}");
    Console.WriteLine($"UID:         {map.UId}");
    Console.WriteLine($"File:        {map.FileName}");

    var players =
        await client.GetPlayerListAsync();

    Console.WriteLine();
    Console.WriteLine($"Players currently on server: {players.Count}");

    foreach (var player in players)
    {
        Console.WriteLine(
            $"- {player.Login} | {player.NickName} | ID {player.PlayerId}");
    }

    Console.WriteLine();
    Console.WriteLine("Permanent read loop active.");
    Console.WriteLine("Drive / finish runs in TMNF and callbacks should print live.");
    Console.WriteLine();
    Console.WriteLine("Controls:");
    Console.WriteLine("  Enter = issue TWO simultaneous RPC calls");
    Console.WriteLine("  R     = restart current challenge");
    Console.WriteLine("  Q     = quit");

    while (true)
    {
        var key =
            Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Q)
            break;

        if (key.Key == ConsoleKey.R)
        {
            Console.WriteLine();
            Console.WriteLine("[RPC TEST] RestartChallenge...");

            var restarted =
                await client.RestartChallengeAsync();

            Console.WriteLine(
                $"[RPC TEST] RestartChallenge returned {restarted}.");

            continue;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            Console.WriteLine(
                "[RPC TEST] Sending map + player-list calls together while callbacks stay active...");

            var mapTask =
                client.GetCurrentChallengeInfoAsync();

            var playersTask =
                client.GetPlayerListAsync();

            await Task.WhenAll(
                mapTask,
                playersTask);

            var currentMap =
                await mapTask;

            var currentPlayers =
                await playersTask;

            Console.WriteLine(
                $"[RPC TEST] OK - {currentMap.Name}, {currentPlayers.Count} player(s).");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Disabling callbacks...");

    var callbacksDisabled =
        await client.EnableCallbacksAsync(false);

    Console.WriteLine(
        callbacksDisabled
            ? "Callbacks disabled."
            : "EnableCallbacks(false) returned false.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("Controller stopped.");

static string FormatParameters(IReadOnlyList<object?> parameters)
{
    if (parameters.Count == 0)
        return "";

    return " | " +
        string.Join(
            " | ",
            parameters.Select(FormatValue));
}

static string FormatValue(object? value)
{
    if (value is null)
        return "null";

    if (value is IReadOnlyDictionary<string, object?> dictionary)
    {
        return "{" +
            string.Join(
                ", ",
                dictionary.Select(
                    pair => $"{pair.Key}={FormatValue(pair.Value)}")) +
            "}";
    }

    if (value is IEnumerable<object?> list &&
        value is not string)
    {
        return "[" +
            string.Join(
                ", ",
                list.Select(FormatValue)) +
            "]";
    }

    return value.ToString() ?? "";
}
