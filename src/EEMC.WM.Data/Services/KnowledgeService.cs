using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Một trích đoạn tài liệu nội bộ đưa cho trợ lý AI, kèm đường dẫn để người dùng mở đúng nguồn.</summary>
public sealed record KnowledgeSource(string Kind, string Title, string Url, string Text, string? Meta);

/// <summary>
/// Tìm trích đoạn nội bộ liên quan câu hỏi: điều của văn bản pháp luật, bản rút gọn ĐÃ DUYỆT, nội dung hồ sơ tài liệu.
/// Chỉ dùng dữ liệu đang hiển thị trong phần mềm (không lấy tài liệu trong thùng rác, bản rút gọn chưa duyệt).
/// Giới hạn số lượng và độ dài để kiểm soát token.
/// </summary>
public sealed class KnowledgeService
{
    public const int MaxSources = 8;
    public const int MaxCharsPerSource = 2500;
    public const int MaxTotalChars = 16000;

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "la", "cua", "va", "cac", "nhung", "cho", "voi", "trong", "theo", "duoc", "co", "khong", "nao", "gi", "the", "nhu",
        "ve", "tu", "den", "mot", "nay", "do", "thi", "ma", "hay", "hoac", "neu", "khi", "de", "tai", "ra", "vao", "con",
        "se", "da", "dang", "bi", "phai", "can", "nhieu", "it", "tat", "ca", "moi", "bao", "sao", "lam", "hoi", "toi", "ban",
        "chung", "oi", "nhe", "giup", "xin", "vui", "long", "biet", "minh", "ai", "o", "nhat", "hon", "rang", "vay", "a", "u"
    };

    private readonly IDbContextFactory<AppDbContext> _factory;

    public KnowledgeService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    /// <summary>Từ khóa (không dấu) của câu hỏi, bỏ từ hư; tối đa 10 từ.</summary>
    public static List<string> Keywords(string question) =>
        TextNormalizer.Normalize(question)
            .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '(', ')', '"', '\'', '“', '”' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2 && !Stop.Contains(w))
            .Distinct().Take(10).ToList();

    public async Task<List<KnowledgeSource>> FindAsync(string question)
    {
        var terms = Keywords(question);
        if (terms.Count == 0) return new();
        int need = terms.Count <= 2 ? 1 : (int)Math.Ceiling(terms.Count / 2.0);
        await using var db = await _factory.CreateDbContextAsync();
        var scored = new List<(double Score, KnowledgeSource Source)>();

        // ---- Văn bản pháp luật: văn bản có từ khóa → chấm điểm từng điều
        var docHits = new Dictionary<int, int>();
        foreach (var t in terms)
            foreach (var id in await db.LegalDocuments.Where(d => d.SearchText.Contains(t)).Select(d => d.Id).ToListAsync())
                docHits[id] = docHits.GetValueOrDefault(id) + 1;
        var topDocs = docHits.Where(x => x.Value >= need).OrderByDescending(x => x.Value).Take(10).Select(x => x.Key).ToList();
        var docs = await db.LegalDocuments.AsNoTracking().Where(d => topDocs.Contains(d.Id)).ToListAsync();
        foreach (var d in docs)
        {
            var name = string.IsNullOrEmpty(d.Number) ? d.Title : $"{Labels.Of(d.DocType)} số {d.Number}";
            var meta = $"{Labels.Of(d.DocType)} {d.Number} \"{d.Title}\"; cơ quan ban hành: {d.Issuer ?? "chưa nhập"}; " +
                       $"tình trạng hiệu lực theo dữ liệu đã nhập: {Labels.Of(d.Effect)}" +
                       (d.EffectCheckedAt.HasValue ? $" (kiểm tra ngày {d.EffectCheckedAt:dd/MM/yyyy})" : "") +
                       (d.EffectiveDate.HasValue ? $"; có hiệu lực từ {d.EffectiveDate:dd/MM/yyyy}" : "");
            var articles = await db.LegalSections.AsNoTracking().Where(s => s.DocumentId == d.Id && s.Kind == LegalSectionKind.Article).ToListAsync();
            foreach (var a in articles)
            {
                var hay = TextNormalizer.Fold(a.Heading + " " + a.Body);
                var head = TextNormalizer.Fold(a.Heading);
                int hit = terms.Count(hay.Contains);
                if (hit < need) continue;
                double score = hit + terms.Count(head.Contains) * 0.5;
                scored.Add((score, new KnowledgeSource("legal", $"{LegalTextParser.Title(a)} – {name}",
                    $"/legal/{d.Id}?tab=full&sec={a.SortOrder}", Cut(LegalTextParser.Title(a) + "\n" + a.Body), meta)));
            }
            // Bản rút gọn chỉ dùng khi đã được duyệt
            if (d.SummaryState == SummaryState.Approved)
            {
                var points = await db.LegalPoints.AsNoTracking().Where(p => p.DocumentId == d.Id).OrderBy(p => p.Kind).ThenBy(p => p.SortOrder).ToListAsync();
                var text = string.Join("\n", new[] { d.SummaryAbout, d.SummaryApplies }.Where(x => !string.IsNullOrWhiteSpace(x))
                    .Concat(points.Select(p => $"- {p.Text}{(string.IsNullOrEmpty(p.Citation) ? "" : $" (căn cứ: {p.Citation})")}{(string.IsNullOrEmpty(p.Due) ? "" : $"; thời hạn: {p.Due}")}")));
                if (text.Length > 0)
                    scored.Add((docHits[d.Id] + 0.25, new KnowledgeSource("summary", $"Bản rút gọn đã duyệt – {name}", $"/legal/{d.Id}?tab=short",
                        Cut(text), meta + $"; bản rút gọn do {d.SummaryAuthor} lập, {d.SummaryApprovedBy} duyệt ngày {d.SummaryApprovedAt:dd/MM/yyyy}")));
            }
        }

        // ---- Hồ sơ tài liệu (không lấy tài liệu trong thùng rác)
        var fileHits = new Dictionary<int, int>();
        foreach (var t in terms)
            foreach (var id in await db.Attachments.Where(a => !a.IsDeleted && a.SearchText.Contains(t)).Select(a => a.Id).Take(200).ToListAsync())
                fileHits[id] = fileHits.GetValueOrDefault(id) + 1;
        var topFiles = fileHits.Where(x => x.Value >= need).OrderByDescending(x => x.Value).Take(4).Select(x => x.Key).ToList();
        foreach (var f in await db.Attachments.AsNoTracking().Where(a => topFiles.Contains(a.Id)).ToListAsync())
        {
            var body = string.IsNullOrEmpty(f.ExtractedText) ? (f.Link ?? "") : BestWindow(f.ExtractedText, terms);
            scored.Add((fileHits[f.Id] - 0.1, new KnowledgeSource("file", f.Name,
                f.StoredFile != null ? $"/files/{f.Id}?inline=true" : (f.Link ?? $"/documents"), Cut(body),
                $"Tài liệu hồ sơ: {Labels.Of(f.Kind)}{(string.IsNullOrEmpty(f.DocumentNumber) ? "" : ", số " + f.DocumentNumber)}; cập nhật {f.UpdatedAt:dd/MM/yyyy}")));
        }

        var result = new List<KnowledgeSource>();
        int total = 0;
        foreach (var (_, s) in scored.OrderByDescending(x => x.Score))
        {
            if (result.Count >= MaxSources || total + s.Text.Length > MaxTotalChars) break;
            result.Add(s);
            total += s.Text.Length;
        }
        return result;
    }

    private static string Cut(string s) => s.Length <= MaxCharsPerSource ? s : s[..MaxCharsPerSource] + " …";

    /// <summary>Đoạn văn dài khoảng MaxCharsPerSource chứa nhiều từ khóa nhất.</summary>
    private static string BestWindow(string text, List<string> terms)
    {
        if (text.Length <= MaxCharsPerSource) return text;
        var folded = TextNormalizer.Fold(text);
        int best = 0, bestHits = -1;
        foreach (var t in terms)
        {
            for (int i = folded.IndexOf(t, StringComparison.Ordinal); i >= 0 && i < folded.Length; i = folded.IndexOf(t, i + 1, StringComparison.Ordinal))
            {
                int start = Math.Max(0, i - MaxCharsPerSource / 4);
                int len = Math.Min(MaxCharsPerSource, folded.Length - start);
                var window = folded.Substring(start, len);
                int hits = terms.Count(window.Contains);
                if (hits > bestHits) { bestHits = hits; best = start; }
                if (bestHits == terms.Count) break;
            }
        }
        return (best > 0 ? "… " : "") + text.Substring(best, Math.Min(MaxCharsPerSource, text.Length - best));
    }
}
