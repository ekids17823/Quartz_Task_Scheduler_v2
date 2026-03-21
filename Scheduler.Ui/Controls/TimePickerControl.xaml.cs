using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scheduler.Ui.Controls;

public partial class TimePickerControl : UserControl
{
    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register("Time", typeof(string), typeof(TimePickerControl), 
            new FrameworkPropertyMetadata("00:00:00", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTimeChanged));

    public string Time
    {
        get { return (string)GetValue(TimeProperty); }
        set { SetValue(TimeProperty, value); }
    }

    private bool _isUpdatingInternally;

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePickerControl control && !control._isUpdatingInternally)
        {
            if (DateTime.TryParse(e.NewValue as string, out var dt))
            {
                control.TimeTextBox.Text = dt.ToString("tt hh:mm:ss");
            }
        }
    }

    public TimePickerControl()
    {
        InitializeComponent();
    }

    private void Increment(int step)
    {
        if (!DateTime.TryParse(TimeTextBox.Text, out var dt)) 
        {
            if (DateTime.TryParse(Time, out var fallback)) dt = fallback;
            else dt = DateTime.Today;
        }
        
        int caret = TimeTextBox.CaretIndex;
        string text = TimeTextBox.Text;
        int firstColon = text.IndexOf(':');
        int lastColon = text.LastIndexOf(':');

        if (firstColon != -1)
        {
            if (caret <= firstColon) dt = dt.AddHours(step);
            else if (caret > firstColon && caret <= lastColon) dt = dt.AddMinutes(step);
            else dt = dt.AddSeconds(step);
        }

        _isUpdatingInternally = true;
        TimeTextBox.Text = dt.ToString("tt hh:mm:ss");
        Time = dt.ToString("HH:mm:ss");
        _isUpdatingInternally = false;
        
        TimeTextBox.CaretIndex = Math.Min(caret, TimeTextBox.Text.Length);
    }

    private void UpButton_Click(object sender, RoutedEventArgs e) => Increment(1);
    private void DownButton_Click(object sender, RoutedEventArgs e) => Increment(-1);

    private void TimeTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Increment(1); e.Handled = true; }
        else if (e.Key == Key.Down) { Increment(-1); e.Handled = true; }
    }

    private void TimeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DateTime.TryParse(TimeTextBox.Text, out var dt))
        {
            _isUpdatingInternally = true;
            TimeTextBox.Text = dt.ToString("tt hh:mm:ss");
            Time = dt.ToString("HH:mm:ss");
            _isUpdatingInternally = false;
        }
        else
        {
            if (DateTime.TryParse(Time, out var oldDt)) 
                TimeTextBox.Text = oldDt.ToString("tt hh:mm:ss");
        }
    }
}
