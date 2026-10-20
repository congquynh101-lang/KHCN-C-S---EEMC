using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EEMC.WM.App.Infrastructure;

/// <summary>
/// Đồng bộ thanh tiêu đề Windows với giao diện: chế độ tối/sáng và màu nền (Windows 11),
/// bo góc cửa sổ. Trên Windows 10 các lệnh không hỗ trợ được bỏ qua.
/// </summary>
public static class WindowTheming
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Gắn tự động cho mọi cửa sổ của ứng dụng.</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window w) Apply(w);
        }));
        ThemeManager.ThemeChanged += (_, _) =>
        {
            foreach (Window w in Application.Current.Windows) Apply(w);
        };
    }

    public static void Apply(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = ThemeManager.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int round = 2;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            if (Application.Current.TryFindResource("C.App") is Color bg)
            {
                int caption = bg.R | (bg.G << 8) | (bg.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }
            if (Application.Current.TryFindResource("Fg.Primary") is SolidColorBrush fg)
            {
                int text = fg.Color.R | (fg.Color.G << 8) | (fg.Color.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
        }
        catch { /* Windows cũ không hỗ trợ: giữ thanh tiêu đề mặc định */ }
    }
}
