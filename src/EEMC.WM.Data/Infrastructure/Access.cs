using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Data.Infrastructure;

/// <summary>Người đang thực hiện thao tác (đọc từ tài khoản đăng nhập, vai trò lấy theo CSDL).</summary>
public sealed record Actor(int UserId, string Name, UserRole Role)
{
    public static readonly Actor System = new(0, "Hệ thống", UserRole.Admin);
}

/// <summary>
/// Kiểm tra quyền ngay trong các dịch vụ dữ liệu (không chỉ ẩn nút trên giao diện).
/// Máy chủ web mở một phạm vi cho mỗi yêu cầu/thao tác trên trình duyệt (<see cref="Use"/>); trong phạm vi đó
/// thiếu người dùng hoặc thiếu vai trò sẽ bị từ chối. Ngoài phạm vi: bản desktop và kiểm thử chạy như hệ thống,
/// còn máy chủ web (<see cref="Enforce"/> = true) từ chối.
/// </summary>
public static class Access
{
    private sealed class Scope { public Actor? Actor; }
    private static readonly AsyncLocal<Scope?> Current_ = new();

    /// <summary>Bật trên máy chủ web: thao tác ngoài mọi phạm vi bị từ chối.</summary>
    public static bool Enforce { get; set; }

    public static Actor? Current => Current_.Value?.Actor;

    /// <summary>Đặt người thực hiện cho đoạn xử lý hiện tại (khôi phục khi Dispose).</summary>
    public static IDisposable Use(Actor? actor)
    {
        var previous = Current_.Value;
        Current_.Value = new Scope { Actor = actor };
        return new Restore(previous);
    }

    public static bool Can(UserRole min)
    {
        var scope = Current_.Value;
        if (scope == null) return !Enforce;
        return scope.Actor != null && scope.Actor.Role >= min;
    }

    /// <summary>Ném <see cref="UnauthorizedAccessException"/> nếu người thực hiện không đủ quyền.</summary>
    public static void Require(UserRole min)
    {
        if (Can(min)) return;
        throw new UnauthorizedAccessException(Current == null
            ? "Phiên đăng nhập đã hết hạn. Hãy tải lại trang và đăng nhập."
            : $"Bạn không có quyền thực hiện thao tác này (cần vai trò {Labels.Of(min)}).");
    }

    /// <summary>Chỉ chính chủ tài khoản hoặc quản trị.</summary>
    public static void RequireSelfOrAdmin(int userId)
    {
        if (Can(UserRole.Admin)) return;
        if (Current_.Value != null && Current?.UserId == userId) return;
        if (Current_.Value == null && !Enforce) return;
        throw new UnauthorizedAccessException("Bạn chỉ được thao tác trên tài khoản của mình.");
    }

    /// <summary>Chỉ chính chủ (kể cả quản trị cũng không xem được dữ liệu riêng của người khác, ví dụ hội thoại AI).</summary>
    public static void RequireSelf(int userId)
    {
        var scope = Current_.Value;
        if (scope == null) { if (Enforce) throw new UnauthorizedAccessException("Phiên đăng nhập đã hết hạn."); return; }
        if (scope.Actor == null || scope.Actor.UserId != userId)
            throw new UnauthorizedAccessException("Bạn chỉ xem được dữ liệu của chính mình.");
    }

    // Tên vai trò trong phiếu đăng nhập (cookie)
    public static string RoleName(UserRole r) => r switch { UserRole.Admin => "admin", UserRole.Editor => "editor", _ => "viewer" };

    public static UserRole ParseRole(string? name) => name switch { "admin" => UserRole.Admin, "editor" => UserRole.Editor, _ => UserRole.Viewer };

    private sealed class Restore(Scope? previous) : IDisposable
    {
        public void Dispose() => Current_.Value = previous;
    }
}
