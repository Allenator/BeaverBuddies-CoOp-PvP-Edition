using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using TimberNet;

// Joins a hosted BeaverBuddies game as an extra player, announces a name
// and color, and moves a cursor in circles so it's easy to spot.
// Usage: FakePlayer [seconds] [name] [hexColor] [host] [port]
//
// With BB_COMPAT (and BB_BUILD) set to what the host's build says of itself
// (the test harness's "compat" logs it), it says the same in a guest's
// presence, which a host on "same build only" lets in. With BB_INSTANCE set,
// it's a test harness's player instead (Tools/Tests): it takes commands from
// ~/Library/Caches/BeaverBuddies-test-<instance> as the harness does, one a
// line, and doesn't announce itself with Timber Empires' event. "press <id>"
// and "release <id>" press and let go of the button of the lever of that
// entity ID; "releaseas <instance> <id>" lets go of it as that test copy's
// player, as a modified game could, which the host should play as this
// player's; "quit" leaves.

int seconds = args.Length > 0 ? int.Parse(args[0]) : 3600;
string name = args.Length > 1 ? args[1] : "Rival";
string color = (args.Length > 2 ? args[2] : "FF3344").TrimStart('#').ToUpperInvariant();
string host = args.Length > 3 ? args[3] : "127.0.0.1";
int port = args.Length > 4 ? int.Parse(args[4]) : 25565;
string? compat = Environment.GetEnvironmentVariable("BB_COMPAT");
string? build = Environment.GetEnvironmentVariable("BB_BUILD");
string? instance = Environment.GetEnvironmentVariable("BB_INSTANCE");
string? commands = string.IsNullOrEmpty(instance) ? null
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "BeaverBuddies-test-" + instance);

// Same ID for the same name, so districts stay owned across runs
string playerID = new Guid(MD5.HashData(Encoding.UTF8.GetBytes("BeaverBuddies.FakePlayer." + name))).ToString();

var client = new TimberClient(new TCPClientWrapper(host, port));
client.OnLog += m => { if (!m.StartsWith("Received event")) Console.WriteLine(m); };
bool failed = false;
client.OnError += m => { Console.Error.WriteLine("Error: " + m); failed = true; };
client.OnMapReceived += bytes => Console.WriteLine($"Map received ({bytes.Length / 1024} KB)");
// Nothing to load: the host's reload save only starts the game again from tick 0
client.OnReload += bytes => { Console.WriteLine($"Reload save received ({bytes.Length / 1024} KB)"); client.EndReload(); };

// A lever's button, as a player's click on it sends it (BeaverBuddies.Events.LeverButtonEvent)
void LeverButton(string entityID, bool pressed, string? asPlayer = null) => client.DoUserInitiatedEvent(new JObject
{
    ["$type"] = "BeaverBuddies.Events.LeverButtonEvent, BeaverBuddies",
    ["entityID"] = entityID,
    ["pressed"] = pressed,
    ["ticksSinceLoad"] = client.TickCount,
    ["randomS0Before"] = null,
    ["playerID"] = asPlayer ?? playerID,
    ["type"] = "LeverButtonEvent",
});

// A test copy's player ID, as the harness gives it (BeaverBuddies.Players.PlayerIdentity)
string HarnessPlayerID(string testInstance) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(testInstance))).ToString();

Console.WriteLine($"Connecting to {host}:{port} as {name} ({playerID})...");
client.Start();

var started = DateTime.UtcNow;
var lastPresence = DateTime.MinValue;
bool announced = commands != null;
bool quit = false;
double angle = 0;
while (!failed && !quit && !client.IsStopped && (DateTime.UtcNow - started).TotalSeconds < seconds)
{
    client.Update();
    client.ReadEvents(client.TickCount);

    if (!announced && (DateTime.UtcNow - started).TotalSeconds > 3)
    {
        client.DoUserInitiatedEvent(new JObject
        {
            ["$type"] = "BeaverBuddies.Players.PlayerAnnouncedEvent, BeaverBuddies",
            ["name"] = name,
            ["color"] = color,
            ["ticksSinceLoad"] = client.TickCount,
            ["randomS0Before"] = null,
            ["playerID"] = playerID,
            ["type"] = "PlayerAnnouncedEvent",
        });
        announced = true;
        Console.WriteLine($"Announced {name} with color #{color}. Set a District Center's owner to them now.");
    }

    // Who this is and what it plays, every second as a guest's game says it (BeaverBuddies.Players.PlayerPresence)
    if (compat != null && (DateTime.UtcNow - lastPresence).TotalSeconds >= 1)
    {
        lastPresence = DateTime.UtcNow;
        client.SendTransientMessage(new JObject
        {
            [TimberNetBase.TYPE_KEY] = "PlayerPresence",
            ["playerID"] = playerID,
            ["name"] = name,
            ["color"] = 2,
            ["host"] = false,
            ["waiting"] = false,
            ["compat"] = compat,
            ["build"] = build,
        });
    }

    if (commands != null && File.Exists(commands))
    {
        string[] lines = File.ReadAllLines(commands);
        File.Delete(commands);
        foreach (string line in lines)
        {
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            Console.WriteLine($"[Test] {line}");
            switch (words[0])
            {
                case "press" when words.Length > 1: LeverButton(words[1], true); break;
                case "release" when words.Length > 1: LeverButton(words[1], false); break;
                case "releaseas" when words.Length > 2: LeverButton(words[2], false, HarnessPlayerID(words[1])); break;
                case "quit": quit = true; break;
                default: Console.WriteLine($"[Test] {line} failed: unknown command"); break;
            }
        }
    }

    angle += 0.05;
    client.SendTransientMessage(new JObject
    {
        [TimberNetBase.TYPE_KEY] = "PlayerCursor",
        ["playerID"] = playerID,
        ["color"] = color,
        ["visible"] = true,
        ["x"] = 64 + 10 * Math.Cos(angle),
        ["y"] = 20,
        ["z"] = 64 + 10 * Math.Sin(angle),
    });

    Thread.Sleep(100);
}

Console.WriteLine("Leaving.");
client.Close();
