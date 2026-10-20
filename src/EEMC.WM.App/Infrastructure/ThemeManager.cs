using System.Windows;

namespace EEMC.WM.App.Infrastructure;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    /// <summary>Thay bộ màu (từ điển đầu tiên trong App.xaml).</summary>
    public static void Apply(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var theme = new ResourceDictionary { Source = uri };
        if (dicts.Count > 0) dicts[0] = theme;
        else dicts.Insert(0, theme);
        IsDark = dark;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
