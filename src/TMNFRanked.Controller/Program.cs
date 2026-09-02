using TMNFRanked.Controller;

const string TargetMap = @"Challenges\test2.Gbx";

Console.WriteLine("TMNF Ranked Controller");
Console.WriteLine("Connecting...");

try
{
    await using var client = new TmnfGbxRemoteClient();

    await client.ConnectAsync("127.0.0.1", 5000);

    var authenticated =
        await client.AuthenticateAsync(
            "SuperAdmin",
            "SuperAdmin");

    if (!authenticated)
    {
        Console.WriteLine("Authentication failed.");
        return;
    }

    Console.WriteLine("Authentication succeeded.");
    Console.WriteLine();

    var current = await client.GetCurrentChallengeInfoAsync();

    Console.WriteLine("CURRENT MAP");
    Console.WriteLine($"Name: {current.Name}");
    Console.WriteLine($"UID:  {current.UId}");
    Console.WriteLine($"File: {current.FileName}");
    Console.WriteLine();

    var challengeList =
        await client.GetChallengeListAsync();

    Console.WriteLine("SERVER SELECTION");

    foreach (var challenge in challengeList)
    {
        Console.WriteLine(
            $"- {challenge.Name} | {challenge.FileName} | {challenge.UId}");
    }

    Console.WriteLine();

    var targetAlreadySelected =
        challengeList.Any(
            challenge =>
                string.Equals(
                    challenge.FileName,
                    TargetMap,
                    StringComparison.OrdinalIgnoreCase));

    if (!targetAlreadySelected)
    {
        Console.WriteLine(
            $"{TargetMap} is not in the server selection.");

        Console.WriteLine("Adding it...");

        var added =
            await client.AddChallengeAsync(TargetMap);

        Console.WriteLine(
            added
                ? "Map added to server selection."
                : "AddChallenge returned false.");

        if (!added)
            return;
    }
    else
    {
        Console.WriteLine(
            $"{TargetMap} is already in the server selection.");
    }

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to switch the server to test2.Gbx.");

    Console.ReadLine();

    Console.WriteLine(
        $"Choosing {TargetMap} as the next map...");

    var chosen =
        await client.ChooseNextChallengeAsync(TargetMap);

    if (!chosen)
    {
        Console.WriteLine(
            "ChooseNextChallenge returned false.");

        return;
    }

    Console.WriteLine(
        "Map chosen successfully.");

    Console.WriteLine(
        "Sending NextChallenge...");

    var switched =
        await client.NextChallengeAsync();

    if (!switched)
    {
        Console.WriteLine(
            "NextChallenge returned false.");

        return;
    }

    Console.WriteLine(
        "NextChallenge accepted by server.");

    Console.WriteLine(
        "Waiting briefly for the server to finish loading...");

    await Task.Delay(2000);

    var afterSwitch =
        await client.GetCurrentChallengeInfoAsync();

    Console.WriteLine();
    Console.WriteLine("CURRENT MAP AFTER SWITCH");
    Console.WriteLine($"Name: {afterSwitch.Name}");
    Console.WriteLine($"UID:  {afterSwitch.UId}");
    Console.WriteLine($"File: {afterSwitch.FileName}");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("Controller stopped.");
