using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Controls;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed partial class PeriodRow : ObservableObject
{
    [ObservableProperty] private string _startText = "08:00";
    [ObservableProperty] private string _endText = "12:00";
}

public sealed partial class DayRow : ObservableObject
{
    public required DayOfWeek Day { get; init; }
    public string Label => Labels.DayOfWeekVi(Day);
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class ReminderRow : ObservableObject
{
    public required int Minutes { get; init; }
    public string Label => new ReminderOffset(Minutes).Label;
    [ObservableProperty] private bool _enabled;
}

public sealed class CategoryRow
{
    public required int Id { get; init; }
    public int? ParentId { get; init; }
    public required string Name { get; init; }
    public bool IsGroup => ParentId == null;
    public string Display => IsGroup ? Name : "      " + Name;
}

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly BackupService _backup;
    private readonly CategoryService _categories;
    private readonly DialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly SyncHost _sync;

    public SettingsViewModel(SettingsService settings, BackupService backup, CategoryService categories, DialogService dialogs, AppPaths paths, SyncHost sync)
    {
        _settings = settings;
        _backup = backup;
        _categories = categories;
        _dialogs = dialogs;
        _paths = paths;
        _sync = sync;
        sync.Service.StatusChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SyncStatusText = _sync.Service.LastResult.Message;
            LoadDevices();
        });
    }

    // ------------------------------------------------ Đồng bộ
    public ObservableCollection<SyncDeviceInfo> Devices { get; } = new();
    [ObservableProperty] private bool _syncEnabled;
    [ObservableProperty] private string _syncFolder = "";
    [ObservableProperty] private string _syncIntervalMinutes = "2";
    [ObservableProperty] private string _syncStatusText = "";
    public string ThisDeviceText => $"Máy này: {_sync.Service.DeviceName} (mã {_sync.Service.DeviceId[..8]})";
    public string? SuggestedSyncFolder => SyncService.SuggestFolder();

    private void LoadDevices()
    {
        Devices.Clear();
        try { foreach (var d in _sync.Service.GetDevices()) Devices.Add(d); }
        catch (Exception ex) { AppLog.Error(ex, "Đọc danh sách máy đồng bộ"); }
    }

    [RelayCommand]
    private void UseOneDrive()
    {
        var f = SyncService.SuggestFolder();
        if (f == null) { _dialogs.Warn("Không tìm thấy thư mục OneDrive trên máy này. Hãy đăng nhập OneDrive của EEMC hoặc chọn thư mục thủ công."); return; }
        SyncFolder = f;
    }

    [RelayCommand]
    private void BrowseSyncFolder()
    {
        var f = _dialogs.PickFolder(string.IsNullOrWhiteSpace(SyncFolder) ? SyncService.SuggestFolder() : SyncFolder);
        if (f != null) SyncFolder = f;
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        if (!_settings.Current.SyncEnabled || _settings.Current.SyncFolder != SyncFolder.Trim())
        {
            _dialogs.Info("Hãy bật đồng bộ, chọn thư mục rồi bấm \"Lưu cài đặt\" trước.");
            return;
        }
        var r = await _sync.RunAsync();
        SyncStatusText = r.Message;
        LoadDevices();
    }

    public override string Title => "Cài đặt";

    public ObservableCollection<PeriodRow> Periods { get; } = new();
    public ObservableCollection<DayRow> Days { get; } = new();
    public ObservableCollection<DateTime> Holidays { get; } = new();
    public ObservableCollection<ReminderRow> Reminders { get; } = new();
    public ObservableCollection<BackupInfo> Backups { get; } = new();
    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();

    [ObservableProperty] private DateTime? _newHoliday;
    [ObservableProperty] private bool _remindWhenOverdue;
    [ObservableProperty] private string _overdueRepeatHours = "24";
    [ObservableProperty] private bool _remindFollowUp;
    [ObservableProperty] private bool _windowsNotification;
    [ObservableProperty] private string _defaultEstimateHours = "2";
    [ObservableProperty] private string _dueSoonDays = "3";
    [ObservableProperty] private bool _darkTheme;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private string _backupFolder = "";
    [ObservableProperty] private bool _autoBackupDaily;
    [ObservableProperty] private string _backupKeepCount = "30";
    [ObservableProperty] private BackupInfo? _selectedBackup;
    [ObservableProperty] private CategoryRow? _selectedCategory;
    [ObservableProperty] private string _categoryName = "";
    [ObservableProperty] private string _statusText = "";

    public string DataFolder => _paths.DataDir;
    public string DatabaseFile => _paths.DatabasePath;

    partial void OnSelectedCategoryChanged(CategoryRow? value)
    {
        if (value != null) CategoryName = value.Name;
    }

    /// <summary>Gọi lại sau khi khôi phục danh mục/thay đổi để màn hình khác cập nhật.</summary>
    public event EventHandler? CategoriesChanged;

    public override async Task RefreshAsync()
    {
        var s = _settings.Current;
        Periods.Clear();
        foreach (var p in s.WorkPeriods.OrderBy(p => p.Start))
            Periods.Add(new PeriodRow { StartText = Fmt(p.Start), EndText = Fmt(p.End) });

        Days.Clear();
        foreach (var d in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            Days.Add(new DayRow { Day = d, IsChecked = s.WorkDays.Contains(d) });

        Holidays.Clear();
        foreach (var h in s.Holidays.Select(h => h.Date).Distinct().OrderBy(h => h)) Holidays.Add(h);

        Reminders.Clear();
        var defaults = new[] { 3 * 1440, 1440, 240, 60, 0 };
        foreach (var m in defaults.Union(s.ReminderOffsets.Select(o => o.Minutes)).Distinct().OrderByDescending(m => m))
            Reminders.Add(new ReminderRow { Minutes = m, Enabled = s.ReminderOffsets.Any(o => o.Minutes == m && o.Enabled) });

        RemindWhenOverdue = s.RemindWhenOverdue;
        OverdueRepeatHours = s.OverdueRepeatHours.ToString();
        RemindFollowUp = s.RemindFollowUp;
        WindowsNotification = s.WindowsNotification;
        DefaultEstimateHours = s.DefaultEstimateHours.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN"));
        DueSoonDays = s.DueSoonDays.ToString();
        DarkTheme = s.DarkTheme;
        MinimizeToTray = s.MinimizeToTray;
        BackupFolder = _backup.BackupFolder;
        AutoBackupDaily = s.AutoBackupDaily;
        BackupKeepCount = s.BackupKeepCount.ToString();
        SyncEnabled = s.SyncEnabled;
        SyncFolder = s.SyncFolder ?? SyncService.SuggestFolder() ?? "";
        SyncIntervalMinutes = s.SyncIntervalMinutes.ToString();
        SyncStatusText = _sync.Service.LastResult.Message;
        LoadDevices();

        LoadBackups();
        await LoadCategoriesAsync();
        StatusText = "";
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    private void LoadBackups()
    {
        Backups.Clear();
        foreach (var b in _backup.ListBackups()) Backups.Add(b);
    }

    private async Task LoadCategoriesAsync()
    {
        CategoryRows.Clear();
        foreach (var c in await _categories.GetAllAsync())
            CategoryRows.Add(new CategoryRow { Id = c.Id, ParentId = c.ParentId, Name = c.Name });
    }

    // ------------------------------------------------ Giờ làm việc / ngày nghỉ
    [RelayCommand] private void AddPeriod() => Periods.Add(new PeriodRow { StartText = "13:00", EndText = "17:00" });
    [RelayCommand] private void RemovePeriod(PeriodRow? row) { if (row != null) Periods.Remove(row); }

    [RelayCommand]
    private void AddHoliday()
    {
        if (NewHoliday is not { } d) return;
        if (!Holidays.Contains(d.Date))
        {
            var idx = 0;
            while (idx < Holidays.Count && Holidays[idx] < d.Date) idx++;
            Holidays.Insert(idx, d.Date);
        }
        NewHoliday = null;
    }

    [RelayCommand] private void RemoveHoliday(DateTime? d) { if (d.HasValue) Holidays.Remove(d.Value); }

    // ------------------------------------------------ Lưu
    [RelayCommand]
    private async Task Save()
    {
        var errors = new List<string>();
        var s = _settings.CloneCurrent();

        s.WorkPeriods = new List<WorkPeriod>();
        foreach (var p in Periods)
        {
            if (DateTimeInput.TryParseTime(p.StartText, out var a) && DateTimeInput.TryParseTime(p.EndText, out var b))
                s.WorkPeriods.Add(new WorkPeriod(a, b));
            else errors.Add($"Giờ làm việc \"{p.StartText}–{p.EndText}\" không đúng định dạng HH:mm.");
        }
        s.WorkDays = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();
        s.Holidays = Holidays.ToList();
        s.ReminderOffsets = Reminders.Select(r => new ReminderOffset(r.Minutes, r.Enabled)).ToList();
        s.RemindWhenOverdue = RemindWhenOverdue;
        s.RemindFollowUp = RemindFollowUp;
        s.WindowsNotification = WindowsNotification;
        s.DarkTheme = DarkTheme;
        s.MinimizeToTray = MinimizeToTray;
        s.AutoBackupDaily = AutoBackupDaily;
        s.BackupFolder = string.IsNullOrWhiteSpace(BackupFolder) || BackupFolder == _paths.DefaultBackupDir ? null : BackupFolder.Trim();

        if (int.TryParse(OverdueRepeatHours, out var orh)) s.OverdueRepeatHours = orh; else errors.Add("Chu kỳ nhắc quá hạn phải là số giờ.");
        if (double.TryParse(DefaultEstimateHours.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var est)) s.DefaultEstimateHours = est;
        else errors.Add("Thời lượng ước tính mặc định phải là số.");
        if (int.TryParse(DueSoonDays, out var ds) && ds is >= 1 and <= 30) s.DueSoonDays = ds; else errors.Add("Số ngày \"sắp đến hạn\" phải từ 1 đến 30.");
        if (int.TryParse(BackupKeepCount, out var bk)) s.BackupKeepCount = bk; else errors.Add("Số bản sao lưu giữ lại phải là số.");

        s.SyncEnabled = SyncEnabled;
        s.SyncFolder = string.IsNullOrWhiteSpace(SyncFolder) ? null : SyncFolder.Trim();
        if (int.TryParse(SyncIntervalMinutes, out var si) && si is >= 1 and <= 60) s.SyncIntervalMinutes = si;
        else errors.Add("Chu kỳ đồng bộ phải từ 1 đến 60 phút.");
        if (s.SyncEnabled && s.SyncFolder == null) errors.Add("Hãy chọn thư mục đồng bộ (ví dụ thư mục OneDrive của EEMC).");
        if (s.SyncEnabled && s.SyncFolder != null)
        {
            try { System.IO.Directory.CreateDirectory(s.SyncFolder); }
            catch (Exception ex) { errors.Add($"Không tạo/truy cập được thư mục đồng bộ: {ex.Message}"); }
            if (s.SyncFolder.StartsWith(_paths.DataDir, StringComparison.OrdinalIgnoreCase))
                errors.Add("Thư mục đồng bộ không được nằm trong thư mục dữ liệu của phần mềm.");
        }
        bool syncJustEnabled = s.SyncEnabled && (!_settings.Current.SyncEnabled || _settings.Current.SyncFolder != s.SyncFolder);

        errors.AddRange(s.Validate());
        if (errors.Count > 0)
        {
            _dialogs.Warn("Chưa lưu được cài đặt:\n\n" + string.Join("\n", errors.Distinct().Select(e => "• " + e)));
            return;
        }
        try
        {
            await _settings.SaveAsync(s);
            if (ThemeManager.IsDark != s.DarkTheme) ThemeManager.Apply(s.DarkTheme);
            StatusText = "Đã lưu cài đặt.";
            LoadBackups();
            if (syncJustEnabled)
            {
                var r = await _sync.RunAsync();
                SyncStatusText = r.Message;
                LoadDevices();
            }
        }
        catch (Exception ex) { _dialogs.Error("Không lưu được cài đặt.", ex); }
    }

    // ------------------------------------------------ Sao lưu / phục hồi
    [RelayCommand]
    private void BrowseBackupFolder()
    {
        var f = _dialogs.PickFolder(BackupFolder);
        if (f != null) BackupFolder = f;
    }

    [RelayCommand] private void OpenBackupFolder()
    {
        System.IO.Directory.CreateDirectory(_backup.BackupFolder);
        _dialogs.OpenPath(_backup.BackupFolder);
    }

    [RelayCommand] private void OpenDataFolder() => _dialogs.OpenPath(_paths.DataDir);
    [RelayCommand] private void OpenLogFolder() => _dialogs.OpenPath(_paths.LogsDir);

    [RelayCommand]
    private async Task BackupNow()
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim();
            var b = await _backup.BackupNowAsync(target);
            LoadBackups();
            StatusText = $"Đã sao lưu: {b.Path}";
            _dialogs.Info($"Sao lưu thành công.\n\n{b.Path}\n({b.SizeText})");
        }
        catch (Exception ex) { _dialogs.Error("Sao lưu thất bại.", ex); }
    }

    [RelayCommand]
    private async Task RestoreSelected()
    {
        if (SelectedBackup == null) { _dialogs.Info("Hãy chọn một bản sao lưu trong danh sách."); return; }
        await RestoreAsync(SelectedBackup.Path);
    }

    [RelayCommand]
    private async Task RestoreFromFile()
    {
        var f = _dialogs.OpenFile("Tệp sao lưu (*.db)|*.db|Tất cả tệp|*.*", _backup.BackupFolder);
        if (f != null) await RestoreAsync(f);
    }

    private async Task RestoreAsync(string file)
    {
        if (!BackupService.IsValidBackup(file, out var err)) { _dialogs.Warn(err); return; }
        if (!_dialogs.Confirm($"PHỤC HỒI dữ liệu từ bản sao lưu?\n\n{file}\n\n" +
                              "Toàn bộ dữ liệu hiện tại sẽ được thay bằng dữ liệu trong bản sao lưu.\n" +
                              "Phần mềm sẽ tự sao lưu dữ liệu hiện tại trước khi phục hồi, sau đó khởi động lại."))
            return;
        try
        {
            var safety = await _backup.RestoreAsync(file);
            _dialogs.Info($"Đã phục hồi dữ liệu. Bản sao lưu dữ liệu trước khi phục hồi:\n{safety.Path}\n\nPhần mềm sẽ khởi động lại.");
            App.Restart();
        }
        catch (Exception ex) { _dialogs.Error("Phục hồi thất bại. Dữ liệu hiện tại không bị thay đổi.", ex); }
    }

    // ------------------------------------------------ Danh mục nghiệp vụ
    [RelayCommand]
    private async Task AddGroup()
    {
        try
        {
            await _categories.AddAsync(CategoryName, null);
            await LoadCategoriesAsync();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { _dialogs.Error("Không thêm được nhóm.", ex); }
    }

    [RelayCommand]
    private async Task AddItem()
    {
        if (SelectedCategory == null) { _dialogs.Info("Hãy chọn nhóm (hoặc một mục trong nhóm) để thêm mục con."); return; }
        var parent = SelectedCategory.ParentId ?? SelectedCategory.Id;
        try
        {
            await _categories.AddAsync(CategoryName, parent);
            await LoadCategoriesAsync();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { _dialogs.Error("Không thêm được mục.", ex); }
    }

    [RelayCommand]
    private async Task RenameCategory()
    {
        if (SelectedCategory == null) { _dialogs.Info("Hãy chọn nhóm/mục cần đổi tên."); return; }
        try
        {
            await _categories.RenameAsync(SelectedCategory.Id, CategoryName);
            await LoadCategoriesAsync();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { _dialogs.Error("Không đổi tên được.", ex); }
    }

    [RelayCommand]
    private async Task DeactivateCategory()
    {
        if (SelectedCategory == null) { _dialogs.Info("Hãy chọn nhóm/mục cần ngừng sử dụng."); return; }
        if (!_dialogs.Confirm($"Ngừng sử dụng \"{SelectedCategory.Name}\"?\n\nCông việc đã gắn danh mục này vẫn giữ nguyên thông tin.")) return;
        try
        {
            await _categories.DeactivateAsync(SelectedCategory.Id);
            await LoadCategoriesAsync();
            CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { _dialogs.Error("Không thực hiện được.", ex); }
    }
}
