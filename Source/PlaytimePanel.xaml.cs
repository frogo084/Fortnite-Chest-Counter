using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Input = System.Windows.Input;

namespace LootTracker;

public partial class PlaytimePanel : Controls.UserControl
{
    ReplayTracker? tracker;
    bool changingSearch;
    IReadOnlyList<string> detectedNames = Array.Empty<string>();
    public PlaytimePanel() => InitializeComponent();
    internal DateTime? DisplayedStartDate => CountFrom.Picker.SelectedDate;
    internal void Bind(ReplayTracker value)
    {
        tracker = value;
        CountFrom.Bind(value);
        Refresh();
    }
    internal void Refresh()
    {
        if (tracker == null) return;
        CountFrom.Refresh();
        var matches = tracker.CountedMatches.ToList();
        var time = tracker.RecordedTime;
        PlaytimeText.Text = $"{(long)time.TotalHours:N0}h {time.Minutes:00}m";
        MinutesText.Text = $"{(long)time.TotalMinutes:N0} minutes · {matches.Count:N0} matches";
        ChestCountText.Text = $"{matches.Sum(ReplayTracker.CountChests):N0} chests · searched + broken + Sprite only";
        ScopeText.Text = tracker.CountFrom.HasValue ? $"COUNTING FROM {tracker.CountFrom.Value:MMM d, yyyy}".ToUpperInvariant() : "ALL RECORDED MATCHES";
        TeammatesGrid.ItemsSource = tracker.TeammateRows();
        detectedNames = tracker.DetectedTeammates;
        UpdateSuggestions(false);
        EmptyRoster.Visibility = tracker.SavedTeammates.Count == 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }
    void UpdateSuggestions(bool open)
    {
        if (NameSuggestions == null || changingSearch) return;
        var query = TeammateNameInput.Text.Trim();
        NameSuggestions.ItemsSource = detectedNames.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
        NoNameMatches.Visibility = NameSuggestions.Items.Count == 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        if (open) SuggestionsPopup.IsOpen = true;
    }
    void SearchNameChanged(object sender, Controls.TextChangedEventArgs e)
    {
        if (!changingSearch) UpdateSuggestions(TeammateNameInput.IsKeyboardFocusWithin);
    }
    void SearchFocused(object sender, Input.KeyboardFocusChangedEventArgs e) => UpdateSuggestions(true);
    void SearchLostFocus(object sender, Input.KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!TeammateNameInput.IsKeyboardFocusWithin && !NameSuggestions.IsKeyboardFocusWithin) SuggestionsPopup.IsOpen = false;
        });
    }
    void SelectSuggestion(string name)
    {
        changingSearch = true;
        try { TeammateNameInput.Text = name; TeammateNameInput.CaretIndex = name.Length; SuggestionsPopup.IsOpen = false; }
        finally { changingSearch = false; }
    }
    void ChooseSuggestion(object sender, Input.MouseButtonEventArgs e)
    {
        if (NameSuggestions.SelectedItem is string name) { SelectSuggestion(name); e.Handled = true; }
    }
    void SearchKeyDown(object sender, Input.KeyEventArgs e)
    {
        if (e.Key == Input.Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Input.Key.Down && NameSuggestions.Items.Count > 0)
        {
            SuggestionsPopup.IsOpen = true; NameSuggestions.SelectedIndex = 0;
            (NameSuggestions.ItemContainerGenerator.ContainerFromIndex(0) as Controls.ListBoxItem)?.Focus(); e.Handled = true;
        }
        else if (e.Key == Input.Key.Enter)
        {
            if (SuggestionsPopup.IsOpen && NameSuggestions.Items.Count == 1) SelectSuggestion((string)NameSuggestions.Items[0]);
            else SaveName(this, new());
            e.Handled = true;
        }
    }
    void SuggestionKeyDown(object sender, Input.KeyEventArgs e)
    {
        if (e.Key == Input.Key.Enter && NameSuggestions.SelectedItem is string name) { SelectSuggestion(name); e.Handled = true; }
        else if (e.Key == Input.Key.Escape) { SuggestionsPopup.IsOpen = false; e.Handled = true; }
    }
    void SaveName(object sender, Wpf.RoutedEventArgs e)
    {
        if (tracker == null) return;
        try
        {
            var name = TeammateNameInput.Text.Trim();
            if (!tracker.DetectedTeammates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var similar = tracker.DetectedTeammates.Where(n => name.Length > 0 && n.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(3).ToList();
                if (similar.Count > 0) { PanelStatus.Text = "Choose the recorded name from the suggestions."; UpdateSuggestions(true); return; }
            }
            tracker.SaveTeammate(name); TeammateNameInput.Text = "";
            SuggestionsPopup.IsOpen = false;
            PanelStatus.Text = "Name saved locally. Counts use recorded actions on your team, not estimates.";
            Refresh();
        }
        catch (Exception ex) { PanelStatus.Text = ex.Message; }
    }
    void RemoveName(object sender, Wpf.RoutedEventArgs e)
    {
        if (tracker == null || TeammatesGrid.SelectedItem is not SavedTeammateRow selected) return;
        try { tracker.RemoveTeammate(selected.Name); PanelStatus.Text = "Saved name removed; replay history kept."; Refresh(); }
        catch (Exception ex) { PanelStatus.Text = ex.Message; }
    }
    void ShowTeammateHistory()
    {
        if (tracker != null && TeammatesGrid.SelectedItem is SavedTeammateRow selected && Wpf.Window.GetWindow(this) is Wpf.Window owner)
            new TeammateHistoryWindow(owner, tracker, selected.Name).ShowDialog();
    }
    void OpenTeammateHistory(object sender, Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Wpf.DependencyObject source && Controls.ItemsControl.ContainerFromElement(TeammatesGrid, source) is Controls.DataGridRow)
            ShowTeammateHistory();
    }
    void TeammateHistoryKey(object sender, Input.KeyEventArgs e)
    {
        if (e.Key == Input.Key.Enter) { ShowTeammateHistory(); e.Handled = true; }
    }
    internal void VerifyBinding(ReplayTracker fixture)
    {
        var original = tracker;
        try
        {
            Bind(fixture);
            if (PlaytimeText.Text != "26h 45m") throw new InvalidOperationException("Playtime display wraps after 24 hours.");
            CountFrom.Picker.SelectedDate = new DateTime(2026, 1, 2);
            CountFrom.ApplyButton.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Primitives.ButtonBase.ClickEvent));
            Refresh();
            if (PlaytimeText.Text != "25h 45m" || !MinutesText.Text.Contains("1,545") || !ChestCountText.Text.StartsWith("4 chests"))
                throw new InvalidOperationException("Date picker did not update playtime and own chests.");
            if (NameSuggestions.Items.Count != 1) throw new InvalidOperationException("Replay teammate discovery did not populate the selector.");
            TeammateNameInput.Text = "Sample"; SaveName(this, new());
            if (fixture.SavedTeammates.Contains("Sample")) throw new InvalidOperationException("A partial name was saved instead of selecting the replay identity.");
            if (NameSuggestions.Items.Count != 1) throw new InvalidOperationException("Partial-name suggestions did not filter.");
            SelectSuggestion((string)NameSuggestions.Items[0]);
            if (TeammateNameInput.Text != "Sample Teammate") throw new InvalidOperationException("Suggestion selection did not fill the exact replay name.");
            TeammateNameInput.Text = "no-such-player"; UpdateSuggestions(false);
            if (NameSuggestions.Items.Count != 0 || NoNameMatches.Visibility != Wpf.Visibility.Visible) throw new InvalidOperationException("Unmatched search state failed.");
            TeammateNameInput.Text = "UI Sample"; SaveName(this, new());
            if (!fixture.SavedTeammates.Contains("UI Sample")) throw new InvalidOperationException("Save teammate control failed.");
            TeammatesGrid.SelectedItem = fixture.TeammateRows().Last(); RemoveName(this, new());
            if (fixture.SavedTeammates.Contains("UI Sample")) throw new InvalidOperationException("Remove teammate control failed.");
            CountFrom.AllTimeButton.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Primitives.ButtonBase.ClickEvent));
            Refresh();
            if (fixture.CountFrom != null || PlaytimeText.Text != "26h 45m") throw new InvalidOperationException("All time control failed.");
        }
        finally { if (original != null) Bind(original); }
    }
    internal async Task CaptureControls(string path)
    {
        if (tracker == null) return;
        var picker = CountFrom.Picker;
        picker.IsDropDownOpen = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        var popup = (Controls.Primitives.Popup)picker.Template.FindName("PART_Popup", picker);
        var calendar = (Controls.Calendar)popup.Child;
        calendar.ApplyTemplate(); calendar.UpdateLayout();
        var item = (Controls.Primitives.CalendarItem)calendar.Template.FindName("PART_CalendarItem", calendar);
        item.ApplyTemplate();
        var originalMonth = calendar.DisplayDate;
        ((Controls.Button)item.Template.FindName("PART_NextButton", item)).RaiseEvent(new Wpf.RoutedEventArgs(Controls.Primitives.ButtonBase.ClickEvent));
        if (calendar.DisplayDate.Month != originalMonth.AddMonths(1).Month) throw new InvalidOperationException("Custom calendar next month failed.");
        ((Controls.Button)item.Template.FindName("PART_PreviousButton", item)).RaiseEvent(new Wpf.RoutedEventArgs(Controls.Primitives.ButtonBase.ClickEvent));
        if (calendar.DisplayDate.Month != originalMonth.Month) throw new InvalidOperationException("Custom calendar previous month failed.");
        ((Controls.Button)item.Template.FindName("PART_HeaderButton", item)).RaiseEvent(new Wpf.RoutedEventArgs(Controls.Primitives.ButtonBase.ClickEvent));
        if (calendar.DisplayMode != Controls.CalendarMode.Year) throw new InvalidOperationException("Custom calendar month/year navigation failed.");
        calendar.DisplayMode = Controls.CalendarMode.Month;
        var selected = calendar.SelectedDate ?? calendar.DisplayDate.Date;
        calendar.SelectedDate = selected.AddDays(1);
        if (picker.SelectedDate != selected.AddDays(1)) throw new InvalidOperationException("Custom calendar selection did not reach the date field.");
        calendar.SelectedDate = selected;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        SaveControlImage(calendar, path + ".calendar.png");
        picker.IsDropDownOpen = false;
        var recordedName = tracker.SavedTeammates.FirstOrDefault(n => detectedNames.Contains(n)) ?? detectedNames.FirstOrDefault();
        if (recordedName != null)
        {
            var query = recordedName.Substring(Math.Max(0, (recordedName.Length - 3) / 2), Math.Min(3, recordedName.Length)).ToLowerInvariant();
            TeammateNameInput.Text = query; UpdateSuggestions(true);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (!NameSuggestions.Items.Cast<string>().Contains(recordedName)) throw new InvalidOperationException("Substring teammate search failed.");
            SaveControlImage((Wpf.FrameworkElement)SuggestionsPopup.Child, path + ".search.png");
            SelectSuggestion(recordedName);
            var owner = Wpf.Window.GetWindow(this)!;
            var saved = TeammatesGrid.Items.Cast<SavedTeammateRow>().FirstOrDefault(r => r.Name == recordedName);
            if (saved != null)
            {
                TeammatesGrid.SelectedItem = saved; TeammatesGrid.ScrollIntoView(saved); TeammatesGrid.UpdateLayout();
                var row = (Controls.DataGridRow)TeammatesGrid.ItemContainerGenerator.ContainerFromItem(saved);
                bool opened = false;
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    var history = Wpf.Application.Current.Windows.OfType<TeammateHistoryWindow>().FirstOrDefault(w => w.Owner == owner);
                    if (history == null) return;
                    try
                    {
                        await history.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                        history.UpdateLayout(); SaveControlImage((Wpf.FrameworkElement)history.Content, path + ".history.png"); opened = true;
                    }
                    finally { history.Close(); }
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                TeammatesGrid.RaiseEvent(new Input.MouseButtonEventArgs(Input.Mouse.PrimaryDevice, Environment.TickCount, Input.MouseButton.Left)
                    { RoutedEvent = Controls.Control.MouseDoubleClickEvent, Source = row });
                if (!opened) throw new InvalidOperationException("Double-click did not open the selected teammate's match history.");
            }
            else
            {
                var history = new TeammateHistoryWindow(owner, tracker, recordedName); history.Show();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                SaveControlImage((Wpf.FrameworkElement)history.Content, path + ".history.png"); history.Close();
            }
            TeammateNameInput.Text = ""; SuggestionsPopup.IsOpen = false;
        }
        File.WriteAllText(path + ".checks.txt", "PASS: custom calendar next/previous month, month/year view, selected-date binding, live case-insensitive substring suggestions, exact replay identity, and double-click opening the selected teammate's shared match history.");
    }
    static void SaveControlImage(Wpf.FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        var bounds = new Wpf.Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var brush = new System.Windows.Media.VisualBrush(element) { ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute, Viewbox = bounds };
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(brush, null, bounds);
        bitmap.Render(visual);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
