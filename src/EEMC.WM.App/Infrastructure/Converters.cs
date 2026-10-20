using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EEMC.WM.App.Infrastructure;

/// <summary>"Red" → Sem.Red, "RedSoft" → Sem.RedSoft... theo bộ màu hiện hành.</summary>
public sealed class SemanticBrushConverter : IValueConverter
{
    public string Suffix { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = "Sem." + (value?.ToString() ?? "Gray") + (parameter as string ?? Suffix);
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool x => x,
            int i => i != 0,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : false;
}

/// <summary>So sánh giá trị với tham số (dùng cho RadioButton gắn với enum).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return t.IsEnum ? Enum.Parse(t, parameter.ToString()!) : parameter;
        }
        return Binding.DoNothing;
    }
}

/// <summary>Chiều rộng thanh biểu đồ: value/max * MaxWidth.</summary>
public sealed class BarWidthConverter : IMultiValueConverter
{
    public double MaxWidth { get; set; } = 220;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double v || values[1] is not double max || max <= 0) return 0.0;
        var w = parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : MaxWidth;
        return Math.Max(v > 0 ? 3 : 0, v / max * w);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
