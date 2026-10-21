using System.Security.Claims;
using EEMC.WM.Core.Domain;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMC.WM.Web.Services;

/// <summary>
/// Gắn người dùng đang đăng nhập vào mọi thao tác trên trình duyệt (sự kiện, điều hướng...) để các dịch vụ dữ liệu
/// kiểm tra quyền. Vai trò lấy từ CSDL nên thay đổi của quản trị có hiệu lực ngay.
/// </summary>
public sealed class AccessCircuitHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _auth;
    private readonly UserService _users;

    public AccessCircuitHandler(AuthenticationStateProvider auth, UserService users)
    {
        _auth = auth;
        _users = users;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            Actor? actor = null;
            try
            {
                var user = (await _auth.GetAuthenticationStateAsync()).User;
                if (AccessWiring.UserId(user) is int id) actor = await _users.GetActorAsync(id);
            }
            catch (InvalidOperationException) { } // chưa có trạng thái đăng nhập khi mạch vừa khởi tạo
            using (Access.Use(actor)) await next(context);
        };
}

public static class AccessWiring
{
    public static int? UserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public static UserRole RoleOf(ClaimsPrincipal user) => Access.ParseRole(user.FindFirst(ClaimTypes.Role)?.Value);

    /// <summary>Phiếu đăng nhập: tài khoản bị khóa/xóa thì đăng xuất; vai trò đổi thì cấp lại phiếu với vai trò mới.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal == null || UserId(ctx.Principal) is not int id) return;
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
        var info = await users.GetAuthInfoAsync(id);
        if (info == null || info.IsDisabled)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var role = Access.RoleName(info.Role);
        if (ctx.Principal.FindFirst(ClaimTypes.Role)?.Value == role) return;
        var claims = ctx.Principal.Claims.Where(c => c.Type != ClaimTypes.Role).Append(new Claim(ClaimTypes.Role, role));
        ctx.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        ctx.ShouldRenew = true;
    }

    /// <summary>Yêu cầu HTTP (trang tải lần đầu, tải tệp...) cũng chạy trong phạm vi người dùng.</summary>
    public static async Task UseActorAsync(HttpContext http, Func<Task> next)
    {
        Actor? actor = null;
        if (UserId(http.User) is int id) actor = await http.RequestServices.GetRequiredService<UserService>().GetActorAsync(id);
        using (Access.Use(actor)) await next();
    }
}
