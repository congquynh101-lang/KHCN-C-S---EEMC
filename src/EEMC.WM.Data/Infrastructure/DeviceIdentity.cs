using System.Text.Json;

namespace EEMC.WM.Data.Infrastructure;

/// <summary>
/// Định danh của máy tính này trong hệ thống đồng bộ. Lưu ở tệp device.json cạnh CSDL
/// (không nằm trong CSDL, để phục hồi bản sao lưu từ máy khác không làm hai máy trùng định danh).
/// </summary>
public sealed class DeviceIdentity
{
    private sealed record Stored(string DeviceId, string DeviceName);

    public DeviceIdentity(AppPaths paths)
    {
        var file = Path.Combine(paths.DataDir, "device.json");
        Stored? s = null;
        try
        {
            if (File.Exists(file)) s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file));
        }
        catch (Exception ex) { AppLog.Error(ex, "Đọc device.json"); }

        if (s == null || string.IsNullOrWhiteSpace(s.DeviceId) || s.DeviceId.Length != 32)
        {
            s = new Stored(Guid.NewGuid().ToString("N"), Environment.MachineName);
            try { File.WriteAllText(file, JsonSerializer.Serialize(s)); }
            catch (Exception ex) { AppLog.Error(ex, "Ghi device.json"); }
        }
        DeviceId = s.DeviceId;
        DeviceName = string.IsNullOrWhiteSpace(s.DeviceName) ? Environment.MachineName : s.DeviceName;
    }

    /// <summary>Tạo định danh cho kiểm thử.</summary>
    public DeviceIdentity(string deviceId, string deviceName)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
    }

    public string DeviceId { get; }
    public string DeviceName { get; }
}
