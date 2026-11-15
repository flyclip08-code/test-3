using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Items;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

class ChestReader : FortniteReplayReader.ReplayReader
{
    public double Radius = 800;
    public int Mine, Far, SearchedEvents, NoLocation, NoOwner, ChestActors, ZeroLocChests;
    public readonly List<double> Distances = new();
    public readonly Dictionary<string, int> Archetypes = new();
    readonly HashSet<uint> seen = new();
    readonly Dictionary<uint, uint> pawnState = new();
    uint? recorderState;
    FVector? ownerLoc;
    static readonly Regex Interesting = new("chest|container|treasure|loot|cache|crate|barrel|vault|relic", RegexOptions.IgnoreCase);

    public ChestReader() : base(null, ParseMode.Debug) { }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);
        seen.Remove(channelIndex);
        var ch = Channels[channelIndex];
        if (ch?.Actor == null) return;
        if (_netGuidCache.TryGetPathName(ch.ArchetypeId ?? 0, out var path) && Interesting.IsMatch(path))
        {
            Archetypes[path] = Archetypes.TryGetValue(path, out var n) ? n + 1 : 1;
            if (path.Contains("chest", StringComparison.OrdinalIgnoreCase))
            {
                ChestActors++;
                var l = ch.Actor.Location;
                if (l == null || (l.X == 0 && l.Y == 0 && l.Z == 0)) ZeroLocChests++;
            }
        }
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        switch (exportGroup)
        {
            case GameState gs:
                if (gs.RecorderPlayerState != null) recorderState = gs.RecorderPlayerState.Value;
                break;
            case PlayerPawn pawn:
                if (pawn.PlayerState.HasValue) pawnState[channelIndex] = pawn.PlayerState.Value;
                if (pawn.ReplicatedMovement.HasValue
                    && recorderState.HasValue
                    && pawnState.TryGetValue(channelIndex, out var st) && st == recorderState.Value)
                {
                    ownerLoc = pawn.ReplicatedMovement.Value.Location;
                }
                break;
            case BaseContainer bc when bc.bAlreadySearched && (bc is Chest || bc is FactionChest):
                SearchedEvents++;
                if (!seen.Add(channelIndex)) break;
                var loc = Channels[channelIndex]?.Actor?.Location;
                if (loc == null) { NoLocation++; break; }
                if (ownerLoc == null) { NoOwner++; break; }
                double dx = loc.X - ownerLoc.X, dy = loc.Y - ownerLoc.Y, dz = loc.Z - ownerLoc.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                Distances.Add(d);
                if (d <= Radius) Mine++; else Far++;
                break;
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
            var replay = reader.ReadReplay(stream);
            var owner = replay.PlayerData?.FirstOrDefault(p => p.IsReplayOwner);
            return new
            {
                file = Path.GetFileName(file),
                timestamp = replay.Info.Timestamp.ToString("o"),
                lengthSec = replay.Info.LengthInMs / 1000.0,
                owner = owner?.PlayerName,
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
                    players = replay.PlayerData?.Count() ?? 0,
                    nearestDistances = reader.Distances.OrderBy(d => d).Take(40).Select(d => Math.Round(d)).ToList(),
                    archetypes = reader.Archetypes.OrderByDescending(k => k.Value).Take(30).ToDictionary(k => k.Key, k => k.Value),
                },
            };
        }
        catch (Exception e)
        {
            return new
            {
                file = Path.GetFileName(file),
                error = e.GetType().Name + ": " + e.Message,
                where = (e.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()).ToArray(),
            };
        }
    }
}
