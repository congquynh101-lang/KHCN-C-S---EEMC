using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EEMC.WM.App.Infrastructure;

/// <summary>Mở ContextMenu của nút khi bấm chuột trái (nút "Chuyển trạng thái ▾").</summary>
public static class DropDownMenu
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DropDownMenu), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase b) return;
        b.Click -= OnClick;
        if ((bool)e.NewValue) b.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ContextMenu == null) return;
        fe.ContextMenu.DataContext = fe.DataContext;
        fe.ContextMenu.PlacementTarget = fe;
        fe.ContextMenu.Placement = PlacementMode.Bottom;
        fe.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}

/// <summary>Nhấp đúp vào phần tử để chạy lệnh.</summary>
public static class DoubleClick
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DoubleClick), new PropertyMetadata(null, OnChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? v) => d.SetValue(CommandProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.MouseLeftButtonDown -= OnDown;
        if (e.NewValue != null) el.MouseLeftButtonDown += OnDown;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not DependencyObject d) return;
        var cmd = GetCommand(d);
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
            e.Handled = true;
        }
    }
}
