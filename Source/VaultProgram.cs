using System.Text.Json;
using ChestReplayReader;

internal static class VaultProgram
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--capture-squad-sample" or "--capture-settings-sample")
        {
            var capture = Path.GetFullPath(args[1]);
            var fixture = Verification.CheckTracking(capture + ".fixture");
            fixture.SaveTeammate("Another sample teammate");
            fixture.SetCountFrom(new DateTime(2026, 1, 2));
            var mode = args[0] == "--capture-settings-sample" ? "--capture-settings" : "--capture-squad";
            new System.Windows.Application().Run(new VaultWindow(fixture, capture, mode));
            return;
        }
        if (args.Length == 2 && args[0] is "--verify" or "--verify-ui")
        {
            var report = Path.GetFullPath(args[1]);
            ChestReader.SelfCheck(); ReplayTracker.SelfCheck(); LootTracker.TokenOdds.SelfCheck();
            TransferVerification.Run(report + ".transfer-fixture");
            if (args[0] == "--verify") Verification.Run(report).GetAwaiter().GetResult();
            var application = new System.Windows.Application();
            var directory = report + ".ui-fixture";
            var fixture = Verification.CheckTracking(directory);
            var window = new VaultWindow(fixture, report + ".unused.png");
            window.VerifyReplayPanel();
            window.VerifyTrackingPanel(fixture);
            window.VerifyOverviewDate(directory);
            window.Close();
            File.WriteAllText(report + ".ui.txt", "PASS: replay odds, playtime, date persistence, teammates, legacy data migration, transfer merge, duplicate import, portable paths, invalid import rejection and failed-write recovery using isolated sample data.");
            return;
        }
        if (args.Length == 2 && args[0] is "--capture" or "--capture-forecast" or "--capture-past" or "--capture-history" or "--capture-personal" or "--capture-squad" or "--capture-controls")
        {
            new System.Windows.Application().Run(new VaultWindow(Path.GetFullPath(args[1]), args[0]));
            return;
        }
        if (args.Length == 1 && args[0] == "--self-test")
        {
            ChestReader.SelfCheck();
            ReplayTracker.SelfCheck();
            LootTracker.TokenOdds.SelfCheck();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: searched/broken attribution; cancelled search exclusion; repeated updates, opened-then-destroyed and unknown destroyers excluded; refills; exact numbered container classes; late class/field metadata; stable identities; categories; odds forecasts, boosts and party scenarios.");
            return;
        }
        if (args.Length == 2 && args[0] == "--scan")
        {
            var result = new ChestReader().Read(args[1]);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "scan-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        using var instance = new Mutex(true, @"Local\FortniteLootTracker", out var first);
        if (!first)
        {
            System.Windows.MessageBox.Show("Fortnite Loot Tracker is already open. Use its taskbar or tray icon to bring it back.", "Fortnite Loot Tracker");
            return;
        }
        var app = new System.Windows.Application();
        app.Run(new VaultWindow());
    }
}
