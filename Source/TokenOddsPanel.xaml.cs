using System.Globalization;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;

namespace LootTracker;

public partial class TokenOddsPanel : Controls.UserControl
{
    bool initialized, past = true, writingReplayInputs, useReplayCounts = true, useReplayAverage = true;
    int party = 1, replayCount, replayMatches;
    double boost = 1.5, replayAverage = 25;

    public TokenOddsPanel()
    {
        InitializeComponent();
        DaysInput.Text = Math.Max(0, (new DateTime(2026, 12, 5) - DateTime.Today).Days).ToString();
        initialized = true;
        RefreshOdds();
    }
    public void SetReplayCounts(int count, double average, int? matches = null)
    {
        replayCount = count; replayAverage = average;
        if (matches.HasValue) replayMatches = matches.Value;
        ReplayCountStatus.Text = $"Your replays: {count:N0} chests · searched + broken · {replayMatches:N0} matches";
        writingReplayInputs = true;
        try
        {
            if (useReplayCounts) OpenedInput.Text = count.ToString();
            if (useReplayAverage) AverageInput.Text = average.ToString("0.##");
        }
        finally { writingReplayInputs = false; }
        RefreshOdds();
    }
    public void SetMode(bool pastMode) { past = pastMode; RefreshOdds(); }
    void ForecastClicked(object sender, Wpf.RoutedEventArgs e) => SetMode(false);
    void PastClicked(object sender, Wpf.RoutedEventArgs e) => SetMode(true);
    void InputChanged(object sender, Controls.TextChangedEventArgs e)
    {
        if (!initialized) return;
        if (!writingReplayInputs && ReferenceEquals(sender, OpenedInput)) useReplayCounts = false;
        if (!writingReplayInputs && ReferenceEquals(sender, AverageInput)) useReplayAverage = false;
        RefreshOdds();
    }
    void BoostClicked(object sender, Wpf.RoutedEventArgs e) { boost = double.Parse(((Controls.Button)sender).Tag.ToString()!, CultureInfo.InvariantCulture); RefreshOdds(); }
    void PartyClicked(object sender, Wpf.RoutedEventArgs e) { party = int.Parse(((Controls.Button)sender).Tag.ToString()!); RefreshOdds(); }
    void ReplayCountsClicked(object sender, Wpf.RoutedEventArgs e)
    {
        party = 1;
        useReplayCounts = useReplayAverage = true;
        BoostedInput.Text = "0";
        SetReplayCounts(replayCount, replayAverage);
    }
    void HelpClicked(object sender, Wpf.RoutedEventArgs e) => HelpText.Visibility = HelpText.Visibility == Wpf.Visibility.Visible ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
    void ReferenceClicked(object sender, Wpf.RoutedEventArgs e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://themathbehindeverything.com/fortnite-pirate-token/") { UseShellExecute = true });

    static double Read(Controls.TextBox field, double min, double max)
    {
        if (!double.TryParse(field.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{Wpf.Automation.AutomationProperties.GetName(field)} must be between {min:N0} and {max:N0}.");
        return value;
    }
    static void Select(Controls.Button button, bool active)
    {
        button.Background = new Media.SolidColorBrush(active ? Media.Color.FromRgb(242, 201, 104) : Media.Colors.Transparent);
        button.Foreground = new Media.SolidColorBrush(active ? Media.Color.FromRgb(10, 10, 8) : Media.Color.FromRgb(242, 201, 104));
    }
    void RefreshOdds()
    {
        if (!initialized) return;
        ForecastFields.Visibility = past ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
        PastFields.Visibility = past ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        ModeSubtitle.Text = past ? "What your chest openings were worth." : "Plan the rest of your season.";
        Select(ForecastMode, !past); Select(PastMode, past);
        Select(BoostOne, boost == 1.5); Select(BoostTwo, boost == 2);
        foreach (var button in new[] { Solo, Duo, Trio, Squad }) Select(button, int.Parse(button.Tag.ToString()!) == party);
        try
        {
            var rate = Read(RateInput, 1, 1e9);
            OddsEstimate estimate;
            if (past)
                estimate = TokenOdds.Calculate(Read(OpenedInput, 0, 1e9), Read(BoostedInput, 0, 1e9), rate, boost, party);
            else
                estimate = TokenOdds.Forecast(Read(HoursInput, 0, 24), Read(DaysInput, 0, 3660), Read(AverageInput, 0, 1000), Read(MinutesInput, .01, 240), Read(BoostHoursInput, 0, 87840), rate, boost, party);
            OddsError.Text = "";
            OddsRatio.Text = estimate.Ratio;
            OddsPercentage.Text = estimate.Percent;
            ResultDescription.Text = past ? $"chance of at least one drop across {estimate.Chests:N0} {(party == 1 ? "personal chests" : "estimated party chests")}." : $"chance of at least one drop across your planned {(party == 1 ? "chest openings" : "estimated party chest openings")}.";
            StatOneLabel.Text = past ? (party == 1 ? "YOUR CHESTS" : "PARTY ESTIMATE") : "MATCHES";
            StatTwoLabel.Text = past ? "CHANCE MISSED" : (party == 1 ? "CHESTS" : "PARTY ESTIMATE");
            StatThreeLabel.Text = past ? "NEXT NORMAL CHEST" : "MATCHES FOR 50%";
            StatOne.Text = (past ? estimate.Chests : estimate.Matches).ToString("N0");
            StatTwo.Text = past ? (100 * estimate.MissChance).ToString("0.0") + "%" : estimate.Chests.ToString("N0");
            StatThree.Text = past ? "1 in " + rate.ToString("N0") : double.IsInfinity(estimate.MatchesForHalf) ? "—" : estimate.MatchesForHalf.ToString("N0");
            ResultNote.Text = past ? "Your next normal chest keeps the same per-chest odds, however many you have already opened." : "More openings give you more attempts, not a guarantee. The estimate uses the playtime and rates you enter.";
        }
        catch (ArgumentException ex)
        {
            OddsError.Text = ex.Message;
            OddsRatio.Text = "Check inputs"; OddsPercentage.Text = ""; ResultDescription.Text = "Adjust the inputs to calculate your odds.";
            StatOne.Text = StatTwo.Text = StatThree.Text = "—"; ResultNote.Text = "";
        }
    }
    internal void ConfigureCapture(bool pastMode)
    {
        SetReplayCounts(2000, 25);
        HoursInput.Text = "3"; DaysInput.Text = "24";
        MinutesInput.Text = "20"; BoostHoursInput.Text = "0"; BoostedInput.Text = "0";
        SetMode(pastMode);
    }
    internal void VerifyReplayBinding()
    {
        SetReplayCounts(1533, 15.33, 100); party = 4; RefreshOdds();
        SetReplayCounts(1540, 15.4, 100);
        ReplayCountsClicked(this, new Wpf.RoutedEventArgs());
        if (party != 1 || OpenedInput.Text != "1540") throw new InvalidOperationException("Replay button did not use fresh personal counts.");
        SetReplayCounts(1550, 15.5, 100);
        if (OpenedInput.Text != "1550") throw new InvalidOperationException("Live counts failed to update.");
        OpenedInput.Text = "2000"; SetReplayCounts(1560, 15.6, 100);
        if (OpenedInput.Text != "2000") throw new InvalidOperationException("Manual scenario was overwritten.");
        ReplayCountsClicked(this, new Wpf.RoutedEventArgs());
        if (OpenedInput.Text != "1560" || BoostedInput.Text != "0") throw new InvalidOperationException("Replay reset failed.");
        SetMode(true);
        if (string.IsNullOrWhiteSpace(OddsPercentage.Text)) throw new InvalidOperationException("Past result missing.");
        SetMode(false);
        if (string.IsNullOrWhiteSpace(OddsPercentage.Text)) throw new InvalidOperationException("Forecast result missing.");
    }
}
