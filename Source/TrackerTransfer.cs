using System.Text.Json;
using ChestReplayReader;

internal sealed partial class ReplayTracker
{
    const string BackupFormat = "Fortnite-Chest-Counter";
    const long MaxBackupBytes = 128L * 1024 * 1024;

    void SaveState(List<MatchResult> history, TrackingPreferences tracking)
    {
        Directory.CreateDirectory(dataDir);
        WriteBackup(Store, new TrackerBackup { History = history, Tracking = tracking });
    }

    static void WriteBackup(string path, TrackerBackup backup, long maxBytes = long.MaxValue)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, backup);
                if (stream.Length > maxBytes) throw new InvalidDataException("The backup is larger than 128 MB.");
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    static TrackerBackup ReadBackup(string path, long maxBytes = long.MaxValue)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes) throw new InvalidDataException("The backup is larger than 128 MB.");
        var backup = JsonSerializer.Deserialize<TrackerBackup>(stream)
            ?? throw new InvalidDataException("The backup is empty.");
        if (backup.Format != BackupFormat || backup.Version != 1)
            throw new InvalidDataException("Choose a Fortnite Chest Counter data backup from a supported version.");
        ValidateBackup(backup);
        return backup;
    }

    static void ValidateBackup(TrackerBackup backup)
    {
        if (backup.History == null || backup.Tracking == null || backup.Tracking.Teammates == null
            || backup.Tracking.Teammates.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl)))
            throw new InvalidDataException("The backup contains invalid tracking data.");
        foreach (var match in backup.History)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.FilePath)
                || match.MyChests == null || match.MyBrokenContainers == null || match.Teammates == null
                || match.FileSize < 0 || match.ParserVersion < 0
                || match.AllContainers.Any(chest => chest == null || string.IsNullOrWhiteSpace(chest.ChestClass)
                    || chest.ChestId == null || !float.IsFinite(chest.Time) || chest.Time < 0)
                || match.Teammates.Any(player => player == null || string.IsNullOrWhiteSpace(player.Name)
                    || player.Searched < 0 || player.Broken < 0))
                throw new InvalidDataException("The backup contains an invalid match.");
        }
    }

    static string ReplayName(MatchResult match) => match.FilePath.Replace('\\', '/').Split('/').Last();

    static bool SameMatch(MatchResult left, MatchResult right)
    {
        if (!string.IsNullOrWhiteSpace(left.ReplayGuid) && !string.IsNullOrWhiteSpace(right.ReplayGuid))
            return left.ReplayGuid.Replace("-", "").Equals(right.ReplayGuid.Replace("-", ""), StringComparison.OrdinalIgnoreCase);
        return ReplayName(left).Equals(ReplayName(right), StringComparison.OrdinalIgnoreCase)
            && left.FileSize == right.FileSize && left.LastWriteUtc == right.LastWriteUtc
            && left.MatchStartUtc == right.MatchStartUtc;
    }

    public void ExportData(string destination)
    {
        if (Busy) throw new InvalidOperationException("Wait for the current scan to finish.");
        var backup = JsonSerializer.Deserialize<TrackerBackup>(JsonSerializer.Serialize(
            new TrackerBackup { History = History, Tracking = preferences }))!;
        // A transfer needs replay names for history display, but never the source PC's user paths.
        foreach (var match in backup.History) match.FilePath = ReplayName(match);
        if (backup.Tracking.CountFrom is DateTime date)
            backup.Tracking.CountFrom = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        WriteBackup(destination, backup, MaxBackupBytes);
    }

    public int ImportData(string source)
    {
        if (Busy) throw new InvalidOperationException("Wait for the current scan to finish.");
        var backup = ReadBackup(source, MaxBackupBytes);
        var merged = History.ToList();
        var added = 0;
        foreach (var incoming in backup.History)
        {
            incoming.FilePath = ReplayName(incoming);
            var index = merged.FindIndex(existing => SameMatch(existing, incoming));
            if (index < 0)
            {
                merged.Add(incoming);
                added++;
            }
            else if ((!Valid(merged[index]) && Valid(incoming))
                || (Valid(merged[index]) == Valid(incoming) && incoming.ParserVersion > merged[index].ParserVersion))
            {
                // Preserve the local replay path so future scans can still recognize its cached result.
                incoming.FilePath = merged[index].FilePath;
                merged[index] = incoming;
            }
        }

        var tracking = new TrackingPreferences
        {
            CountFrom = backup.Tracking.CountFrom?.Date,
            Teammates = preferences.Teammates.Concat(backup.Tracking.Teammates)
                .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        Directory.CreateDirectory(dataDir);
        WriteBackup(Path.Combine(dataDir, "before-import.fchest"), new TrackerBackup { History = History, Tracking = preferences });
        SaveState(merged, tracking);
        History = merged;
        preferences = tracking;
        Changed?.Invoke();
        return added;
    }
}

internal sealed class TrackerBackup
{
    [System.Text.Json.Serialization.JsonRequired]
    public string Format { get; set; } = "Fortnite-Chest-Counter";
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 1;
    [System.Text.Json.Serialization.JsonRequired]
    public List<MatchResult> History { get; set; } = new();
    [System.Text.Json.Serialization.JsonRequired]
    public TrackingPreferences Tracking { get; set; } = new();
}
