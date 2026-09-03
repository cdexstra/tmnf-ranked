using TMNFRanked.Controller;

Console.WriteLine("TMNF Ranked Controller");
Console.WriteLine("Manual Flow Control test v2");
Console.WriteLine();

try
{
    await using var client =
        new TmnfGbxRemoteClient();

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

    Console.WriteLine();
    Console.WriteLine(
        "Enabling callbacks...");

    var callbacksEnabled =
        await client.EnableCallbacksAsync(true);

    Console.WriteLine(
        callbacksEnabled
            ? "Callbacks enabled."
            : "EnableCallbacks(true) returned false.");

    if (!callbacksEnabled)
        return;

    Console.WriteLine();
    Console.WriteLine(
        "Setting ChatTime to 0...");

    var chatTimeChanged =
        await client.SetChatTimeAsync(0);

    Console.WriteLine(
        chatTimeChanged
            ? "SetChatTime(0) accepted."
            : "SetChatTime(0) returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "Enabling Manual Flow Control...");

    var enabled =
        await client.ManualFlowControlEnableAsync(true);

    Console.WriteLine(
        enabled
            ? "Manual Flow Control enabled."
            : "ManualFlowControlEnable(true) returned false.");

    if (!enabled)
        return;

    var state =
        await client.ManualFlowControlIsEnabledAsync();

    Console.WriteLine(
        $"ManualFlowControlIsEnabled = {state}");

    Console.WriteLine();
    Console.WriteLine(
        "Now go into TMNF and finish the round.");

    Console.WriteLine(
        "The game should stop at a transition instead of immediately continuing.");

    Console.WriteLine();
    Console.WriteLine(
        "When it is visibly waiting, come back here and press Enter.");

    Console.ReadLine();

    var transition =
        await client.ManualFlowControlGetCurTransitionAsync();

    Console.WriteLine();
    Console.WriteLine(
        $"Blocked transition: '{transition}'");

    if (string.IsNullOrWhiteSpace(transition))
    {
        Console.WriteLine();
        Console.WriteLine(
            "No blocked transition is currently reported.");

        Console.WriteLine(
            "Press Enter to disable manual flow control and quit.");

        Console.ReadLine();

        await client.ManualFlowControlEnableAsync(false);
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to call ManualFlowControlProceed().");

    Console.ReadLine();

    var proceeded =
        await client.ManualFlowControlProceedAsync();

    Console.WriteLine(
        proceeded
            ? "Proceed accepted. Watch TMNF continue."
            : "ManualFlowControlProceed returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter when you're done observing.");

    Console.ReadLine();

    Console.WriteLine();
    Console.WriteLine(
        "Disabling Manual Flow Control before exit...");

    var disabled =
        await client.ManualFlowControlEnableAsync(false);

    Console.WriteLine(
        disabled
            ? "Manual Flow Control disabled."
            : "ManualFlowControlEnable(false) returned false.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("Controller stopped.");
