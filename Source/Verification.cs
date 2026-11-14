using System.Text.Json;
using ChestReplayReader;

internal static class Verification
{
    internal static async Task Run(string reportPath)
    {
        CheckTracking(Path.Combine(Path.GetDirectoryName(reportPath)!, "tracking-check-" + Guid.NewGuid().ToString("N")));
        var tracker = new ReplayTracker();
        var good = tracker.History.Where(ReplayTracker.Valid).ToList();
        var candidates = good.Where(r => File.Exists(r.FilePath)).ToList();
        var migrations = new List<object>();
        int verified = 0, changed = 0;
        foreach (var cached in candidates)
        {
            var info = new FileInfo(cached.FilePath);
            if (info.Length != cached.FileSize || info.LastWriteTimeUtc != cached.LastWriteUtc || ReplayFile.IsStillRecording(cached.FilePath)) { changed++; continue; }
            var actual = new ChestReader().Read(cached.FilePath);
            info.Refresh();
            if (!info.Exists || info.Length != cached.FileSize || info.LastWriteTimeUtc != cached.LastWriteUtc) { changed++; continue; }
            if (!ReplayTracker.Valid(actual) || (cached.ParserVersion == ChestReader.ParserVersion
                && (actual.ChestsOpened != cached.ChestsOpened || actual.ContainersBroken != cached.ContainersBroken)))
                throw new InvalidOperationException("Replay/cache mismatch: " + Path.GetFileName(cached.FilePath));
            if (cached.ParserVersion != ChestReader.ParserVersion
                && (actual.ChestsOpened != cached.ChestsOpened || actual.ContainersBroken != cached.ContainersBroken))
                migrations.Add(new { Replay = Path.GetFileName(cached.FilePath), PreviousParser = cached.ParserVersion,
                    PreviousSearched = cached.ChestsOpened, CurrentSearched = actual.ChestsOpened,
                    PreviousBroken = cached.ContainersBroken, CurrentBroken = actual.ContainersBroken,
                    Added = actual.AllContainers.Select(c => (c.ChestId,c.ChestClass)).Except(cached.AllContainers.Select(c => (c.ChestId,c.ChestClass))).Count(),
                    Removed = cached.AllContainers.Select(c => (c.ChestId,c.ChestClass)).Except(actual.AllContainers.Select(c => (c.ChestId,c.ChestClass))).Count() });
            tracker.History.Remove(cached); tracker.History.Add(actual); // Verification memory only; never saves the user's cache.
            if (actual.AllContainers.Count() != actual.ContainersAccessed) throw new InvalidOperationException("Container total mismatch.");
            verified++;
            File.WriteAllText(reportPath + ".progress.txt", $"Verified {verified} replays out of {candidates.Count} available saved recordings.");
        }
        if (verified < 1) throw new InvalidOperationException("No stable recorded matches available for verification.");
        good = tracker.History.Where(ReplayTracker.Valid).ToList();
        var testDir = Path.Combine(Path.GetDirectoryName(reportPath)!, "tracker-flow-" + Guid.NewGuid().ToString("N"));
        var demos = Path.Combine(testDir, "demos");
        var data = Path.Combine(testDir, "data");
        Directory.CreateDirectory(demos);
        var sources = candidates.GroupBy(r => r.ReplayGuid ?? r.FilePath).Select(g => g.First()).Take(2).ToList();
        if (sources.Count != 2) throw new InvalidOperationException("Two unique replays required for watcher check.");
        void CopyReplay(MatchResult r, string name)
        {
            var destination = Path.Combine(demos, name + ".replay");
            File.Copy(r.FilePath, destination);
            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow.AddMinutes(-2));
        }
        CopyReplay(sources[0], "first");
        var isolated = new ReplayTracker(data, demos);
        int notifications = 0;
        isolated.NewMatch += _ => notifications++;
        await isolated.Scan(); await isolated.Scan();
        if (isolated.History.Count != 1 || notifications != 0) throw new InvalidOperationException("Initial import check failed.");
        CopyReplay(sources[1], "new-match");
        await isolated.Scan(); await isolated.Scan(); await isolated.Scan();
        if (isolated.History.Count != 2 || notifications != 1) throw new InvalidOperationException("Automatic new replay check failed.");
        CopyReplay(sources[0], "duplicate");
        await isolated.Scan(); await isolated.Scan(); await isolated.Scan();
        if (isolated.History.Count != 2 || notifications != 1) throw new InvalidOperationException("Duplicate GUID handling failed.");
        var invalidPath = Path.Combine(demos, "invalid.replay");
        File.WriteAllBytes(invalidPath, new byte[] { 0, 1, 2, 3 });
        File.SetLastWriteTimeUtc(invalidPath, DateTime.UtcNow.AddMinutes(-2));
        await isolated.Scan(); await isolated.Scan();
        if (isolated.History.Count(ReplayTracker.Valid) != 2 || notifications != 1) throw new InvalidOperationException("Invalid replay handling failed.");
        var otherFolder = Path.Combine(testDir, "other-demos"); Directory.CreateDirectory(otherFolder);
        isolated.ChooseFolder(otherFolder);
        var restored = new ReplayTracker(data);
        if (restored.Folder != otherFolder || restored.History.Count != 3) throw new InvalidOperationException("Folder setting/history persistence failed.");
        tracker.Export(reportPath + ".csv");
        var summary = new
        {
            Result = "passed", VerifiedStableReplayFiles = verified, ChangedFilesSkipped = changed,
            SavedMatches = good.Count, SearchedContainers = good.Sum(r => r.ChestsOpened),
            BrokenContainers = good.Sum(r => r.ContainersBroken),
            PersonalSearchedChestsForOdds = good.Sum(r => ReplayTracker.CountSearched(r, "Chests") + ReplayTracker.CountSearched(r, "Sprite")),
            PersonalAllChestsForOdds = good.Sum(ReplayTracker.CountChests),
            TotalContainers = good.Sum(r => r.ContainersAccessed),
            ParserVersion = ChestReader.ParserVersion, ParserMigrations = migrations,
            FlowChecks = "Initial import, automatic new replay, duplicate GUID/no duplicate alert, invalid replay exclusion, folder selection persistence, history reload, CSV export, inclusive date filter, 24+ hour playtime, saved teammate persistence/removal, scoped shared-match counts and unavailable data"
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static ReplayTracker CheckTracking(string directory)
    {
        var tracker = new ReplayTracker(directory, Path.Combine(directory, "no-replays"));
        var start = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Local);
        MatchResult Match(string name, DateTime local, uint minutes, int searched, int broken)
        {
            var result = new MatchResult { ParserVersion = 4, FilePath = name + ".replay", MatchStartUtc = local.ToUniversalTime(),
                LengthMs = minutes * 60000, Complete = true, RecorderFound = true, TeamDataAvailable = true };
            result.MyChests.Add(new ChestOpen(1, "Chest", name));
            result.MyBrokenContainers.Add(new ChestOpen(2, "B_TieredChest_Relics", name + "-sprite"));
            result.MyBrokenContainers.Add(new ChestOpen(3, "Tiered_Ammo_Athena", name + "-ammo"));
            result.Teammates.Add(new TeammateMatch { Name = "Sample Teammate", Searched = searched, Broken = broken });
            return result;
        }
        tracker.History.Add(Match("old", start.AddTicks(-1), 60, 100, 100));
        tracker.History.Add(Match("included", start, 1500, 4, 2));
        tracker.History.Add(Match("later", start.AddDays(1), 45, 3, 1));
        var invalid = Match("invalid", start, 100, 90, 90); invalid.Complete = false; tracker.History.Add(invalid);
        tracker.SaveTeammate(" Sample Teammate "); tracker.SaveTeammate("sample teammate");
        tracker.SaveTeammate("No recorded actions");
        tracker.SetCountFrom(start);
        var row = tracker.TeammateRows().First();
        var teammateHistory = tracker.TeammateHistory("SAMPLE TEAMMATE");
        if (teammateHistory.Count != 2 || teammateHistory.Sum(r => r.Actions.Searched + r.Actions.Broken) != 10
            || teammateHistory[0].Match.FilePath != "later.replay" || tracker.TeammateHistory("Unknown teammate").Count != 0)
            throw new InvalidOperationException("Scoped per-teammate history failed.");
        if (tracker.CountedMatches.Count() != 2 || tracker.RecordedTime != TimeSpan.FromMinutes(1545)
            || tracker.CountedMatches.Sum(ReplayTracker.CountChests) != 4 || tracker.VisibleHistory.Count() != 3
            || tracker.SavedTeammates.Count != 2 || tracker.DetectedTeammates.Count != 1 || tracker.DetectedTeammates[0] != "Sample Teammate"
            || row.SharedMatches != "2" || row.Searched != "7" || row.Broken != "3" || row.Total != "10"
            || tracker.TeammateRows()[1].Searched != "Not recorded")
            throw new InvalidOperationException("Date-scoped time, chest or teammate counts failed.");
        var csv = Path.Combine(directory, "scope.csv"); tracker.Export(csv);
        if (File.ReadAllLines(csv).Length != 7 || File.ReadAllText(csv).Contains("old.replay") || File.ReadAllText(csv).Contains("invalid.replay"))
            throw new InvalidOperationException("Date-scoped CSV failed.");
        var restored = new ReplayTracker(directory);
        if (restored.CountFrom != start.Date || restored.SavedTeammates.Count != 2) throw new InvalidOperationException("Tracking preferences did not persist.");
        restored.RemoveTeammate("SAMPLE TEAMMATE");
        if (new ReplayTracker(directory).SavedTeammates.Count != 1) throw new InvalidOperationException("Saved name removal did not persist.");
        tracker.SetCountFrom(start.AddYears(1));
        if (tracker.CountedMatches.Any() || tracker.RecordedTime != TimeSpan.Zero || tracker.DetectedTeammates.Any() || tracker.TeammateRows()[0].SharedMatches != "—")
            throw new InvalidOperationException("Empty date range did not clear counts.");
        tracker.SetCountFrom(null);
        if (tracker.CountedMatches.Count() != 3 || tracker.RecordedTime != TimeSpan.FromMinutes(1605)) throw new InvalidOperationException("All-time reset failed.");
        return tracker;
    }
}
