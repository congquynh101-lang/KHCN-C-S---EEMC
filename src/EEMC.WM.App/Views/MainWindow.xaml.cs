using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.App.ViewModels;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.Views;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly TrayService _tray;
    private bool _exiting;
    private bool _trayHintShown;

    public MainWindow(MainViewModel vm, SettingsService settings, TrayService tray)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;
        _tray = tray;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); }));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Find, Key.F, ModifierKeys.Control));
    }

    /// <summary>Đóng cửa sổ: thu nhỏ xuống khay để tiếp tục nhắc việc (nếu bật trong Cài đặt).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.Notify("EEMC KHCN Work Manager vẫn đang chạy",
                    "Phần mềm tiếp tục nhắc việc ở khay hệ thống. Nhấp đúp biểu tượng để mở lại, chọn Thoát để đóng hẳn.", false);
            }
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ExitApplication()
    {
        _exiting = true;
        Close();
    }
}
