using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace EEMC.WM.App.Controls;

/// <summary>Ô nhập ngày (dd/MM/yyyy) + giờ (HH:mm). Value = null khi chưa chọn ngày.</summary>
public sealed class DateTimeInput : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(DateTime?), typeof(DateTimeInput),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty DefaultTimeProperty = DependencyProperty.Register(
        nameof(DefaultTime), typeof(TimeSpan), typeof(DateTimeInput), new PropertyMetadata(new TimeSpan(17, 0, 0)));

    private readonly DatePicker _date;
    private readonly TextBox _time;
    private bool _updating;

    public DateTimeInput()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        _date = new DatePicker { SelectedDateFormat = DatePickerFormat.Short, VerticalContentAlignment = VerticalAlignment.Center };
        _date.SelectedDateChanged += (_, _) => Push();
        _time = new TextBox { Text = "", MaxLength = 5, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Giờ (HH:mm)", HorizontalContentAlignment = HorizontalAlignment.Center };
        _time.LostKeyboardFocus += (_, _) => { Push(); Render(); };
        _time.KeyDown += (_, e) => { if (e.Key == Key.Enter) Push(); };
        _time.TextChanged += (_, _) => { if (!_updating && TryParseTime(_time.Text, out _)) Push(); };

        Grid.SetColumn(_date, 0);
        Grid.SetColumn(_time, 2);
        grid.Children.Add(_date);
        grid.Children.Add(_time);
        Content = grid;
        // Hỗ trợ AutomationProperties / focus vào ô ngày
        Focusable = false;
    }

    public DateTime? Value
    {
        get => (DateTime?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public TimeSpan DefaultTime
    {
        get => (TimeSpan)GetValue(DefaultTimeProperty);
        set => SetValue(DefaultTimeProperty, value);
    }

    public new void Focus() => _date.Focus();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DateTimeInput)d).Render();

    private void Render()
    {
        _updating = true;
        try
        {
            var v = Value;
            _date.SelectedDate = v?.Date;
            _time.Text = v.HasValue ? v.Value.ToString("HH:mm") : "";
        }
        finally { _updating = false; }
    }

    private void Push()
    {
        if (_updating) return;
        var date = _date.SelectedDate;
        if (date == null)
        {
            SetCurrentValue(ValueProperty, null);
            return;
        }
        TimeSpan time;
        if (string.IsNullOrWhiteSpace(_time.Text)) time = Value?.TimeOfDay ?? DefaultTime;
        else if (!TryParseTime(_time.Text, out time)) return; // giữ giá trị cũ khi giờ chưa hợp lệ
        var newValue = date.Value.Date + time;
        if (Value != newValue)
        {
            _updating = true;
            try { SetCurrentValue(ValueProperty, newValue); }
            finally { _updating = false; }
        }
        BindingOperations.GetBindingExpression(this, ValueProperty)?.UpdateSource();
    }

    /// <summary>Chấp nhận "8", "08", "8:30", "0830", "17h", "17h30".</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToLowerInvariant().Replace('h', ':').Replace('.', ':');
        if (s.EndsWith(':')) s = s.TrimEnd(':');
        int h, m = 0;
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out h)) return false;
            if (parts[1].Length > 0 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m)) return false;
        }
        else if (s.Length <= 2) { if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out h)) return false; }
        else if (s.Length is 3 or 4)
        {
            if (!int.TryParse(s[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out h) ||
                !int.TryParse(s[^2..], NumberStyles.None, CultureInfo.InvariantCulture, out m)) return false;
        }
        else return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }
}
