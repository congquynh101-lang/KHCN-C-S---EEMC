using System.Text.Json;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

// ============================================================================
// ĐỒNG BỘ NHIỀU MÁY QUA THƯ MỤC DÙNG CHUNG (OneDrive/SharePoint/thư mục mạng)
//
// <Thư mục đồng bộ>/devices/<mã máy>/device.json            thông tin máy
// <Thư mục đồng bộ>/devices/<mã máy>/<ticks 19 số>-b.json    gói thay đổi (bất biến)
// <Thư mục đồng bộ>/devices/<mã máy>/<ticks 19 số>-s.json    gói ảnh chụp toàn bộ (sau khi dọn gói cũ)
//
// - Mỗi máy CHỈ ghi vào thư mục của mình → OneDrive không bao giờ phải gộp tệp xung đột.
// - Tệp được ghi tạm (.tmp) rồi đổi tên → máy khác không đọc phải tệp ghi dở.
// - Mỗi máy đọc các tệp mới của máy khác theo thứ tự tên và ghi nhớ tệp cuối đã đọc.
// - Xung đột sửa cùng một công việc: bản có dấu thời gian (SyncStamp) mới hơn được giữ.
// - Lịch sử trao đổi chỉ cộng thêm, không mất.
// - Hai máy làm offline sinh trùng mã CV: máy tạo sau tự đổi sang mã mới.
// ============================================================================

public sealed class SyncCategoryDto
{
    public string SyncId { get; set; } = "";
    public long Stamp { get; set; }
    public string Name { get; set; } = "";
    public string? ParentSyncId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SyncTaskDto
{
    public string SyncId { get; set; } = "";
    public long Stamp { get; set; }
    public string Code { get; set; } = "";
    public string? OriginDevice { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime DueAt { get; set; }
    public string Title { get; set; } = "";
    public TaskPriority Priority { get; set; }
    public WorkTaskStatus Status { get; set; }
    public string? CategorySyncId { get; set; }
    public string? ParentSyncId { get; set; }
    public string? ProjectName { get; set; }
    public string? AssignedBy { get; set; }
    public string? Assignee { get; set; }
    public string? Coordinator { get; set; }
    public double? EstimatedHours { get; set; }
    public DateTime? PlannedStart { get; set; }
    public bool IsFixedTime { get; set; }
    public int Progress { get; set; }
    public string? Deliverable { get; set; }
    public DocumentStatus DocumentStatus { get; set; }
    public string? DocumentNumber { get; set; }
    public string? LeaderOpinionNeeded { get; set; }
    public string? Notes { get; set; }
    public string? FolderPath { get; set; }
    public DateTime? ExpectedResponseAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SyncHistoryDto
{
    public string SyncId { get; set; } = "";
    public string TaskSyncId { get; set; } = "";
    public DateTime At { get; set; }
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsComment { get; set; }
}

public sealed class SyncTombstoneDto
{
    public string Kind { get; set; } = "";
    public string SyncId { get; set; } = "";
    public long Stamp { get; set; }
}

public sealed class SyncBatch
{
    public int Format { get; set; } = 1;
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public bool IsSnapshot { get; set; }
    public List<SyncCategoryDto> Categories { get; set; } = new();
    public List<SyncTaskDto> Tasks { get; set; } = new();
    public List<SyncHistoryDto> Histories { get; set; } = new();
    public List<SyncTombstoneDto> Tombstones { get; set; } = new();
    public WorkSettings? SharedSettings { get; set; }
    public long SharedSettingsStamp { get; set; }

    public int Count => Categories.Count + Tasks.Count + Histories.Count + Tombstones.Count + (SharedSettings != null ? 1 : 0);
}

public sealed class SyncDeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string? UserName { get; set; }
    public string? AppVersion { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public bool IsThisDevice { get; set; }

    public string LastSeenText => LastWriteUtc == default ? "" : "Đồng bộ lúc " + Labels.DateTime(LastWriteUtc.ToLocalTime());
    public string Title => IsThisDevice ? $"{DeviceName} (máy này)" : DeviceName;
}

public enum SyncState { Disabled, Idle, Running, Ok, Error }

public sealed class SyncResult
{
    public SyncState State { get; init; }
    public DateTime At { get; init; }
    public int Imported { get; init; }
    public int Exported { get; init; }
    public int CodesRenamed { get; init; }
    public string Message { get; init; } = "";
}

public sealed class SyncService
{
    private const int ChunkSize = 2000;
    private const int CompactAfterFiles = 300;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly SettingsService _settings;
    private readonly DeviceIdentity _device;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastFileTicks;

    public SyncService(IDbContextFactory<AppDbContext> factory, SettingsService settings, DeviceIdentity device)
    {
        _factory = factory;
        _settings = settings;
        _device = device;
    }

    /// <summary>Số tệp trong thư mục của máy này để bắt đầu dọn gói cũ (tạo ảnh chụp toàn bộ).</summary>
    public int CompactThreshold { get; set; } = CompactAfterFiles;

    public SyncResult LastResult { get; private set; } = new() { State = SyncState.Disabled };
    public event EventHandler? StatusChanged;
    /// <summary>Có dữ liệu nhận về từ máy khác (để giao diện làm mới).</summary>
    public event EventHandler? DataImported;

    public string DeviceId => _device.DeviceId;
    public string DeviceName => _device.DeviceName;

    /// <summary>Thư mục OneDrive của tổ chức (hoặc cá nhân) trên máy này, nếu có.</summary>
    public static string? SuggestFolder()
    {
        foreach (var v in new[] { "OneDriveCommercial", "OneDrive" })
        {
            var root = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Path.Combine(root, "EEMC WorkManager", "DongBo");
        }
        return null;
    }

    private void Report(SyncResult r)
    {
        LastResult = r;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ Chạy một lượt đồng bộ

    public async Task<SyncResult> RunAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (!s.SyncEnabled || string.IsNullOrWhiteSpace(s.SyncFolder))
        {
            var off = new SyncResult { State = SyncState.Disabled, At = DateTime.Now, Message = "Chưa bật đồng bộ" };
            Report(off);
            return off;
        }
        if (!await _gate.WaitAsync(0, ct)) return LastResult;
        try
        {
            Report(new SyncResult { State = SyncState.Running, At = DateTime.Now, Message = "Đang đồng bộ..." });
            var root = s.SyncFolder!.Trim();
            var devicesDir = Path.Combine(root, "devices");
            var myDir = Path.Combine(devicesDir, _device.DeviceId);
            try { Directory.CreateDirectory(myDir); }
            catch (Exception ex) { throw new IOException($"Không truy cập được thư mục đồng bộ: {root}. {ex.Message}", ex); }

            int imported = await Task.Run(() => ImportAllAsync(devicesDir, ct), ct);
            int renamed = await ResolveCodeCollisionsAsync();
            int exported = await Task.Run(() => ExportAsync(myDir, snapshot: false), ct);
            await Task.Run(() => CompactIfNeededAsync(myDir), ct);
            WriteDeviceInfo(myDir);

            if (imported > 0 || renamed > 0) DataImported?.Invoke(this, EventArgs.Empty);
            var ok = new SyncResult
            {
                State = SyncState.Ok, At = DateTime.Now, Imported = imported, Exported = exported, CodesRenamed = renamed,
                Message = $"Đã đồng bộ lúc {DateTime.Now:HH:mm}" +
                          (imported > 0 ? $" • nhận {imported} thay đổi" : "") +
                          (exported > 0 ? $" • gửi {exported} thay đổi" : "") +
                          (renamed > 0 ? $" • đổi {renamed} mã trùng" : "")
            };
            Report(ok);
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Đồng bộ dữ liệu");
            var err = new SyncResult { State = SyncState.Error, At = DateTime.Now, Message = "Lỗi đồng bộ: " + ex.Message };
            Report(err);
            return err;
        }
        finally { _gate.Release(); }
    }

    private void WriteDeviceInfo(string myDir)
    {
        var info = new SyncDeviceInfo
        {
            DeviceId = _device.DeviceId,
            DeviceName = _device.DeviceName,
            UserName = Environment.UserName,
            AppVersion = typeof(SyncService).Assembly.GetName().Version?.ToString(3),
            LastWriteUtc = DateTime.UtcNow
        };
        WriteAtomic(Path.Combine(myDir, "device.json"), JsonSerializer.Serialize(info, Json));
    }

    public List<SyncDeviceInfo> GetDevices()
    {
        var list = new List<SyncDeviceInfo>();
        var folder = _settings.Current.SyncFolder;
        if (string.IsNullOrWhiteSpace(folder)) return list;
        var devicesDir = Path.Combine(folder, "devices");
        if (!Directory.Exists(devicesDir)) return list;
        foreach (var dir in Directory.GetDirectories(devicesDir))
        {
            var id = Path.GetFileName(dir);
            SyncDeviceInfo? info = null;
            try
            {
                var f = Path.Combine(dir, "device.json");
                if (File.Exists(f)) info = JsonSerializer.Deserialize<SyncDeviceInfo>(File.ReadAllText(f), Json);
            }
            catch { /* bỏ qua tệp lỗi */ }
            info ??= new SyncDeviceInfo { DeviceId = id, DeviceName = id[..Math.Min(8, id.Length)] };
            info.IsThisDevice = id == _device.DeviceId;
            list.Add(info);
        }
        return list.OrderByDescending(d => d.IsThisDevice).ThenByDescending(d => d.LastWriteUtc).ToList();
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    // ------------------------------------------------------------------ Nhập

    private async Task<int> ImportAllAsync(string devicesDir, CancellationToken ct)
    {
        if (!Directory.Exists(devicesDir)) return 0;
        int total = 0;
        foreach (var dir in Directory.GetDirectories(devicesDir))
        {
            var sourceId = Path.GetFileName(dir);
            if (sourceId == _device.DeviceId || sourceId.Length != 32) continue;

            await using var stateDb = await _factory.CreateDbContextAsync(ct);
            var state = await stateDb.SyncSourceStates.FirstOrDefaultAsync(x => x.DeviceId == sourceId, ct);
            var last = state?.LastFile ?? "";
            var files = Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileName)
                .Where(n => n != null && n != "device.json" && n.Length >= 21 && string.CompareOrdinal(n, last) > 0)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            foreach (var name in files)
            {
                ct.ThrowIfCancellationRequested();
                SyncBatch? batch;
                try
                {
                    batch = JsonSerializer.Deserialize<SyncBatch>(await File.ReadAllTextAsync(Path.Combine(dir, name!), ct), Json);
                }
                catch (Exception ex)
                {
                    // Tệp có thể đang được OneDrive tải về; thử lại lượt sau
                    AppLog.Warn($"Chưa đọc được tệp đồng bộ {name} của máy {sourceId}: {ex.Message}");
                    break;
                }
                if (batch == null || batch.Format != 1) { AppLog.Warn($"Bỏ qua tệp đồng bộ không hợp lệ: {name}"); }
                else total += await ApplyBatchAsync(batch);

                state ??= stateDb.SyncSourceStates.Add(new SyncSourceState { DeviceId = sourceId }).Entity;
                state.LastFile = name!;
                state.LastImportUtc = DateTime.UtcNow;
                if (batch != null && !string.IsNullOrWhiteSpace(batch.DeviceName)) state.DeviceName = batch.DeviceName;
                await stateDb.SaveChangesAsync(ct);
            }
        }
        return total;
    }

    /// <returns>Số bản ghi thực sự thay đổi trên máy này.</returns>
    public async Task<int> ApplyBatchAsync(SyncBatch batch)
    {
        int changed = 0;
        await using var db = await _factory.CreateDbContextAsync();
        db.ImportMode = true;
        await using var tx = await db.Database.BeginTransactionAsync();

        // --- Dấu vết xóa (đọc trước để không thêm lại công việc đã xóa vĩnh viễn)
        var tombs = await db.SyncTombstones.ToDictionaryAsync(t => t.Kind + ":" + t.SyncId);

        // --- Danh mục
        var cats = await db.Categories.ToDictionaryAsync(c => c.SyncId);
        foreach (var d in batch.Categories.OrderBy(c => c.ParentSyncId == null ? 0 : 1))
        {
            cats.TryGetValue(d.SyncId, out var local);
            if (local != null && d.Stamp <= local.SyncStamp) continue;
            var parent = d.ParentSyncId != null && cats.TryGetValue(d.ParentSyncId, out var p) ? p : null;
            if (local == null)
            {
                local = new Category { SyncId = d.SyncId };
                db.Categories.Add(local);
                cats[d.SyncId] = local;
            }
            local.Name = d.Name;
            // Gán qua quan hệ để nhóm mới trong cùng gói (chưa có Id) vẫn liên kết đúng
            local.Parent = parent;
            if (parent == null) local.ParentId = null;
            local.SortOrder = d.SortOrder;
            local.IsActive = d.IsActive;
            local.SyncStamp = d.Stamp;
            local.SyncDirty = false;
            changed++;
        }
        await db.SaveChangesAsync();

        // --- Công việc
        var taskIds = batch.Tasks.Select(t => t.SyncId).Concat(batch.Tasks.Where(t => t.ParentSyncId != null).Select(t => t.ParentSyncId!))
            .Concat(batch.Histories.Select(h => h.TaskSyncId)).Distinct().ToList();
        var tasks = await db.Tasks.Where(t => taskIds.Contains(t.SyncId)).ToDictionaryAsync(t => t.SyncId);
        var pendingParents = new List<(WorkTask Task, string ParentSyncId)>();
        foreach (var d in batch.Tasks)
        {
            if (tombs.TryGetValue("task:" + d.SyncId, out var tomb) && tomb.SyncStamp >= d.Stamp) continue;
            tasks.TryGetValue(d.SyncId, out var local);
            if (local != null && d.Stamp <= local.SyncStamp) continue;
            if (local == null)
            {
                local = new WorkTask { SyncId = d.SyncId };
                db.Tasks.Add(local);
                tasks[d.SyncId] = local;
            }
            else if (local.DueAt != d.DueAt)
            {
                // Hạn mới: đặt lại các mốc nhắc trên máy này
                var lid = local.Id;
                await db.ReminderLogs.Where(r => r.TaskId == lid).ExecuteDeleteAsync();
            }
            CopyToEntity(d, local, cats);
            if (d.ParentSyncId != null) pendingParents.Add((local, d.ParentSyncId));
            else local.ParentId = null;
            changed++;
        }
        await db.SaveChangesAsync();
        foreach (var (t, parentSyncId) in pendingParents)
            t.ParentId = tasks.TryGetValue(parentSyncId, out var parent) ? parent.Id : null;
        if (pendingParents.Count > 0) await db.SaveChangesAsync();

        // --- Lịch sử (chỉ cộng thêm)
        if (batch.Histories.Count > 0)
        {
            var hids = batch.Histories.Select(h => h.SyncId).ToList();
            var existing = (await db.TaskHistories.Where(h => hids.Contains(h.SyncId)).Select(h => h.SyncId).ToListAsync()).ToHashSet();
            foreach (var h in batch.Histories)
            {
                if (existing.Contains(h.SyncId) || !tasks.TryGetValue(h.TaskSyncId, out var t)) continue;
                db.TaskHistories.Add(new TaskHistory
                {
                    SyncId = h.SyncId, TaskId = t.Id, At = h.At, Action = h.Action, Detail = h.Detail,
                    IsComment = h.IsComment, SyncStamp = 1, SyncDirty = false
                });
                existing.Add(h.SyncId);
                changed++;
            }
            await db.SaveChangesAsync();
        }

        // --- Xóa vĩnh viễn
        foreach (var d in batch.Tombstones.Where(t => t.Kind == "task"))
        {
            var key = "task:" + d.SyncId;
            if (tombs.TryGetValue(key, out var existingTomb))
            {
                if (existingTomb.SyncStamp < d.Stamp) existingTomb.SyncStamp = d.Stamp;
            }
            else
            {
                var nt = new SyncTombstone { Kind = "task", SyncId = d.SyncId, SyncStamp = d.Stamp, SyncDirty = false };
                db.SyncTombstones.Add(nt);
                tombs[key] = nt;
            }
            var local = await db.Tasks.FirstOrDefaultAsync(t => t.SyncId == d.SyncId);
            if (local != null && local.SyncStamp <= d.Stamp)
            {
                var id = local.Id;
                await db.TaskHistories.Where(h => h.TaskId == id).ExecuteDeleteAsync();
                await db.ReminderLogs.Where(r => r.TaskId == id).ExecuteDeleteAsync();
                await db.Notifications.Where(n => n.TaskId == id).ExecuteDeleteAsync();
                await db.Tasks.Where(t => t.ParentId == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.ParentId, (int?)null));
                db.Tasks.Remove(local);
                changed++;
            }
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // --- Thiết lập làm việc dùng chung
        if (batch.SharedSettings != null && await _settings.ApplySharedFromSyncAsync(batch.SharedSettings, batch.SharedSettingsStamp))
            changed++;

        return changed;
    }

    private static void CopyToEntity(SyncTaskDto d, WorkTask t, IReadOnlyDictionary<string, Category> cats)
    {
        t.Code = d.Code;
        t.OriginDevice = d.OriginDevice;
        t.ReceivedAt = d.ReceivedAt;
        t.DueAt = d.DueAt;
        t.Title = d.Title;
        t.Priority = d.Priority;
        t.Status = d.Status;
        t.CategoryId = d.CategorySyncId != null && cats.TryGetValue(d.CategorySyncId, out var c) ? c.Id : null;
        t.ProjectName = d.ProjectName;
        t.AssignedBy = d.AssignedBy;
        t.Assignee = d.Assignee;
        t.Coordinator = d.Coordinator;
        t.EstimatedHours = d.EstimatedHours;
        t.PlannedStart = d.PlannedStart;
        t.IsFixedTime = d.IsFixedTime;
        t.Progress = d.Progress;
        t.Deliverable = d.Deliverable;
        t.DocumentStatus = d.DocumentStatus;
        t.DocumentNumber = d.DocumentNumber;
        t.LeaderOpinionNeeded = d.LeaderOpinionNeeded;
        t.Notes = d.Notes;
        t.FolderPath = d.FolderPath;
        t.ExpectedResponseAt = d.ExpectedResponseAt;
        t.StartedAt = d.StartedAt;
        t.CompletedAt = d.CompletedAt;
        t.ClosedAt = d.ClosedAt;
        t.CreatedAt = d.CreatedAt;
        t.UpdatedAt = d.UpdatedAt;
        t.IsDeleted = d.IsDeleted;
        t.DeletedAt = d.DeletedAt;
        t.SyncStamp = d.Stamp;
        t.SyncDirty = false;
        t.RefreshSearchText();
    }

    // ------------------------------------------------------------------ Trùng mã công việc

    /// <summary>
    /// Hai máy làm việc offline có thể cùng sinh một mã. Công việc tạo sau (theo CreatedAt, rồi SyncId)
    /// đổi sang mã mới — chỉ máy đã tạo công việc đó thực hiện, rồi gửi mã mới cho các máy khác.
    /// </summary>
    public async Task<int> ResolveCodeCollisionsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var dupCodes = await db.Tasks.GroupBy(t => t.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync();
        int renamed = 0;
        foreach (var code in dupCodes)
        {
            var group = await db.Tasks.Where(t => t.Code == code).ToListAsync();
            var ordered = group.OrderBy(t => t.CreatedAt).ThenBy(t => t.SyncId, StringComparer.Ordinal).ToList();
            foreach (var t in ordered.Skip(1))
            {
                bool mine = t.OriginDevice == null || t.OriginDevice == _device.DeviceId;
                if (!mine) continue;
                var old = t.Code;
                t.Code = await TaskService.NextCodeAsync(db, t.ReceivedAt.Year);
                t.RefreshSearchText();
                db.TaskHistories.Add(new TaskHistory
                {
                    TaskId = t.Id, At = DateTime.Now, Action = "Đổi mã",
                    Detail = $"Mã {old} trùng với công việc tạo trên máy khác khi đồng bộ; đổi thành {t.Code}"
                });
                await db.SaveChangesAsync();
                renamed++;
            }
        }
        return renamed;
    }

    // ------------------------------------------------------------------ Xuất

    private sealed record ExportSet(List<SyncBatch> Batches, List<Category> Cats, List<WorkTask> Tasks, List<TaskHistory> Histories, List<SyncTombstone> Tombs, bool SettingsDirty, int Total);

    /// <summary>Đóng gói dữ liệu cần gửi (snapshot = toàn bộ) thành các gói, chưa ghi tệp.</summary>
    private async Task<ExportSet> BuildExportAsync(bool snapshot)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var catSync = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.SyncId);
        var allCats = await db.Categories.AsNoTracking().Where(c => snapshot || c.SyncDirty).ToListAsync();
        var tasks = await db.Tasks.AsNoTracking().Where(t => snapshot || t.SyncDirty).ToListAsync();
        var taskSync = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.SyncId);
        var histories = await db.TaskHistories.AsNoTracking().Where(h => snapshot || h.SyncDirty).ToListAsync();
        var tombs = await db.SyncTombstones.AsNoTracking().Where(t => snapshot || t.SyncDirty).ToListAsync();
        var (settingsStamp, settingsDirty) = await _settings.GetSharedSyncStateAsync();
        bool includeSettings = settingsDirty || (snapshot && settingsStamp > 0);

        int total = allCats.Count + tasks.Count + histories.Count + tombs.Count + (includeSettings ? 1 : 0);
        if (total == 0) return new ExportSet(new List<SyncBatch>(), allCats, tasks, histories, tombs, settingsDirty, 0);

        // Chia thành nhiều tệp nếu nhiều thay đổi (lần đồng bộ đầu tiên)
        var batches = new List<SyncBatch>();
        SyncBatch NewBatch() { var b = new SyncBatch { DeviceId = _device.DeviceId, DeviceName = _device.DeviceName, CreatedUtc = DateTime.UtcNow, IsSnapshot = snapshot }; batches.Add(b); return b; }
        var cur = NewBatch();
        void Roll() { if (cur.Count >= ChunkSize) cur = NewBatch(); }

        foreach (var c in allCats.OrderBy(c => c.ParentId == null ? 0 : 1))
        {
            cur.Categories.Add(new SyncCategoryDto
            {
                SyncId = c.SyncId, Stamp = c.SyncStamp, Name = c.Name, SortOrder = c.SortOrder, IsActive = c.IsActive,
                ParentSyncId = c.ParentId.HasValue && catSync.TryGetValue(c.ParentId.Value, out var ps) ? ps : null
            });
            Roll();
        }
        foreach (var t in tasks)
        {
            cur.Tasks.Add(ToDto(t, catSync, taskSync));
            Roll();
        }
        foreach (var h in histories)
        {
            if (!taskSync.TryGetValue(h.TaskId, out var tsid)) continue;
            cur.Histories.Add(new SyncHistoryDto { SyncId = h.SyncId, TaskSyncId = tsid, At = h.At, Action = h.Action, Detail = h.Detail, IsComment = h.IsComment });
            Roll();
        }
        foreach (var t in tombs)
        {
            cur.Tombstones.Add(new SyncTombstoneDto { Kind = t.Kind, SyncId = t.SyncId, Stamp = t.SyncStamp });
            Roll();
        }
        if (includeSettings)
        {
            cur.SharedSettings = _settings.CloneCurrent();
            cur.SharedSettings.SyncFolder = null;
            cur.SharedSettings.BackupFolder = null;
            cur.SharedSettingsStamp = settingsStamp;
        }
        return new ExportSet(batches, allCats, tasks, histories, tombs, settingsDirty, total);
    }

    /// <summary>Toàn bộ dữ liệu dạng gói đồng bộ (dùng khi nhập dữ liệu bản desktop lên bản web).</summary>
    public async Task<List<SyncBatch>> BuildSnapshotBatchesAsync() => (await BuildExportAsync(snapshot: true)).Batches.Where(b => b.Count > 0).ToList();

    private async Task<int> ExportAsync(string myDir, bool snapshot)
    {
        var (batches, allCats, tasks, histories, tombs, settingsDirty, total) = await BuildExportAsync(snapshot);
        if (total == 0) return 0;

        string? firstName = null;
        foreach (var b in batches.Where(b => b.Count > 0))
        {
            var name = NextFileName(myDir, snapshot && firstName == null ? "s" : "b");
            firstName ??= name;
            WriteAtomic(Path.Combine(myDir, name), JsonSerializer.Serialize(b, Json));
        }
        if (snapshot && firstName != null)
        {
            // Đã có ảnh chụp toàn bộ: xóa các gói cũ của máy này
            foreach (var old in Directory.GetFiles(myDir, "*.json").Select(Path.GetFileName)
                         .Where(n => n != "device.json" && string.CompareOrdinal(n, firstName) < 0))
            {
                try { File.Delete(Path.Combine(myDir, old!)); } catch { /* xóa lần sau */ }
            }
        }

        // Đánh dấu đã gửi (chỉ khi chưa bị sửa tiếp trong lúc ghi tệp)
        await using (var mark = await _factory.CreateDbContextAsync())
        {
            mark.ImportMode = true;
            await MarkClean(mark.Categories, allCats.Where(x => x.SyncDirty).ToDictionary(x => x.Id, x => x.SyncStamp));
            await MarkClean(mark.Tasks, tasks.Where(x => x.SyncDirty).ToDictionary(x => x.Id, x => x.SyncStamp));
            await MarkClean(mark.TaskHistories, histories.Where(x => x.SyncDirty).ToDictionary(x => x.Id, x => x.SyncStamp));
            var tombIds = tombs.Where(t => t.SyncDirty).Select(t => t.Id).ToList();
            foreach (var t in await mark.SyncTombstones.Where(t => tombIds.Contains(t.Id)).ToListAsync())
                if (tombs.First(x => x.Id == t.Id).SyncStamp == t.SyncStamp) t.SyncDirty = false;
            await mark.SaveChangesAsync();
        }
        if (settingsDirty) await _settings.MarkSharedCleanAsync();
        return snapshot ? 0 : total;
    }

    private static async Task MarkClean<T>(DbSet<T> set, Dictionary<int, long> sent) where T : class, ISyncEntity
    {
        if (sent.Count == 0) return;
        var ids = sent.Keys.ToList();
        var rows = await set.Where(e => ids.Contains(EF.Property<int>(e, "Id"))).ToListAsync();
        foreach (var r in rows)
        {
            var id = (int)typeof(T).GetProperty("Id")!.GetValue(r)!;
            if (sent.TryGetValue(id, out var stamp) && stamp == r.SyncStamp) r.SyncDirty = false;
        }
    }

    private static SyncTaskDto ToDto(WorkTask t, IReadOnlyDictionary<int, string> catSync, IReadOnlyDictionary<int, string> taskSync) => new()
    {
        SyncId = t.SyncId, Stamp = t.SyncStamp, Code = t.Code, OriginDevice = t.OriginDevice,
        ReceivedAt = t.ReceivedAt, DueAt = t.DueAt, Title = t.Title, Priority = t.Priority, Status = t.Status,
        CategorySyncId = t.CategoryId.HasValue && catSync.TryGetValue(t.CategoryId.Value, out var cs) ? cs : null,
        ParentSyncId = t.ParentId.HasValue && taskSync.TryGetValue(t.ParentId.Value, out var ps) ? ps : null,
        ProjectName = t.ProjectName, AssignedBy = t.AssignedBy, Assignee = t.Assignee, Coordinator = t.Coordinator,
        EstimatedHours = t.EstimatedHours, PlannedStart = t.PlannedStart, IsFixedTime = t.IsFixedTime, Progress = t.Progress,
        Deliverable = t.Deliverable, DocumentStatus = t.DocumentStatus, DocumentNumber = t.DocumentNumber,
        LeaderOpinionNeeded = t.LeaderOpinionNeeded, Notes = t.Notes, FolderPath = t.FolderPath,
        ExpectedResponseAt = t.ExpectedResponseAt, StartedAt = t.StartedAt, CompletedAt = t.CompletedAt, ClosedAt = t.ClosedAt,
        CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, IsDeleted = t.IsDeleted, DeletedAt = t.DeletedAt
    };

    private string NextFileName(string myDir, string kind)
    {
        // Tên tăng dần theo thời gian UTC (19 chữ số) để sắp xếp đúng thứ tự trên mọi máy
        long ticks = Math.Max(DateTime.UtcNow.Ticks, _lastFileTicks + 1);
        while (File.Exists(Path.Combine(myDir, $"{ticks:D19}-b.json")) || File.Exists(Path.Combine(myDir, $"{ticks:D19}-s.json"))) ticks++;
        _lastFileTicks = ticks;
        return $"{ticks:D19}-{kind}.json";
    }

    private async Task CompactIfNeededAsync(string myDir)
    {
        var count = Directory.GetFiles(myDir, "*.json").Length;
        if (count < CompactThreshold) return;
        AppLog.Info($"Dọn {count} gói đồng bộ cũ, tạo ảnh chụp toàn bộ dữ liệu.");
        await ExportAsync(myDir, snapshot: true);
    }
}
