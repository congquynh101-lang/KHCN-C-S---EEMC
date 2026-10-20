namespace EEMC.WM.Core.Time;

public interface IClock
{
    /// <summary>Thời điểm hiện tại theo giờ Việt Nam (Asia/Ho_Chi_Minh).</summary>
    DateTime Now { get; }
}

public sealed class VietnamClock : IClock
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }

    public DateTime Now
    {
        get
        {
            var t = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
            // Bỏ phần nhỏ hơn giây để lưu trữ/so sánh gọn gàng
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Unspecified);
        }
    }
}

public sealed class FixedClock : IClock
{
    public FixedClock(DateTime now) { Now = now; }
    public DateTime Now { get; set; }
}
