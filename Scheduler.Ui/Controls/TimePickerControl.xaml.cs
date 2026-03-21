using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;

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
    private TextBox? _focusedTextBox;

    public TimePickerControl()
    {
        InitializeComponent();
        _focusedTextBox = HourText;
        UpdateUIFromTime(Time);
    }

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimePickerControl control && !control._isUpdatingInternally)
        {
            control.UpdateUIFromTime(e.NewValue as string);
        }
    }

    private void UpdateUIFromTime(string? timeString)
    {
        if (DateTime.TryParse(timeString, out var dt))
        {
            AmPmText.Text = dt.Hour < 12 ? "上午" : "下午";
            int h = dt.Hour % 12;
            if (h == 0) h = 12;
            HourText.Text = h.ToString("D2");
            MinuteText.Text = dt.Minute.ToString("D2");
            SecondText.Text = dt.Second.ToString("D2");
        }
    }

    private void UpdateTimeFromUI()
    {
        if (_isUpdatingInternally) return;

        if (int.TryParse(HourText.Text, out int h) &&
            int.TryParse(MinuteText.Text, out int m) &&
            int.TryParse(SecondText.Text, out int s))
        {
            bool isPm = AmPmText.Text == "下午";
            if (h == 12) h = 0;
            if (isPm) h += 12;

            h = Math.Max(0, Math.Min(23, h));
            m = Math.Max(0, Math.Min(59, m));
            s = Math.Max(0, Math.Min(59, s));

            _isUpdatingInternally = true;
            Time = $"{h:D2}:{m:D2}:{s:D2}";
            _isUpdatingInternally = false;
        }
    }

    private void NumberValidation(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    private void TimeSegment_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _focusedTextBox = tb;
            tb.SelectAll();
        }
    }
    
    private void TimeSegment_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            if (int.TryParse(tb.Text, out int val))
            {
                if (tb == HourText) val = Math.Max(1, Math.Min(12, val));
                else val = Math.Max(0, Math.Min(59, val));
                tb.Text = val.ToString("D2");
            }
            else tb.Text = tb == HourText ? "12" : "00";
            UpdateTimeFromUI();
        }
    }

    private void AmPmText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        AmPmText.Text = AmPmText.Text == "上午" ? "下午" : "上午";
        UpdateTimeFromUI();
    }

    private void Increment(int step)
    {
        if (_focusedTextBox == null) _focusedTextBox = HourText;
        
        if (int.TryParse(_focusedTextBox.Text, out int val))
        {
            val += step;
            if (_focusedTextBox == HourText)
            {
                if (val > 12) val = 1;
                else if (val < 1) val = 12;
            }
            else
            {
                if (val > 59) val = 0;
                else if (val < 0) val = 59;
            }
            _focusedTextBox.Text = val.ToString("D2");
            UpdateTimeFromUI();
            _focusedTextBox.SelectAll();
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e) => Increment(1);
    private void DownButton_Click(object sender, RoutedEventArgs e) => Increment(-1);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Increment(1); e.Handled = true; }
        else if (e.Key == Key.Down) { Increment(-1); e.Handled = true; }
    }
}
