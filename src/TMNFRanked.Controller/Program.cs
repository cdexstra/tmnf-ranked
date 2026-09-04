using TMNFRanked.Controller;

Console.WriteLine("TMNF Ranked Controller");
Console.WriteLine("ManiaLink HUD test");
Console.WriteLine();

const string hudV1 = """
<manialink id="49001">
  <frame posn="0 43 1">
    <quad posn="-35 0 0" sizen="70 8" bgcolor="000C"/>
    <label posn="-31 -1.5 1" textsize="2" text="$fffheplest"/>
    <label posn="-7 -1.5 1" textsize="2" text="$aaa● ○"/>
    <label posn="-1.5 -1.5 1" textsize="2" text="$fff1 - 1"/>
    <label posn="7 -1.5 1" textsize="2" text="$aaa○ ●"/>
    <label posn="21 -1.5 1" textsize="2" text="$fffOPPONENT"/>
    <label posn="-7 -5 1" textsize="1" text="$aaaMAP 1/3  •  A07-Race"/>
  </frame>
</manialink>
""";

const string hudV2 = """
<manialink id="49001">
  <frame posn="0 43 1">
    <quad posn="-35 0 0" sizen="70 8" bgcolor="000C"/>
    <label posn="-31 -1.5 1" textsize="2" text="$fffheplest"/>
    <label posn="-7 -1.5 1" textsize="2" text="$aaa● ○"/>
    <label posn="-1.5 -1.5 1" textsize="2" text="$fff2 - 1"/>
    <label posn="7 -1.5 1" textsize="2" text="$aaa○ ●"/>
    <label posn="21 -1.5 1" textsize="2" text="$fffOPPONENT"/>
    <label posn="-10 -5 1" textsize="1" text="$0f0ROUND WON  +  MAP POINT"/>
  </frame>
</manialink>
""";

try
{
    await using var client =
        new TmnfGbxRemoteClient();

    client.ConnectionFaulted += (_, exception) =>
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[CONNECTION FAULT] {exception.Message}");
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

    Console.WriteLine();
    Console.WriteLine(
        "Sending Ranked HUD v1...");

    var shown =
        await client.SendDisplayManialinkPageAsync(
            hudV1,
            timeoutMilliseconds: 0,
            hideOnClick: false);

    Console.WriteLine(
        shown
            ? "HUD v1 accepted by server."
            : "HUD v1 returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "Look at TMNF now.");

    Console.WriteLine(
        "You should see a small Ranked bar near the top of the screen.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to UPDATE the same HUD.");

    Console.ReadLine();

    var updated =
        await client.SendDisplayManialinkPageAsync(
            hudV2,
            timeoutMilliseconds: 0,
            hideOnClick: false);

    Console.WriteLine(
        updated
            ? "HUD v2 accepted by server."
            : "HUD v2 returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "The score should now read 2 - 1 and show ROUND WON.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to HIDE the HUD.");

    Console.ReadLine();

    var hidden =
        await client.SendHideManialinkPageAsync();

    Console.WriteLine(
        hidden
            ? "HUD hidden."
            : "SendHideManialinkPage returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to send it again once more.");

    Console.ReadLine();

    var reshown =
        await client.SendDisplayManialinkPageAsync(
            hudV1,
            timeoutMilliseconds: 0,
            hideOnClick: false);

    Console.WriteLine(
        reshown
            ? "HUD re-shown successfully."
            : "HUD re-show returned false.");

    Console.WriteLine();
    Console.WriteLine(
        "Press Enter to clean up and exit.");

    Console.ReadLine();

    await client.SendHideManialinkPageAsync();
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("ERROR:");
    Console.WriteLine(ex);
}

Console.WriteLine();
Console.WriteLine("Controller stopped.");
