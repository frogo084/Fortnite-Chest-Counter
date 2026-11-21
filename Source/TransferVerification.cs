using System.Text.Json;
using ChestReplayReader;

internal static class TransferVerification
{
    internal static void Run(string directory)
    {
        var laptopDir = Path.Combine(directory, "laptop");
        var desktopDir = Path.Combine(directory, "desktop");
        Directory.CreateDirectory(laptopDir);
        Directory.CreateDirectory(desktopDir);
        var day = new DateTime(2026, 1, 2);
        MatchResult Match(string guid, string file, DateTime date) => new()
        {
            ReplayGuid = guid, FilePath = file, ParserVersion = ChestReader.ParserVersion,
            Complete = true, RecorderFound = true, MatchStartUtc = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime(),
            LastWriteUtc = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime(), FileSize = 100, LengthMs = 60000,
            MyChests = new() { new ChestOpen(1, "Chest", "searched") },
            MyBrokenContainers = new() { new ChestOpen(2, "B_TieredChest_Relics", "broken") },
            Teammates = new() { new TeammateMatch { Name = "Sample teammate", Searched = 2, Broken = 1 } }
        };

        // Start with files written by 3.1.2, including a match hidden by its date filter.
        var laptopHistory = new List<MatchResult>
        {
            Match("shared-recording", @"C:\SampleLaptop\Demos\shared.replay", day),
            Match("laptop-recording", @"C:\SampleLaptop\Demos\older.replay", day.AddDays(-1))
        };
        File.WriteAllText(Path.Combine(laptopDir, "history.json"), JsonSerializer.Serialize(laptopHistory));
        File.WriteAllText(Path.Combine(laptopDir, "tracking.json"), JsonSerializer.Serialize(
            new TrackingPreferences { CountFrom = day, Teammates = new() { "Sample teammate" } }));
        var laptop = new ReplayTracker(laptopDir, Path.Combine(laptopDir, "demos"));
        if (laptop.History.Count != 2 || laptop.CountFrom != day) throw new InvalidOperationException("Legacy data migration failed.");
        var backup = Path.Combine(directory, "laptop.fchest");
        laptop.ExportData(backup);
        if (File.ReadAllText(backup).Contains("SampleLaptop")) throw new InvalidOperationException("Transfer exposed the source PC path.");
        if (laptop.History[0].FilePath != laptopHistory[0].FilePath) throw new InvalidOperationException("Export changed the source history.");

        var desktop = new ReplayTracker(desktopDir, Path.Combine(desktopDir, "demos"));
        desktop.History.Add(Match("SHAREDRECORDING", @"C:\SampleDesktop\Demos\renamed.replay", day));
        desktop.History.Add(Match("desktop-recording", "desktop.replay", day.AddDays(1)));
        desktop.SaveTeammate("Desktop teammate");
        var desktopFolder = desktop.Folder;
        if (desktop.ImportData(backup) != 1 || desktop.History.Count != 3
            || desktop.CountedMatches.Count() != 2 || desktop.CountFrom != day
            || desktop.RecordedTime != TimeSpan.FromMinutes(2)
            || desktop.CountedMatches.Sum(ReplayTracker.CountChests) != 4
            || desktop.SavedTeammates.Count != 2 || desktop.Folder != desktopFolder
            || desktop.History[0].FilePath != @"C:\SampleDesktop\Demos\renamed.replay")
            throw new InvalidOperationException("Transfer lost or duplicated matches, preferences, or the local replay folder.");
        if (desktop.ImportData(backup) != 0 || desktop.History.Count != 3)
            throw new InvalidOperationException("Repeated import counted the same recording twice.");
        if (!File.Exists(Path.Combine(desktopDir, "before-import.fchest")))
            throw new InvalidOperationException("The pre-import backup was not saved.");
        var reopened = new ReplayTracker(desktopDir, desktopFolder);
        if (reopened.History.Count != 3 || reopened.CountFrom != day || reopened.SavedTeammates.Count != 2)
            throw new InvalidOperationException("Imported data did not survive restart.");
        reopened.SetCountFrom(null);
        reopened.SaveTeammate("Saved after import");
        reopened = new ReplayTracker(desktopDir, desktopFolder);
        if (reopened.CountFrom != null || reopened.CountedMatches.Sum(ReplayTracker.CountChests) != 6
            || reopened.SavedTeammates.Count != 3)
            throw new InvalidOperationException("Updates after import were not persisted.");

        var before = File.ReadAllText(Path.Combine(desktopDir, "data.json"));
        foreach (var invalid in new[] { "{}", "{", "{\"Format\":\"Fortnite-Chest-Counter\",\"Version\":999,\"History\":[],\"Tracking\":{}}" })
        {
            var bad = Path.Combine(directory, "bad.fchest");
            File.WriteAllText(bad, invalid);
            var rejected = false;
            try { reopened.ImportData(bad); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { rejected = true; }
            if (!rejected || File.ReadAllText(Path.Combine(desktopDir, "data.json")) != before)
                throw new InvalidOperationException("Invalid import changed the saved state.");
        }

        // A failed final file replacement must leave the in-memory and legacy states intact.
        var blockedDir = Path.Combine(directory, "blocked");
        Directory.CreateDirectory(blockedDir);
        Directory.CreateDirectory(Path.Combine(blockedDir, "data.json"));
        var blocked = new ReplayTracker(blockedDir, Path.Combine(blockedDir, "demos"));
        var failed = false;
        try { blocked.ImportData(backup); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        if (!failed || blocked.History.Count != 0 || blocked.CountFrom != null)
            throw new InvalidOperationException("Failed import changed the current state.");

        var legacy = new ReplayTracker(Path.Combine(directory, "guidless"));
        var guidless = Match("unused", @"C:\SampleLaptop\Demos\legacy.replay", day);
        guidless.ReplayGuid = null;
        legacy.History.Add(guidless);
        var oldBackup = Path.Combine(directory, "guidless.fchest");
        legacy.ExportData(oldBackup);
        if (reopened.ImportData(oldBackup) != 1 || reopened.ImportData(oldBackup) != 0)
            throw new InvalidOperationException("Repeated import of a legacy recording duplicated its counts.");
    }
}
