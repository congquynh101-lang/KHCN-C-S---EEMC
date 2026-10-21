using System.Collections.Concurrent;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

public enum LoginResult { Success, InvalidCredentials, LockedOut, NoUsers, Disabled }

/// <summary>Thông tin tối thiểu để kiểm tra phiên đăng nhập và quyền (được lưu đệm).</summary>
public sealed record UserAuthInfo(int Id, string DisplayName, UserRole Role, bool IsDisabled);

/// <summary>Tài khoản đăng nhập bản web. Mật khẩu được băm (PBKDF2, chuẩn ASP.NET Core Identity).</summary>
public sealed class UserService
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public UserService(IDbContextFactory<AppDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<bool> HasAnyUserAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AnyAsync();
    }

    public static List<string> ValidatePassword(string? password)
    {
        var e = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < 10) e.Add("Mật khẩu tối thiểu 10 ký tự.");
        else
        {
            if (!password.Any(char.IsLetter)) e.Add("Mật khẩu cần có chữ cái.");
            if (!password.Any(char.IsDigit)) e.Add("Mật khẩu cần có chữ số.");
        }
        return e;
    }

    /// <summary>Tạo tài khoản quản trị đầu tiên (chỉ khi chưa có tài khoản nào).</summary>
    public async Task<AppUser> CreateFirstAdminAsync(string userName, string displayName, string password)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Users.AnyAsync()) throw new InvalidOperationException("Đã có tài khoản. Hãy đăng nhập.");
        return await CreateAsync(db, userName, displayName, password, UserRole.Admin);
    }

    private async Task<AppUser> CreateAsync(AppDbContext db, string userName, string displayName, string password, UserRole role)
    {
        var errors = new List<string>();
        userName = (userName ?? "").Trim().ToLowerInvariant();
        if (userName.Length < 3 || userName.Any(c => !(char.IsLetterOrDigit(c) && c < 128) && c != '.' && c != '_' && c != '-'))
            errors.Add("Tên đăng nhập tối thiểu 3 ký tự, chỉ gồm chữ không dấu, số, dấu chấm, gạch dưới, gạch ngang.");
        errors.AddRange(ValidatePassword(password));
        if (await db.Users.AnyAsync(u => u.UserName == userName)) errors.Add("Tên đăng nhập đã tồn tại.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var u = new AppUser
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim(),
            Role = role,
            CreatedAt = _clock.Now
        };
        u.PasswordHash = _hasher.HashPassword(u, password);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    public async Task<(LoginResult Result, AppUser? User)> LoginAsync(string userName, string password)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync()) return (LoginResult.NoUsers, null);
        var name = (userName ?? "").Trim().ToLowerInvariant();
        var u = await db.Users.FirstOrDefaultAsync(x => x.UserName == name);
        var now = _clock.Now;
        if (u == null)
        {
            // Băm giả để thời gian phản hồi giống nhau, tránh dò tên đăng nhập
            _hasher.HashPassword(new AppUser(), password ?? "");
            return (LoginResult.InvalidCredentials, null);
        }
        if (u.LockedUntil.HasValue && u.LockedUntil > now) return (LoginResult.LockedOut, null);
        if (u.IsDisabled)
        {
            _hasher.HashPassword(new AppUser(), password ?? "");
            return (LoginResult.Disabled, null);
        }

        var r = _hasher.VerifyHashedPassword(u, u.PasswordHash, password ?? "");
        if (r == PasswordVerificationResult.Failed)
        {
            u.FailedLogins++;
            if (u.FailedLogins >= MaxFailedLogins)
            {
                u.LockedUntil = now.Add(LockoutTime);
                u.FailedLogins = 0;
            }
            await db.SaveChangesAsync();
            return (u.LockedUntil > now ? LoginResult.LockedOut : LoginResult.InvalidCredentials, null);
        }
        if (r == PasswordVerificationResult.SuccessRehashNeeded) u.PasswordHash = _hasher.HashPassword(u, password!);
        u.FailedLogins = 0;
        u.LockedUntil = null;
        u.LastLoginAt = now;
        await db.SaveChangesAsync();
        return (LoginResult.Success, u);
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        Access.RequireSelfOrAdmin(userId);
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
        if (_hasher.VerifyHashedPassword(u, u.PasswordHash, currentPassword ?? "") == PasswordVerificationResult.Failed)
            throw new ValidationException(new[] { "Mật khẩu hiện tại không đúng." });
        var errors = ValidatePassword(newPassword);
        if (errors.Count > 0) throw new ValidationException(errors);
        u.PasswordHash = _hasher.HashPassword(u, newPassword);
        await db.SaveChangesAsync();
    }

    /// <summary>Đặt lại mật khẩu (chỉ dùng từ dòng lệnh trên máy chủ khi quên mật khẩu).</summary>
    public async Task ResetPasswordAsync(string userName, string newPassword)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var name = (userName ?? "").Trim().ToLowerInvariant();
        var u = await db.Users.FirstOrDefaultAsync(x => x.UserName == name) ?? throw new InvalidOperationException($"Không có tài khoản \"{name}\".");
        var errors = ValidatePassword(newPassword);
        if (errors.Count > 0) throw new ValidationException(errors);
        u.PasswordHash = _hasher.HashPassword(u, newPassword);
        u.FailedLogins = 0;
        u.LockedUntil = null;
        await db.SaveChangesAsync();
    }

    public async Task<AppUser?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    }

    // ---------------------------------------------------------------- Phân quyền

    private readonly ConcurrentDictionary<int, UserAuthInfo?> _authCache = new();

    /// <summary>Vai trò và trạng thái khóa hiện tại của tài khoản (đệm trong bộ nhớ, làm mới khi quản trị thay đổi).</summary>
    public async Task<UserAuthInfo?> GetAuthInfoAsync(int id)
    {
        if (_authCache.TryGetValue(id, out var cached)) return cached;
        await using var db = await _factory.CreateDbContextAsync();
        var info = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UserAuthInfo(u.Id, u.DisplayName, u.Role, u.IsDisabled)).FirstOrDefaultAsync();
        _authCache[id] = info;
        return info;
    }

    /// <summary>Người thực hiện tương ứng tài khoản (null nếu không tồn tại hoặc bị khóa).</summary>
    public async Task<Actor?> GetActorAsync(int id)
    {
        var info = await GetAuthInfoAsync(id);
        return info == null || info.IsDisabled ? null : new Actor(info.Id, info.DisplayName, info.Role);
    }

    // ---------------------------------------------------------------- Quản lý người dùng (quản trị)

    public async Task<List<AppUser>> GetAllAsync()
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.UserName).ToListAsync();
    }

    public async Task<AppUser> CreateUserAsync(string userName, string displayName, string password, UserRole role)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        return await CreateAsync(db, userName, displayName, password, role);
    }

    /// <summary>Đổi tên hiển thị, vai trò, khóa/mở khóa. Luôn giữ ít nhất một quản trị đang hoạt động.</summary>
    public async Task UpdateUserAsync(int id, string displayName, UserRole role, bool disabled)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
        bool losesAdmin = u.Role == UserRole.Admin && !u.IsDisabled && (role != UserRole.Admin || disabled);
        if (losesAdmin && !await db.Users.AnyAsync(x => x.Id != id && x.Role == UserRole.Admin && !x.IsDisabled))
            throw new ValidationException(new[] { "Phải còn ít nhất một tài khoản quản trị đang hoạt động." });
        u.DisplayName = string.IsNullOrWhiteSpace(displayName) ? u.UserName : displayName.Trim();
        u.Role = role;
        u.IsDisabled = disabled;
        await db.SaveChangesAsync();
        _authCache.TryRemove(id, out _);
    }

    /// <summary>Quản trị đặt mật khẩu mới cho tài khoản khác (mở khóa đăng nhập sai).</summary>
    public async Task AdminSetPasswordAsync(int id, string newPassword)
    {
        Access.Require(UserRole.Admin);
        var errors = ValidatePassword(newPassword);
        if (errors.Count > 0) throw new ValidationException(errors);
        await using var db = await _factory.CreateDbContextAsync();
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
        u.PasswordHash = _hasher.HashPassword(u, newPassword);
        u.FailedLogins = 0;
        u.LockedUntil = null;
        await db.SaveChangesAsync();
    }
}
