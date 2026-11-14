using Wpf = System.Windows;
using ChestReplayReader;

namespace LootTracker;

public partial class TeammateHistoryWindow : Wpf.Window
{
    internal TeammateHistoryWindow(Wpf.Window owner, ReplayTracker tracker, string name)
    {
        InitializeComponent();
        Owner = owner; Icon = owner.Icon; FontFamily = owner.FontFamily;
        Resources.MergedDictionaries.Add(owner.Resources);
        Title = name + " · shared match history"; TeammateHeading.Text = name;
        var rows = tracker.TeammateHistory(name);
        HistoryScope.Text = $"{rows.Count:N0} shared recorded matches · " + (tracker.CountFrom.HasValue ? $"from {tracker.CountFrom:MMM d, yyyy}" : "all time");
        SearchedTotal.Text = rows.Sum(r => r.Actions.Searched).ToString("N0");
        BrokenTotal.Text = rows.Sum(r => r.Actions.Broken).ToString("N0");
        CombinedTotal.Text = rows.Sum(r => r.Actions.Searched + r.Actions.Broken).ToString("N0");
        TeammateMatches.ItemsSource = rows;
        NoSharedMatches.Visibility = rows.Count == 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }
    void CloseHistory(object sender, Wpf.RoutedEventArgs e) => Close();
}

internal sealed record TeammateHistoryRow(MatchResult Match, TeammateMatch Actions)
{
    public string Date => (Match.MatchStartUtc ?? Match.LastWriteUtc).ToLocalTime().ToString("MMM d, yyyy · h:mm tt");
    public string Searched => Actions.Searched + Actions.Broken == 0 ? "Not recorded" : Actions.Searched.ToString("N0");
    public string Broken => Actions.Searched + Actions.Broken == 0 ? "—" : Actions.Broken.ToString("N0");
    public string Total => Actions.Searched + Actions.Broken == 0 ? "—" : (Actions.Searched + Actions.Broken).ToString("N0");
    public string Duration => $"{(long)TimeSpan.FromMilliseconds(Match.LengthMs).TotalMinutes:N0}m {TimeSpan.FromMilliseconds(Match.LengthMs).Seconds:00}s";
}
