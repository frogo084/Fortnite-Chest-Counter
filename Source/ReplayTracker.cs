using System.Text.Json;
using ChestReplayReader;

internal sealed class ReplayTracker
{
    readonly string dataDir;
    readonly Dictionary<string, (long, DateTime)> stable = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (long, DateTime)> processed = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> startupFiles = new(StringComparer.OrdinalIgnoreCase);
    public List<MatchResult> History { get; private set; } = new();
    TrackingPreferences preferences = new();
    public DateTime? CountFrom => preferences.CountFrom?.Date;
    public IReadOnlyList<string> SavedTeammates => preferences.Teammates;
    public IEnumerable<MatchResult> VisibleHistory => History.Where(InRange);
    public IEnumerable<MatchResult> CountedMatches => VisibleHistory.Where(Valid);
    public TimeSpan RecordedTime => TimeSpan.FromMilliseconds(CountedMatches.Sum(r => (long)r.LengthMs));
    public IReadOnlyList<string> DetectedTeammates => CountedMatches.SelectMany(r => r.Teammates)
        .Where(t => !string.IsNullOrWhiteSpace(t.Name)).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Key).ToList();
    public string Folder { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
    public string Status { get; private set; } = "Watching for your next replay…";
    public bool Busy { get; private set; }
    public bool Stopped { get; set; }
    public event Action? Changed;
    public event Action<MatchResult>? NewMatch;
    string Store => Path.Combine(dataDir, "history.json");

    public ReplayTracker(string? dataDirectory = null, string? replayFolder = null)
    {
        dataDir = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteLootTracker");
        if (replayFolder != null) Folder = replayFolder;
        try
        {
            var settings = Path.Combine(dataDir, "settings.json");
            if (File.Exists(settings)) Folder = JsonSerializer.Deserialize<string>(File.ReadAllText(settings)) ?? Folder;
        }
        catch (Exception) { /* The default folder remains usable if settings are corrupt. */ }
        try
        {
            var trackingFile = Path.Combine(dataDir, "tracking.json");
            if (File.Exists(trackingFile)) preferences = JsonSerializer.Deserialize<TrackingPreferences>(File.ReadAllText(trackingFile)) ?? new();
            preferences.Teammates ??= new();
        }
        catch (Exception) { Status = "Saved tracking preferences could not be loaded."; }
        try
        {
            if (File.Exists(Store)) History = JsonSerializer.Deserialize<List<MatchResult>>(File.ReadAllText(Store)) ?? new();
        }
        catch (Exception ex)
        {
            // Preserve unreadable history before any new scans can overwrite it.
            if (File.Exists(Store)) File.Copy(Store, Path.Combine(dataDir, $"history-recovery-{DateTime.UtcNow:yyyyMMddHHmmss}.json"), false);
            Status = "History recovery saved. " + ex.Message;
        }
        RememberStartup();
    }

    void RememberStartup()
    {
        try { startupFiles = Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.replay").ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Choose an accessible replay folder in Settings."; }
    }
    public void ChooseFolder(string path)
    {
        if (Busy) throw new InvalidOperationException("Wait for the current scan to finish.");
        path = Path.GetFullPath(path);
        _ = Directory.GetFiles(path, "*.replay");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), JsonSerializer.Serialize(path));
        Folder = path; stable.Clear(); processed.Clear(); RememberStartup();
        Changed?.Invoke();
    }
    public async Task Scan()
    {
        if (Busy || Stopped) return;
        Busy = true;
        try
        {
            if (!Directory.Exists(Folder)) { Status = "Choose your replay folder in Settings to start tracking."; return; }
            var files = new DirectoryInfo(Folder).GetFiles("*.replay").OrderByDescending(f => f.LastWriteTimeUtc);
            foreach (var file in files)
            {
                if (Stopped) return;
                var signature = (file.Length, file.LastWriteTimeUtc);
                if (processed.TryGetValue(file.FullName, out var parsed) && parsed == signature) continue;
                if (History.Any(r => r.ParserVersion == ChestReader.ParserVersion && r.FilePath == file.FullName && (r.FileSize, r.LastWriteUtc) == signature)) continue;
                if (DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromSeconds(15)) continue;
                if (!stable.TryGetValue(file.FullName, out var last) || last != signature) { stable[file.FullName] = signature; continue; }
                if (ReplayFile.IsStillRecording(file.FullName)) continue;
                Status = "Reading " + file.Name + "…"; Changed?.Invoke();
                var result = await Task.Run(() => new ChestReader().Read(file.FullName));
                if (Stopped) return;
                file.Refresh();
                if (!file.Exists || (file.Length, file.LastWriteTimeUtc) != signature) continue;
                var alreadyKnownMatch = result.ReplayGuid != null && History.Any(r => r.ReplayGuid == result.ReplayGuid);
                History.RemoveAll(r => r.FilePath.Equals(file.FullName, StringComparison.OrdinalIgnoreCase) || (result.ReplayGuid != null && r.ReplayGuid == result.ReplayGuid));
                History.Add(result);
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(Store + ".tmp", JsonSerializer.Serialize(History));
                File.Move(Store + ".tmp", Store, true);
                processed[file.FullName] = signature;
                Changed?.Invoke();
                if (!alreadyKnownMatch && !startupFiles.Contains(file.FullName) && Valid(result) && InRange(result)) NewMatch?.Invoke(result);
            }
            Status = "Watching for your next replay…";
        }
        catch (Exception ex) { Status = "Scan needs attention: " + ex.Message; }
        finally { Busy = false; if (!Stopped) Changed?.Invoke(); }
    }
    public static bool Valid(MatchResult r) => r.ParserVersion >= 2 && r.Complete && r.RecorderFound && !r.IsLive;
    public bool InRange(MatchResult r) => !CountFrom.HasValue || (r.MatchStartUtc ?? r.LastWriteUtc).ToLocalTime().Date >= CountFrom.Value;
    public void SetCountFrom(DateTime? date)
    {
        preferences.CountFrom = date?.Date; SaveTracking(); Changed?.Invoke();
    }
    public void SaveTeammate(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw new ArgumentException("Enter an Epic display name (1–64 characters).");
        if (!preferences.Teammates.Contains(name, StringComparer.OrdinalIgnoreCase)) preferences.Teammates.Add(name);
        SaveTracking(); Changed?.Invoke();
    }
    public void RemoveTeammate(string name)
    {
        preferences.Teammates.RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)); SaveTracking(); Changed?.Invoke();
    }
    void SaveTracking()
    {
        Directory.CreateDirectory(dataDir);
        var path = Path.Combine(dataDir, "tracking.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(preferences)); File.Move(path + ".tmp", path, true);
    }
    public IReadOnlyList<SavedTeammateRow> TeammateRows()
    {
        var matches = CountedMatches.ToList();
        return SavedTeammates.Select(name =>
        {
            var observed = matches.SelectMany(r => r.Teammates.Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
            var searched = observed.Sum(t => t.Searched); var broken = observed.Sum(t => t.Broken);
            return new SavedTeammateRow(name, observed.Count == 0 ? "—" : observed.Count.ToString("N0"),
                searched + broken == 0 ? "Not recorded" : searched.ToString("N0"), searched + broken == 0 ? "—" : broken.ToString("N0"),
                searched + broken == 0 ? "—" : (searched + broken).ToString("N0"));
        }).ToList();
    }
    public IReadOnlyList<LootTracker.TeammateHistoryRow> TeammateHistory(string name) => CountedMatches
        .OrderByDescending(r => r.MatchStartUtc ?? r.LastWriteUtc)
        .SelectMany(r => r.Teammates.Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(t => new LootTracker.TeammateHistoryRow(r, t))).ToList();
    public static int Count(MatchResult r, string kind) => r.AllContainers.Count(c => Kind(c.ChestClass) == kind);
    public static int CountSearched(MatchResult r, string kind) => r.MyChests.Count(c => Kind(c.ChestClass) == kind);
    public static int CountChests(MatchResult r) => Count(r, "Chests") + Count(r, "Sprite");
    public static string Kind(string raw)
    {
        var b = raw.ToLowerInvariant();
        if (b.Contains("sprite") || b.Contains("relic") || b.Contains("extraction") || b.Contains("booty")) return "Sprite";
        if (b.Contains("ammo")) return "Ammo";
        if (b.Contains("chest")) return "Chests";
        if (b.Contains("food")) return "Food box";
        if (b.Contains("cooler")) return "Cooler";
        if (b.Contains("safe")) return "Safe";
        if (b.Contains("cash") || b.Contains("register")) return "Cash register";
        return "Other container";
    }
    public static double Chance(double count, double probability)
    {
        if (count <= 0 || probability <= 0) return 0;
        if (probability >= 1) return 1;
        return Math.Clamp(1 - Math.Exp(count * Math.Log(1 - probability)), 0, 1);
    }
    public void Export(string destination)
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "Match,Replay,Container,Blueprint,Method,Count" };
        foreach (var r in CountedMatches.OrderBy(r => r.MatchStartUtc))
            foreach (var method in new[] { (Name: "Searched", Items: r.MyChests), (Name: "Broken", Items: r.MyBrokenContainers) })
                foreach (var g in method.Items.GroupBy(c => c.ChestClass))
                    lines.Add(string.Join(",", Q((r.MatchStartUtc ?? r.LastWriteUtc).ToLocalTime().ToString("s")), Q(Path.GetFileName(r.FilePath)), Q(Kind(g.Key)), Q(g.Key), Q(method.Name), g.Count()));
        File.WriteAllLines(destination, lines, System.Text.Encoding.UTF8);
    }
    public static void SelfCheck()
    {
        var counts = new MatchResult();
        counts.MyChests.Add(new ChestOpen(1, "Tiered_Chest_Athena", "searched"));
        counts.MyChests.Add(new ChestOpen(2, "B_TieredChest_Relics", "sprite"));
        counts.MyBrokenContainers.Add(new ChestOpen(3, "Tiered_Chest_Athena", "broken"));
        counts.MyBrokenContainers.Add(new ChestOpen(4, "Tiered_Ammo_Athena", "ammo"));
        if (CountChests(counts) != 3) throw new InvalidOperationException("Combined searched/broken chest count failed.");
        if (Kind("B_TieredChest_Relics_C") != "Sprite" || Kind("Tiered_Ammo_Athena") != "Ammo"
            || Chance(0, 1) != 0 || Chance(1, 1) != 1 || Chance(20, 0) != 0
            || Math.Abs(Chance(2, .5) - .75) > 1e-12
            || Math.Abs(Chance(137500, 1.0 / 137500) - .632122) > .00001)
            throw new InvalidOperationException("Classification or probability self-check failed.");
    }
}

internal sealed class TrackingPreferences
{
    public DateTime? CountFrom { get; set; }
    public List<string> Teammates { get; set; } = new();
}
internal sealed record SavedTeammateRow(string Name, string SharedMatches, string Searched, string Broken, string Total);
