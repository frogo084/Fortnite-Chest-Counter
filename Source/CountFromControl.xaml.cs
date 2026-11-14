using System.Globalization;
using Controls = System.Windows.Controls;
using Wpf = System.Windows;

namespace LootTracker;

public partial class CountFromControl : Controls.UserControl
{
    ReplayTracker? tracker;
    DateTime? savedDate;

    public CountFromControl()
    {
        InitializeComponent();
        Language = Wpf.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
    }

    internal Controls.DatePicker Picker => CountFromPicker;

    internal void Bind(ReplayTracker value)
    {
        tracker = value;
        savedDate = value.CountFrom;
        CountFromPicker.SelectedDate = savedDate;
        CountFromPicker.Text = savedDate?.ToString("d", CultureInfo.CurrentCulture) ?? "";
        DateStatus.Text = "";
    }

    internal void Refresh()
    {
        if (tracker != null && savedDate != tracker.CountFrom)
            Bind(tracker);
    }

    void ApplyDate(object sender, Wpf.RoutedEventArgs e)
    {
        if (!DateTime.TryParse(CountFromPicker.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
        {
            DateStatus.Text = "Choose a valid date, or select All time.";
            return;
        }

        SaveDate(date.Date);
    }

    void ClearDate(object sender, Wpf.RoutedEventArgs e) => SaveDate(null);

    void SaveDate(DateTime? date)
    {
        if (tracker == null) return;
        try
        {
            tracker.SetCountFrom(date);
            Bind(tracker);
            DateStatus.Text = date.HasValue ? "Start date saved." : "Showing all recorded matches.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DateStatus.Text = "Could not save the start date.";
        }
    }

    void InvalidDate(object? sender, Controls.DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        DateStatus.Text = "Choose a valid date, or select All time.";
    }
}
