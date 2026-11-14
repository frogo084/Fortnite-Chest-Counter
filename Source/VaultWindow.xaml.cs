using System.Globalization;
using ChestReplayReader;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Animation = System.Windows.Media.Animation;
using Imaging = System.Windows.Media.Imaging;

public partial class VaultWindow : Wpf.Window
{
    readonly ReplayTracker tracker;
    readonly System.Windows.Threading.DispatcherTimer scanTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly System.Windows.Threading.DispatcherTimer countTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly System.Diagnostics.Stopwatch countClock = new();
    readonly NotifyIcon tray = new();
    readonly string? capturePath;
    readonly string captureMode;
    double shownCount, fromCount;
    int targetCount;
    bool ready;
    List<MatchRow> rows = new();

    public VaultWindow(string? capturePath = null, string captureMode = "--capture") : this(new ReplayTracker(), capturePath, captureMode) { }

    internal VaultWindow(ReplayTracker tracker, string? capturePath = null, string captureMode = "--capture")
    {
        this.tracker = tracker;
        this.capturePath = capturePath;
        this.captureMode = captureMode;
        InitializeComponent();
        PlaytimePage.Bind(tracker);
        Icon = Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app-icon.png"));
        AnimationsToggle.IsChecked = Wpf.SystemParameters.ClientAreaAnimation;
        Width = Math.Min(1240, Wpf.SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(920, Wpf.SystemParameters.WorkArea.Height - 40);
        using var iconData = typeof(VaultProgram).Assembly.GetManifestResourceStream("AppIcon");
        tray.Icon = new System.Drawing.Icon(iconData!);
        tray.Text = "Fortnite Loot Tracker";
        tray.Visible = capturePath == null;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = Wpf.WindowState.Normal; Activate(); });
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show tracker", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); }));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tracker.Changed += UpdateView;
        tracker.NewMatch += r =>
        {
            tray.BalloonTipTitle = $"{r.ContainersAccessed} containers searched + broken";
            tray.BalloonTipText = $"{r.ChestsOpened} searched · {r.ContainersBroken} broken";
            tray.ShowBalloonTip(10000);
        };
        scanTimer.Tick += async (_, _) => await tracker.Scan();
        countTimer.Tick += (_, _) =>
        {
            var t = Math.Min(1, countClock.Elapsed.TotalMilliseconds / 750);
            shownCount = fromCount + (targetCount - fromCount) * (1 - Math.Pow(1 - t, 3));
            TotalText.Text = ((int)Math.Round(shownCount)).ToString("N0");
            if (t >= 1) countTimer.Stop();
        };
        Loaded += async (_, _) =>
        {
            ready = true;
            UpdateView();
            if (capturePath != null)
            {
                IsHitTestVisible = false;
                AnimationsToggle.IsChecked = false; UpdateMotion();
                TotalText.Text = targetCount.ToString("N0");
                if (captureMode is "--capture-squad" or "--capture-controls") SelectPage("playtime");
                else if (captureMode == "--capture-history") SelectPage("history");
                else if (captureMode == "--capture-personal")
                {
                    FillReplayOdds(); OddsPage.SetMode(true); SelectPage("odds");
                }
                else if (captureMode != "--capture")
                {
                    OddsPage.ConfigureCapture(captureMode == "--capture-past");
                    SelectPage("odds");
                }
                if (captureMode == "--capture-controls") await PlaytimePage.CaptureControls(capturePath);
                await Dispatcher.InvokeAsync(Capture, System.Windows.Threading.DispatcherPriority.ContextIdle);
                return;
            }
            UpdateMotion(); scanTimer.Start(); await tracker.Scan();
        };
        Closed += (_, _) => { tracker.Stopped = true; scanTimer.Stop(); countTimer.Stop(); tray.Dispose(); };
    }

    bool Motion => AnimationsToggle.IsChecked == true && Wpf.SystemParameters.ClientAreaAnimation;

    void UpdateView()
    {
        if (!ready) return;
        var good = tracker.CountedMatches.OrderByDescending(r => r.MatchStartUtc ?? r.LastWriteUtc).ToList();
        FillReplayOdds();
        PlaytimePage.Refresh();
        var total = good.Sum(r => r.ContainersAccessed);
        if (total != targetCount || TotalText.Text == "0")
        {
            targetCount = total;
            var measure = new Media.FormattedText(total.ToString("N0"), CultureInfo.CurrentCulture, Wpf.FlowDirection.LeftToRight,
                new Media.Typeface(TotalText.FontFamily, TotalText.FontStyle, TotalText.FontWeight, TotalText.FontStretch), 240, Media.Brushes.White, Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
            TotalText.FontSize = Math.Min(240, 240 * 730 / Math.Max(1, measure.Width));
            if (Motion && capturePath == null) { fromCount = shownCount; countClock.Restart(); countTimer.Start(); }
            else { shownCount = total; TotalText.Text = total.ToString("N0"); }
        }
        ChestsText.Text = good.Sum(r => ReplayTracker.Count(r, "Chests")).ToString("N0");
        SpritesText.Text = good.Sum(r => ReplayTracker.Count(r, "Sprite")).ToString("N0");
        AmmoText.Text = good.Sum(r => ReplayTracker.Count(r, "Ammo")).ToString("N0");
        OtherText.Text = (total - good.Sum(r => ReplayTracker.Count(r, "Chests") + ReplayTracker.Count(r, "Sprite") + ReplayTracker.Count(r, "Ammo"))).ToString("N0");
        MatchesText.Text = tracker.CountFrom.HasValue ? $"{good.Count:N0} MATCHES · SINCE {tracker.CountFrom.Value:MMM d, yyyy}".ToUpperInvariant() : $"{good.Count:N0} MATCHES TRACKED";
        var latest = good.FirstOrDefault();
        LatestTotal.Text = latest?.ContainersAccessed.ToString() ?? "—";
        LatestBreakdown.Text = latest == null ? "Play a match to get started" : $"{latest.ChestsOpened} searched + {latest.ContainersBroken} broken";
        LatestDate.Text = latest == null ? "" : (latest.MatchStartUtc ?? latest.LastWriteUtc).ToLocalTime().ToString("MMM d, h:mm tt");
        ViewMatchButton.IsEnabled = latest != null;
        StatusText.Text = tracker.Status;
        FolderText.Text = tracker.Folder;
        SettingsStatus.Text = tracker.Status;
        ChooseFolderButton.IsEnabled = ScanButton.IsEnabled = !tracker.Busy;
        var selected = (MatchesGrid.SelectedItem as MatchRow)?.Result.FilePath;
        rows = tracker.VisibleHistory.OrderByDescending(r => r.MatchStartUtc ?? r.LastWriteUtc).Select(r => new MatchRow(r)).ToList();
        MatchesGrid.ItemsSource = rows;
        MatchesGrid.SelectedItem = rows.FirstOrDefault(r => r.Result.FilePath == selected) ?? rows.FirstOrDefault();
    }
    void SelectPage(string page)
    {
        OverviewPage.Visibility = page == "overview" ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        HistoryPage.Visibility = page == "history" ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        OddsPage.Visibility = page == "odds" ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        PlaytimePage.Visibility = page == "playtime" ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        OverviewTab.Tag = page == "overview" ? "active" : null;
        HistoryTab.Tag = page == "history" ? "active" : null;
        OddsTab.Tag = page == "odds" ? "active" : null;
        PlaytimeTab.Tag = page == "playtime" ? "active" : null;
    }
    void ShowOverview(object sender, Wpf.RoutedEventArgs e) => SelectPage("overview");
    void ShowHistory(object sender, Wpf.RoutedEventArgs e) => SelectPage("history");
    void ShowSettings(object sender, Wpf.RoutedEventArgs e) => SelectPage("settings");
    void ShowPlaytime(object sender, Wpf.RoutedEventArgs e) { PlaytimePage.Refresh(); SelectPage("playtime"); }
    void ShowOdds(object sender, Wpf.RoutedEventArgs e)
    {
        FillReplayOdds();
        SelectPage("odds");
    }
    void ViewLatest(object sender, Wpf.RoutedEventArgs e)
    {
        SelectPage("history");
        MatchesGrid.SelectedItem = rows.FirstOrDefault(r => ReplayTracker.Valid(r.Result));
        if (MatchesGrid.SelectedItem != null) MatchesGrid.ScrollIntoView(MatchesGrid.SelectedItem);
    }
    void MatchSelected(object sender, Controls.SelectionChangedEventArgs e)
    {
        if (DetailsGrid == null) return;
        if (MatchesGrid.SelectedItem is not MatchRow row) { DetailsGrid.ItemsSource = null; return; }
        var r = row.Result;
        DetailHeading.Text = ReplayTracker.Valid(r) ? $"{row.Date} · {r.ChestsOpened} searched · {row.Broken} broken" : r.Error ?? "Counts unavailable: recording player not found.";
        DetailsGrid.ItemsSource = ReplayTracker.Valid(r)
            ? r.MyChests.Select(c => (Item: c, Method: "Searched")).Concat(r.MyBrokenContainers.Select(c => (Item: c, Method: "Broken")))
                .GroupBy(c => (c.Item.ChestClass, c.Method)).OrderByDescending(g => g.Count())
                .Select(g => new { Kind = ReplayTracker.Kind(g.Key.ChestClass), Count = g.Count(), Method = g.Key.Method, Blueprint = g.Key.ChestClass }).ToList()
            : null;
    }
    async void ChooseFolder(object sender, Wpf.RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose your Fortnite replay folder", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(tracker.Folder) ? tracker.Folder : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try { tracker.ChooseFolder(dialog.SelectedPath); await tracker.Scan(); }
        catch (Exception ex) { SettingsStatus.Text = "Could not use that folder: " + ex.Message; }
    }
    async void ScanNow(object sender, Wpf.RoutedEventArgs e) => await tracker.Scan();
    void ExportHistory(object sender, Wpf.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV files|*.csv", FileName = "Fortnite-container-counts.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { tracker.Export(dialog.FileName); DetailHeading.Text = "Match breakdowns exported."; }
        catch (Exception ex) { Wpf.MessageBox.Show(this, ex.Message, "Export failed"); }
    }
    void TopmostChanged(object sender, Wpf.RoutedEventArgs e) => Topmost = TopmostToggle.IsChecked == true;
    void AnimationsChanged(object sender, Wpf.RoutedEventArgs e) => UpdateMotion();
    void UpdateMotion()
    {
        if (Motion && capturePath == null)
        {
            ChestMotion.BeginAnimation(Media.TranslateTransform.YProperty, new Animation.DoubleAnimation(0, -6, TimeSpan.FromSeconds(3.5)) { AutoReverse = true, RepeatBehavior = Animation.RepeatBehavior.Forever, EasingFunction = new Animation.SineEase() });
            WatchLight.BeginAnimation(OpacityProperty, new Animation.DoubleAnimation(.5, 1, TimeSpan.FromSeconds(1.5)) { AutoReverse = true, RepeatBehavior = Animation.RepeatBehavior.Forever });
        }
        else
        {
            ChestMotion.BeginAnimation(Media.TranslateTransform.YProperty, null);
            WatchLight.BeginAnimation(OpacityProperty, null);
            countTimer.Stop(); shownCount = targetCount; TotalText.Text = targetCount.ToString("N0");
        }
    }
    void FillReplayOdds()
    {
        var good = tracker.CountedMatches.ToList();
        // Requested model: include every personal searched/broken chest, including Sprite chests.
        var count = good.Sum(ReplayTracker.CountChests);
        OddsPage.SetReplayCounts(count, good.Count == 0 ? 25 : (double)count / good.Count, good.Count);
    }
    void Capture()
    {
        UpdateLayout();
        var root = (Wpf.FrameworkElement)((Controls.Viewbox)Content).Child;
        var bitmap = new Imaging.RenderTargetBitmap(1440, 1024, 96, 96, Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new Imaging.PngBitmapEncoder(); png.Frames.Add(Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(capturePath!)) png.Save(stream);
        Close();
    }
    internal void VerifyReplayPanel() => OddsPage.VerifyReplayBinding();
    internal void VerifyTrackingPanel(ReplayTracker fixture) => PlaytimePage.VerifyBinding(fixture);
    internal sealed class MatchRow
    {
        public MatchResult Result { get; }
        public string Date => (Result.MatchStartUtc ?? Result.LastWriteUtc).ToLocalTime().ToString("MMM d, h:mm tt");
        public string Total => ReplayTracker.Valid(Result) ? Result.ContainersAccessed.ToString() : "—";
        public string Searched => ReplayTracker.Valid(Result) ? Result.ChestsOpened.ToString() : "—";
        public string Broken => !ReplayTracker.Valid(Result) || Result.ParserVersion < 3 ? "—" : Result.ContainersBroken.ToString();
        public string Chests => ReplayTracker.Valid(Result) ? ReplayTracker.Count(Result, "Chests").ToString() : "—";
        public string Sprite => ReplayTracker.Valid(Result) ? ReplayTracker.Count(Result, "Sprite").ToString() : "—";
        public string Ammo => ReplayTracker.Valid(Result) ? ReplayTracker.Count(Result, "Ammo").ToString() : "—";
        public string State => ReplayTracker.Valid(Result) ? Result.ParserVersion < 3 ? "Search only" : "Ready" : !Result.Complete ? "Unsupported / partial" : "Player not found";
        public MatchRow(MatchResult result) => Result = result;
    }
}
