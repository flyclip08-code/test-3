using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

class ChestReader : FortniteReplayReader.ReplayReader
{
    public double Radius = 800;
    public int Mine, Far, SearchedEvents, NoLocation, NoOwner, ChestActors, ZeroLocChests;
    public readonly Dictionary<uint, (string Path, double X, double Y, double Z)> Actors = new();
    public readonly HashSet<uint> Seen = new();
    public readonly List<double> Distances = new();
    public readonly Dictionary<string, int> Archetypes = new();
    static readonly Regex Interesting = new("chest|container|treasure|loot|cache|crate|barrel|vault|relic", RegexOptions.IgnoreCase);

    public ChestReader() : base(null, new FortniteReplaySettings { IgnoreContainers = false }) { }

    protected override void OnChannelActorRead(uint channel, Actor actor)
    {
        base.OnChannelActorRead(channel, actor);
        string path = null;
        if (actor.Archetype != null) GuidCache.NetGuidToPathName.TryGetValue(actor.Archetype.Value, out path);
        if (path == null) return;
        var v = actor.Location;
        double x = v != null ? v.X : 0, y = v != null ? v.Y : 0, z = v != null ? v.Z : 0;
        Actors[channel] = (path, x, y, z);
        Seen.Remove(channel);
        if (Interesting.IsMatch(path))
        {
            Archetypes[path] = Archetypes.TryGetValue(path, out var n) ? n + 1 : 1;
            if (path.Contains("chest", StringComparison.OrdinalIgnoreCase))
            {
                ChestActors++;
                if (x == 0 && y == 0 && z == 0) ZeroLocChests++;
            }
        }
    }

    protected override void OnChannelClosed(uint channel)
    {
        Actors.Remove(channel);
        Seen.Remove(channel);
        base.OnChannelClosed(channel);
    }

    protected override void OnExportRead(uint channel, INetFieldExportGroup exportGroup)
    {
        base.OnExportRead(channel, exportGroup);
        if (exportGroup is BaseContainer bc && bc.bAlreadySearched == true)
        {
            SearchedEvents++;
            if (!Seen.Add(channel)) return;
            if (!Actors.TryGetValue(channel, out var a)) { NoLocation++; return; }
            var owner = Replay.GameInformation.Players.FirstOrDefault(p => p.IsPlayersReplay);
            var loc = owner?.LastKnownLocation?.Location;
            if (loc == null) { NoOwner++; return; }
            double dx = a.X - loc.X, dy = a.Y - loc.Y, dz = a.Z - loc.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            Distances.Add(d);
            if (d <= Radius) Mine++; else Far++;
        }
    }
}

static class Program
{
    static int Main(string[] args)
    {
        var target = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (target == null) { Console.Error.WriteLine("usage: ReplayScan <fichier.replay|dossier>"); return 2; }
        double radius = 800;
        foreach (var a in args) if (a.StartsWith("--radius=") && double.TryParse(a.Substring(9), out var r)) radius = r;
        var files = Directory.Exists(target)
            ? Directory.GetFiles(target, "*.replay").OrderBy(File.GetLastWriteTimeUtc).ToArray()
            : new[] { target };
        var results = files.Select(f => Scan(f, radius)).ToList();
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static object Scan(string file, double radius)
    {
        try
        {
            var reader = new ChestReader { Radius = radius };
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var replay = reader.ReadReplay(stream, ParseType.Full);
            var owner = replay.GameInformation.Players.FirstOrDefault(p => p.IsPlayersReplay);
            return new
            {
                file = Path.GetFileName(file),
                timestamp = replay.Info?.Timestamp.ToString("o"),
                lengthSec = (replay.Info?.LengthInMs ?? 0) / 1000.0,
                owner = owner?.DisplayName,
                ownerFound = owner != null,
                placement = owner?.Placement,
                chests = reader.Mine,
                farOpened = reader.Far,
                diag = new
                {
                    radius,
                    searchedEvents = reader.SearchedEvents,
                    noChestLocation = reader.NoLocation,
                    noOwnerLocation = reader.NoOwner,
                    chestActors = reader.ChestActors,
                    chestActorsAtZero = reader.ZeroLocChests,
                    players = replay.GameInformation.Players.Count,
                    nearestDistances = reader.Distances.OrderBy(d => d).Take(40).Select(d => Math.Round(d)).ToList(),
                    archetypes = reader.Archetypes.OrderByDescending(k => k.Value).Take(30).ToDictionary(k => k.Key, k => k.Value),
                },
            };
        }
        catch (Exception e)
        {
            return new { file = Path.GetFileName(file), error = e.GetType().Name + ": " + e.Message };
        }
    }
}
