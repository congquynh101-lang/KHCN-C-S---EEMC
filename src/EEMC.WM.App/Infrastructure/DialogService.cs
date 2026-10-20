using System.Windows;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.Win32;

namespace EEMC.WM.App.Infrastructure;

/// <summary>Hộp thoại dùng chung (tách khỏi ViewModel để dễ thay thế/kiểm thử).</summary>
public sealed class DialogService
{
    private const string Caption = "EEMC KHCN Work Manager";

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public void Info(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Warn(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Error(string message, Exception? ex = null)
    {
        if (ex != null && ex is not ValidationException) AppLog.Error(ex, message);
        var detail = ex switch
        {
            null => "",
            ValidationException v => Environment.NewLine + string.Join(Environment.NewLine, v.Errors.Select(e => "• " + e)),
            InvalidOperationException io => Environment.NewLine + io.Message,
            _ => Environment.NewLine + "Chi tiết đã được ghi vào nhật ký lỗi."
        };
        Show(message + detail, MessageBoxButton.OK, ex is ValidationException ? MessageBoxImage.Warning : MessageBoxImage.Error);
    }

    public bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public MessageBoxResult YesNoCancel(string message) =>
        Show(message, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner != null && owner.IsVisible
            ? MessageBox.Show(owner, message, Caption, buttons, image)
            : MessageBox.Show(message, Caption, buttons, image);
    }

    public string? SaveFile(string filter, string fileName)
    {
        var d = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true, OverwritePrompt = true };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? OpenFile(string filter, string? initialDir = null)
    {
        var d = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(initialDir) && System.IO.Directory.Exists(initialDir)) d.InitialDirectory = initialDir;
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? PickFolder(string? initial = null)
    {
        var d = new OpenFolderDialog { Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(Owner) == true ? d.FolderName : null;
    }

    /// <summary>Mở tệp/thư mục/đường dẫn mạng bằng ứng dụng mặc định của Windows (không chỉnh sửa tài liệu).</summary>
    public void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path.Trim()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error($"Không mở được: {path}", new InvalidOperationException(ex.Message));
        }
    }
}
