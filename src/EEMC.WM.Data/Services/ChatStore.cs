using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Lịch sử hội thoại với trợ lý AI: mỗi người chỉ xem, xóa hội thoại của mình.</summary>
public sealed class ChatStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;

    public ChatStore(IDbContextFactory<AppDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<List<ChatConversation>> GetConversationsAsync(int userId, int take = 50)
    {
        Access.RequireSelf(userId);
        await using var db = await _factory.CreateDbContextAsync();
        return await db.ChatConversations.AsNoTracking().Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt).Take(take).ToListAsync();
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(int userId, int conversationId)
    {
        Access.RequireSelf(userId);
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.ChatConversations.AnyAsync(c => c.Id == conversationId && c.UserId == userId)) return new();
        return await db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).ToListAsync();
    }

    public async Task<ChatConversation> CreateConversationAsync(int userId, string firstQuestion)
    {
        Access.RequireSelf(userId);
        var title = firstQuestion.Trim().Replace('\n', ' ');
        if (title.Length > 80) title = title[..77].TrimEnd() + "...";
        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        var c = new ChatConversation { UserId = userId, Title = title.Length == 0 ? "Hội thoại" : title, CreatedAt = now, UpdatedAt = now };
        db.ChatConversations.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task<ChatMessage> AddMessageAsync(int userId, int conversationId, ChatMessage m)
    {
        Access.RequireSelf(userId);
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.ChatConversations.FirstOrDefaultAsync(x => x.Id == conversationId && x.UserId == userId)
                ?? throw new InvalidOperationException("Không tìm thấy hội thoại.");
        m.ConversationId = conversationId;
        m.CreatedAt = _clock.Now;
        c.UpdatedAt = m.CreatedAt;
        db.ChatMessages.Add(m);
        await db.SaveChangesAsync();
        return m;
    }

    public async Task DeleteConversationAsync(int userId, int conversationId)
    {
        Access.RequireSelf(userId);
        await using var db = await _factory.CreateDbContextAsync();
        await db.ChatConversations.Where(c => c.Id == conversationId && c.UserId == userId).ExecuteDeleteAsync();
    }

    /// <summary>Số token đã dùng hôm nay của người dùng (giới hạn chi phí).</summary>
    public async Task<long> TokensTodayAsync(int userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var today = _clock.Now.Date;
        var ids = db.ChatConversations.Where(c => c.UserId == userId).Select(c => c.Id);
        return await db.ChatMessages.Where(m => ids.Contains(m.ConversationId) && m.CreatedAt >= today)
            .SumAsync(m => m.InputTokens + m.OutputTokens);
    }
}
