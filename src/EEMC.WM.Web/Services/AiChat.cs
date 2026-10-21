using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Web.Services;

/// <summary>Cấu hình trợ lý AI (chỉ quản trị sửa). Mặc định tắt tra cứu nội bộ và tìm web cho tới khi quản trị bật.</summary>
public sealed class AiSettings
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low / medium / high: mức suy nghĩ, ảnh hưởng chất lượng và chi phí.</summary>
    public string Effort { get; set; } = "medium";
    public int MaxOutputTokens { get; set; } = 8000;
    /// <summary>Số tin nhắn gần nhất gửi kèm làm ngữ cảnh hội thoại.</summary>
    public int HistoryMessages { get; set; } = 8;
    /// <summary>Gửi trích đoạn tài liệu nội bộ (văn bản pháp luật, bản rút gọn đã duyệt, hồ sơ) lên Claude để trả lời.</summary>
    public bool InternalSearch { get; set; }
    public bool WebSearch { get; set; }
    public int WebSearchMaxUses { get; set; } = 3;
    public UserRole MinRole { get; set; } = UserRole.Viewer;
    public long DailyTokensPerUser { get; set; } = 300_000;

    public static readonly (string Id, string Label)[] Models =
    {
        ("claude-opus-5-5", "Claude Opus 5.5 – chính xác nhất (mặc định)"),
        ("claude-sonnet-5-5", "Claude Sonnet 5.5 – nhanh, chi phí thấp hơn"),
        ("claude-haiku-5-5", "Claude Haiku 5.5 – rẻ nhất, câu hỏi đơn giản"),
    };
}

/// <summary>Lưu cấu hình AI; khóa API được mã hóa (ASP.NET Data Protection) và không bao giờ gửi xuống trình duyệt.</summary>
public sealed class AiSettingsService
{
    private const string ConfigKey = "ai.config", ApiKeyKey = "ai.key";
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IDataProtector _protector;

    public AiSettingsService(IDbContextFactory<AppDbContext> factory, IDataProtectionProvider dp)
    {
        _factory = factory;
        _protector = dp.CreateProtector("EEMC.WM.AI.ApiKey");
    }

    public async Task<AiSettings> GetAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == ConfigKey).Select(s => s.Value).FirstOrDefaultAsync();
        try { return json == null ? new AiSettings() : JsonSerializer.Deserialize<AiSettings>(json) ?? new AiSettings(); }
        catch (JsonException) { return new AiSettings(); }
    }

    public async Task SaveAsync(AiSettings s)
    {
        Access.Require(UserRole.Admin);
        var errors = new List<string>();
        if (AiSettings.Models.All(m => m.Id != s.Model)) errors.Add("Mô hình không hợp lệ.");
        if (s.Effort is not ("low" or "medium" or "high")) errors.Add("Mức suy nghĩ không hợp lệ.");
        if (s.MaxOutputTokens is < 1000 or > 32000) errors.Add("Độ dài trả lời tối đa trong khoảng 1.000–32.000 token.");
        if (s.HistoryMessages is < 0 or > 30) errors.Add("Số tin nhắn ngữ cảnh trong khoảng 0–30.");
        if (s.WebSearchMaxUses is < 1 or > 10) errors.Add("Số lần tìm web mỗi câu hỏi trong khoảng 1–10.");
        if (s.DailyTokensPerUser < 10_000) errors.Add("Hạn mức token mỗi ngày tối thiểu 10.000.");
        if (errors.Count > 0) throw new ValidationException(errors);
        await SetAsync(ConfigKey, JsonSerializer.Serialize(s));
    }

    public async Task SetApiKeyAsync(string? apiKey)
    {
        Access.Require(UserRole.Admin);
        apiKey = apiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey)) { await SetAsync(ApiKeyKey, null); return; }
        if (!apiKey.StartsWith("sk-ant-")) throw new ValidationException(new[] { "Khóa API của Anthropic bắt đầu bằng \"sk-ant-\"." });
        await SetAsync(ApiKeyKey, _protector.Protect(apiKey));
    }

    /// <summary>Khóa đã lưu (giải mã) hoặc biến môi trường ANTHROPIC_API_KEY trên máy chủ; null nếu chưa có.</summary>
    public async Task<string?> GetApiKeyAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var enc = await db.Settings.AsNoTracking().Where(s => s.Key == ApiKeyKey).Select(s => s.Value).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(enc))
        {
            try { return _protector.Unprotect(enc); }
            catch (System.Security.Cryptography.CryptographicException ex) { AppLog.Error(ex, "Giải mã khóa API AI"); }
        }
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    /// <summary>Mô tả khóa để hiển thị (chỉ 4 ký tự cuối).</summary>
    public async Task<string?> DescribeApiKeyAsync()
    {
        var k = await GetApiKeyAsync();
        return k == null ? null : $"sk-ant-…{k[^4..]}";
    }

    private async Task SetAsync(string key, string? value)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (value == null) { if (row != null) db.Settings.Remove(row); }
        else if (row == null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
    }
}

/// <summary>Nguồn được trích dẫn trong câu trả lời.</summary>
public sealed record ChatSource(int N, string Kind, string Title, string Url, string? Quote);

public sealed class ChatResult
{
    public int ConversationId { get; set; }
    public string Text { get; set; } = "";
    public List<ChatSource> Sources { get; set; } = new();
    /// <summary>Tài liệu nội bộ đã tra cứu và gửi kèm (kể cả không được trích dẫn).</summary>
    public List<ChatSource> Consulted { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// Hỏi đáp với Claude qua máy chủ (khóa API không ra trình duyệt): tra cứu nội bộ trước, gửi trích đoạn dưới dạng
/// tài liệu có bật trích dẫn, tùy chọn tìm web; trả lời truyền dần (streaming); lưu lịch sử và số token.
/// </summary>
public sealed class ChatService
{
    private readonly AiSettingsService _settings;
    private readonly ChatStore _store;
    private readonly KnowledgeService _knowledge;
    private readonly IClock _clock;

    public ChatService(AiSettingsService settings, ChatStore store, KnowledgeService knowledge, IClock clock)
    {
        _settings = settings;
        _store = store;
        _knowledge = knowledge;
        _clock = clock;
    }

    /// <summary>Địa chỉ API thay thế — chỉ dùng cho kiểm thử với máy chủ giả lập.</summary>
    public string? BaseUrlOverride { get; set; }

    public static readonly JsonSerializerOptions Json =new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Trợ lý có dùng được với vai trò này không (đã bật, đã có khóa, đủ vai trò).</summary>
    public async Task<(bool Available, string? Reason)> CheckAvailableAsync(UserRole role)
    {
        var s = await _settings.GetAsync();
        if (!s.Enabled) return (false, "Trợ lý AI đang tắt.");
        if (role < s.MinRole) return (false, "Vai trò của bạn chưa được dùng trợ lý AI.");
        if (await _settings.GetApiKeyAsync() == null) return (false, "Chưa cấu hình khóa API.");
        return (true, null);
    }

    public async Task<ChatResult> AskAsync(Actor user, int? conversationId, string question, Func<string, Task> onDelta,
        Func<string, Task> onStatus, CancellationToken ct)
    {
        var result = new ChatResult();
        question = question.Trim();
        if (question.Length == 0) { result.Error = "Câu hỏi trống."; return result; }
        if (question.Length > 4000) question = question[..4000];

        var s = await _settings.GetAsync();
        var (ok, reason) = await CheckAvailableAsync(user.Role);
        if (!ok) { result.Error = reason; return result; }
        if (await _store.TokensTodayAsync(user.UserId) >= s.DailyTokensPerUser)
        {
            result.Error = $"Bạn đã dùng hết hạn mức {s.DailyTokensPerUser:N0} token hôm nay. Thử lại vào ngày mai hoặc liên hệ quản trị.";
            return result;
        }

        var conv = conversationId is int cid
            ? (await _store.GetConversationsAsync(user.UserId, 200)).FirstOrDefault(c => c.Id == cid)
            : null;
        conv ??= await _store.CreateConversationAsync(user.UserId, question);
        result.ConversationId = conv.Id;
        var history = (await _store.GetMessagesAsync(user.UserId, conv.Id)).Where(m => m.Error == null).TakeLast(s.HistoryMessages).ToList();
        await _store.AddMessageAsync(user.UserId, conv.Id, new ChatMessage { Role = "user", Content = question });

        // ---- Tra cứu nội bộ (chỉ khi quản trị cho phép gửi trích đoạn lên Claude)
        var sources = new List<KnowledgeSource>();
        if (s.InternalSearch)
        {
            await onStatus("Đang tra cứu tài liệu nội bộ…");
            sources = await _knowledge.FindAsync(question);
            result.Consulted = sources.Select((x, i) => new ChatSource(i + 1, x.Kind, x.Title, x.Url, null)).ToList();
        }

        var messages = new List<BetaMessageParam>();
        foreach (var m in history)
        {
            // Lịch sử gửi lại dạng văn bản (giới hạn độ dài từng tin để kiểm soát token)
            var msgText = m.Content.Length > 4000 ? m.Content[..4000] + " …" : m.Content;
            messages.Add(new BetaMessageParam { Role = m.Role == "user" ? Role.User : Role.Assistant, Content = msgText });
        }
        // Lịch sử phải bắt đầu bằng tin của người dùng
        while (messages.Count > 0 && messages[0].Role == Role.Assistant) messages.RemoveAt(0);

        var content = new List<BetaContentBlockParam>();
        foreach (var src in sources)
        {
            content.Add(new BetaRequestDocumentBlock
            {
                Source = new BetaPlainTextSource { Data = src.Text },
                Title = src.Title,
                Context = src.Meta,
                Citations = new BetaCitationsConfigParam { Enabled = true },
            });
        }
        content.Add(new BetaTextBlockParam
        {
            Text = (sources.Count > 0 ? $"Các tài liệu nội bộ ở trên là trích đoạn tìm được trong phần mềm cho câu hỏi này.\n\n" : "") + question
        });
        messages.Add(new BetaMessageParam { Role = Role.User, Content = content });

        var tools = new List<BetaToolUnion>();
        if (s.WebSearch) tools.Add(new BetaWebSearchTool20260209 { MaxUses = s.WebSearchMaxUses });

        var p = new MessageCreateParams
        {
            Model = s.Model,
            MaxTokens = s.MaxOutputTokens,
            System = SystemPrompt(s, sources.Count > 0),
            Messages = messages,
            OutputConfig = new BetaOutputConfig { Effort = s.Effort switch { "low" => Effort.Low, "high" => Effort.High, _ => Effort.Medium } },
            Tools = tools.Count > 0 ? tools : null,
        };
        // Bị bộ lọc an toàn từ chối thì máy chủ Anthropic tự chuyển sang mô hình dự phòng (Haiku không hỗ trợ)
        if (s.Model != "claude-haiku-5-5")
        {
            p = p with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
        }

        var text = new StringBuilder();
        var cited = new Dictionary<string, ChatSource>();
        var blockMarks = new SortedSet<int>();
        long inTok = 0, outTok = 0;
        string? stop = null, model = s.Model;
        try
        {
            var key = await _settings.GetApiKeyAsync();
            var client = BaseUrlOverride == null ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = key, BaseUrl = BaseUrlOverride };
            await onStatus("Đang soạn câu trả lời…");
            await foreach (var ev in client.Beta.Messages.CreateStreaming(p, ct))
            {
                if (ev.TryPickStart(out var start))
                {
                    inTok = start.Message.Usage.InputTokens;
                    model = start.Message.Model;
                }
                else if (ev.TryPickContentBlockStart(out var cbs))
                {
                    if (cbs.ContentBlock.TryPickBetaServerToolUse(out _)) await onStatus("Đang tìm trên web…");
                    else if (cbs.ContentBlock.TryPickBetaText(out _)) await onStatus("");
                }
                else if (ev.TryPickContentBlockDelta(out var d))
                {
                    if (d.Delta.TryPickText(out var t))
                    {
                        text.Append(t.Text);
                        await onDelta(t.Text);
                    }
                    else if (d.Delta.TryPickCitations(out var c))
                    {
                        var src = MapCitation(c.Citation, sources);
                        if (src == null) continue;
                        if (!cited.TryGetValue(src.Url, out var known))
                        {
                            known = src with { N = cited.Count + 1 };
                            cited[src.Url] = known;
                        }
                        blockMarks.Add(known.N);
                    }
                }
                else if (ev.TryPickContentBlockStop(out _))
                {
                    if (blockMarks.Count > 0)
                    {
                        var marks = string.Concat(blockMarks.Select(n => $"[{n}]"));
                        text.Append(marks);
                        await onDelta(marks);
                        blockMarks.Clear();
                    }
                }
                else if (ev.TryPickDelta(out var md))
                {
                    outTok = md.Usage.OutputTokens;
                    if (md.Usage.InputTokens is long it && it > inTok) inTok = it;
                    stop = md.Delta.StopReason?.ToString();
                }
            }
            if (stop == "refusal" && text.Length == 0)
                result.Error = "Yêu cầu không được Claude chấp nhận theo chính sách an toàn. Hãy diễn đạt lại câu hỏi.";
            else if (stop == "max_tokens")
            {
                text.Append("\n\n(Câu trả lời bị cắt do đạt giới hạn độ dài.)");
                await onDelta("\n\n(Câu trả lời bị cắt do đạt giới hạn độ dài.)");
            }
        }
        catch (OperationCanceledException) { result.Error = "Đã dừng."; }
        catch (AnthropicUnauthorizedException) { result.Error = "Khóa API không hợp lệ hoặc đã bị thu hồi. Liên hệ quản trị."; }
        catch (AnthropicRateLimitException) { result.Error = "Claude đang quá tải hoặc vượt hạn mức gọi API. Thử lại sau ít phút."; }
        catch (AnthropicBadRequestException ex) { AppLog.Error(ex, "Gọi Claude API (400)"); result.Error = "Yêu cầu không hợp lệ: " + ex.Message; }
        catch (Anthropic5xxException) { result.Error = "Máy chủ Claude đang gặp sự cố. Thử lại sau."; }
        catch (AnthropicIOException) { result.Error = "Không kết nối được tới Claude API (kiểm tra Internet của máy chủ)."; }
        catch (AnthropicApiException ex) { AppLog.Error(ex, "Gọi Claude API"); result.Error = "Lỗi khi gọi Claude: " + ex.Message; }
        catch (Exception ex) { AppLog.Error(ex, "Đọc phản hồi Claude API"); result.Error = "Không đọc được phản hồi từ Claude. Chi tiết đã ghi vào nhật ký lỗi."; }

        result.Text = text.ToString();
        result.Sources = cited.Values.OrderBy(x => x.N).ToList();
        await _store.AddMessageAsync(user.UserId, conv.Id, new ChatMessage
        {
            Role = "assistant", Content = result.Text, Model = model, InputTokens = inTok, OutputTokens = outTok, Error = result.Error,
            SourcesJson = JsonSerializer.Serialize(new { cited = result.Sources, consulted = result.Consulted }, Json)
        });
        return result;
    }

    /// <summary>Đổi trích dẫn của Claude thành nguồn có đường dẫn: tài liệu nội bộ (theo thứ tự gửi) hoặc trang web.</summary>
    private static ChatSource? MapCitation(Citation c, List<KnowledgeSource> sources)
    {
        if (c.TryPickBetaCitationCharLocation(out var ch) && ch.DocumentIndex < sources.Count)
        {
            var s = sources[(int)ch.DocumentIndex];
            return new ChatSource(0, s.Kind, s.Title, s.Url, ch.CitedText);
        }
        if (c.TryPickBetaCitationsWebSearchResultLocation(out var w))
            return new ChatSource(0, "web", w.Title ?? w.Url, w.Url, w.CitedText);
        return null;
    }

    private string SystemPrompt(AiSettings s, bool hasDocs) =>
        $"""
        Bạn là trợ lý của phòng Khoa học Công nghệ và Chuyển đổi số, Tổng công ty Thiết bị điện Đông Anh (EEMC). Hôm nay là {_clock.Now:dd/MM/yyyy}.
        Trả lời bằng tiếng Việt, ngắn gọn, đúng trọng tâm, trình bày rõ ràng.
        {(hasDocs ? "Ưu tiên dựa vào các tài liệu nội bộ được gửi kèm và trích dẫn đúng đoạn đã dùng. Ghi rõ điều, khoản, điểm và số hiệu văn bản khi nêu quy định." : "Không có tài liệu nội bộ nào được gửi kèm cho câu hỏi này.")}
        {(s.WebSearch ? "Khi cần thông tin bên ngoài hoặc mới, có thể tìm trên web; ưu tiên nguồn chính thức (cổng thông tin Chính phủ, cơ sở dữ liệu văn bản pháp luật, trang của Bộ, ngành)." : "Không có công cụ tìm web.")}
        Không tự suy đoán số hiệu, ngày ban hành, tình trạng hiệu lực hay nội dung điều khoản. Nếu tài liệu không đủ để trả lời, nói rõ là chưa có căn cứ và gợi ý cần tra cứu thêm ở đâu.
        Tình trạng hiệu lực trong tài liệu nội bộ là do người dùng nhập; khi nêu, ghi kèm ngày kiểm tra nếu có và khuyên đối chiếu nguồn chính thức.
        """;
}
