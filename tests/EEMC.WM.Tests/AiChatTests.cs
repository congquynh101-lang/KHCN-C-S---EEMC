using System.Net;
using System.Net.Sockets;
using System.Text;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using EEMC.WM.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace EEMC.WM.Tests;

/// <summary>Trợ lý AI chạy với máy chủ Claude giả lập (không gọi API thật, không tốn chi phí).</summary>
public sealed class AiChatTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-ai", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 22, 9, 0, 0));
    private AppPaths _paths = null!;
    private TestDbFactory _factory = null!;
    private AiSettingsService _settings = null!;
    private ChatStore _store = null!;
    private static readonly Actor User = new(7, "Thư ký", UserRole.Editor);

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_dir);
        _factory = new TestDbFactory(_paths.DatabasePath);
        await DbInitializer.InitializeAsync(_factory);
        _settings = new AiSettingsService(_factory, new EphemeralDataProtectionProvider());
        _store = new ChatStore(_factory, _clock);
        var legal = new LegalService(_factory, _paths, _clock);
        await legal.SaveAsync(new LegalDocument
        {
            Title = "Nghị định mẫu về doanh nghiệp khoa học", DocType = LegalDocType.Decree, Number = "99/2026/NĐ-MAU", Effect = LegalEffect.InEffect,
            FullText = "Chương I\nCHUNG\nĐiều 1. Phạm vi\nNội dung mẫu.\nĐiều 5. Báo cáo định kỳ\n1. Doanh nghiệp khoa học gửi báo cáo hằng năm trước ngày 31 tháng 3."
        });
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public void Keywords_drop_vietnamese_function_words()
    {
        Assert.Equal(new[] { "doanh", "nghiep", "khoa", "hoc", "bao", "cao" }.Except(new[] { "bao" }),
            KnowledgeService.Keywords("Doanh nghiệp khoa học phải báo cáo như thế nào?").Except(new[] { "bao" }));
        Assert.DoesNotContain("la", KnowledgeService.Keywords("Thời hạn là gì"));
    }

    [Fact]
    public async Task Knowledge_returns_article_with_link_and_effect_status()
    {
        var found = await new KnowledgeService(_factory).FindAsync("doanh nghiệp khoa học báo cáo hằng năm");
        var top = found.First();
        Assert.Equal("legal", top.Kind);
        Assert.Contains("Điều 5", top.Title);
        Assert.StartsWith("/legal/", top.Url);
        Assert.Contains("Còn hiệu lực", top.Meta);
    }

    [Fact]
    public async Task Settings_require_admin_and_key_is_encrypted()
    {
        using (Access.Use(User)) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _settings.SaveAsync(new AiSettings { Enabled = true }));
        await _settings.SetApiKeyAsync("sk-ant-test-0000000000abcd");
        Assert.Equal("sk-ant-…abcd", await _settings.DescribeApiKeyAsync());
        await using var db = _factory.CreateDbContext();
        Assert.DoesNotContain("sk-ant-test", db.Settings.Single(s => s.Key == "ai.key").Value); // không lưu dạng rõ
        await Assert.ThrowsAsync<ValidationException>(() => _settings.SetApiKeyAsync("khong-phai-khoa"));
    }

    [Fact]
    public async Task Ask_streams_answer_maps_citations_and_saves_history()
    {
        await _settings.SaveAsync(new AiSettings { Enabled = true, InternalSearch = true, WebSearch = true });
        await _settings.SetApiKeyAsync("sk-ant-test-0000000000abcd");

        using var fake = new FakeClaude();
        var chat = new ChatService(_settings, _store, new KnowledgeService(_factory), _clock) { BaseUrlOverride = fake.BaseUrl };
        var streamed = new StringBuilder();
        ChatResult r;
        using (Access.Use(User))
            r = await chat.AskAsync(User, null, "Doanh nghiệp khoa học báo cáo hằng năm khi nào?",
                d => { streamed.Append(d); return Task.CompletedTask; }, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Equal("Theo quy định, doanh nghiệp gửi báo cáo trước ngày 31/3[1]", r.Text);
        Assert.Equal(r.Text, streamed.ToString());
        var src = Assert.Single(r.Sources);
        Assert.Contains("Điều 5", src.Title);
        Assert.StartsWith("/legal/", src.Url);
        Assert.Equal("gửi báo cáo hằng năm trước ngày 31 tháng 3", src.Quote);

        // Yêu cầu gửi đi: khóa ở header, tài liệu có trích dẫn, tìm web, dự phòng khi bị từ chối, mức suy nghĩ
        var body = fake.LastBody!;
        Assert.Equal("sk-ant-test-0000000000abcd", fake.LastApiKey);
        Assert.Contains("\"model\":\"claude-opus-5-5\"", body);
        Assert.Contains("\"citations\":{\"enabled\":true}", body);
        Assert.Contains("web_search_20260209", body);
        Assert.Contains("\"fallbacks\":\"default\"", body);
        Assert.Contains("\"effort\":\"medium\"", body);
        Assert.Contains("server-side-fallback-2026-07-01", fake.LastBeta);

        using (Access.Use(User))
        {
            var conv = Assert.Single(await _store.GetConversationsAsync(User.UserId));
            var msgs = await _store.GetMessagesAsync(User.UserId, conv.Id);
            Assert.Equal(new[] { "user", "assistant" }, msgs.Select(m => m.Role));
            Assert.Equal(120 + 42, await _store.TokensTodayAsync(User.UserId));
        }
        // Người khác không xem được hội thoại
        using (Access.Use(new Actor(8, "Khác", UserRole.Admin)))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _store.GetConversationsAsync(User.UserId));
    }

    [Fact]
    public async Task Ask_is_blocked_when_disabled_or_over_daily_limit()
    {
        var chat = new ChatService(_settings, _store, new KnowledgeService(_factory), _clock);
        using (Access.Use(User))
        {
            var r = await chat.AskAsync(User, null, "Câu hỏi", _ => Task.CompletedTask, _ => Task.CompletedTask, CancellationToken.None);
            Assert.Equal("Trợ lý AI đang tắt.", r.Error);
        }
    }

    /// <summary>Máy chủ giả lập trả về luồng SSE giống Messages API.</summary>
    private sealed class FakeClaude : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string BaseUrl { get; }
        public string? LastBody, LastApiKey, LastBeta;

        public FakeClaude()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            BaseUrl = $"http://localhost:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                using (var reader = new StreamReader(ctx.Request.InputStream)) LastBody = await reader.ReadToEndAsync();
                LastApiKey = ctx.Request.Headers["x-api-key"];
                LastBeta = ctx.Request.Headers["anthropic-beta"];
                ctx.Response.ContentType = "text/event-stream";
                var sse = string.Concat(
                    Event("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":120,\"output_tokens\":1}}}"),
                    Event("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}"),
                    Event("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Theo quy định, doanh nghiệp gửi báo cáo trước ngày 31/3\"}}"),
                    Event("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"citations_delta\",\"citation\":{\"type\":\"char_location\",\"cited_text\":\"gửi báo cáo hằng năm trước ngày 31 tháng 3\",\"document_index\":0,\"document_title\":\"Điều 5\",\"start_char_index\":10,\"end_char_index\":50}}}"),
                    Event("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}"),
                    Event("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":42}}"),
                    Event("message_stop", "{\"type\":\"message_stop\"}"));
                var bytes = Encoding.UTF8.GetBytes(sse);
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        private static string Event(string name, string data) => $"event: {name}\ndata: {data}\n\n";

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}
