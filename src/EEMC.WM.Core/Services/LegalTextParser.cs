using System.Text;
using System.Text.RegularExpressions;
using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Services;

/// <summary>Căn cứ đã đọc: số điều, khoản, điểm (null nếu không ghi).</summary>
public readonly record struct LegalCitation(string? Article, string? Clause, string? Point);

/// <summary>Nội dung một điều: phần dẫn và các khoản.</summary>
public sealed class ArticleBody
{
    public List<string> Intro { get; } = new();
    public List<LegalClause> Clauses { get; } = new();
}

/// <summary>Khoản (Number rỗng: nhóm điểm đứng ngay sau điều, không có khoản).</summary>
public sealed class LegalClause
{
    public string Number { get; set; } = "";
    public List<string> Lines { get; } = new();
    public List<LegalSubPoint> Points { get; } = new();
}

public sealed class LegalSubPoint
{
    public string Letter { get; set; } = "";
    public List<string> Lines { get; } = new();
}

/// <summary>
/// Tách toàn văn văn bản pháp luật thành mục lục Phần / Chương / Mục / Điều theo cách đánh số của văn bản gốc.
/// Không sửa nội dung: mỗi dòng được giữ nguyên, chỉ chia vào đúng mục.
/// </summary>
public static class LegalTextParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex PartRx = new(@"^PHẦN\s+(THỨ\s+\p{L}+|[IVXLC]+|\d+)\b\s*[.:\-–]?\s*(.*)$", Opt);
    private static readonly Regex ChapterRx = new(@"^CHƯƠNG\s+([IVXLC]+|\d+)\b\s*[.:\-–]?\s*(.*)$", Opt);
    private static readonly Regex SectionRx = new(@"^MỤC\s+(\d+)\b\s*[.:\-–]?\s*(.*)$", Opt);
    private static readonly Regex ArticleRx = new(@"^ĐIỀU\s+(\d+[a-zđ]?)\s*[.:]\s*(.*)$", Opt);
    private static readonly Regex ClosingRx = new(@"^NƠI\s+NHẬN\s*:?", Opt);
    private static readonly Regex CitationArticleRx = new(@"Điều\s+(\d+[a-zđ]?)", Opt);

    public static List<LegalSection> Parse(string? fullText)
    {
        var result = new List<LegalSection>();
        if (string.IsNullOrWhiteSpace(fullText)) return result;

        var lines = NormalizeLines(fullText);
        LegalSection? current = null;
        var body = new StringBuilder();
        bool needHeading = false;

        void Flush()
        {
            if (current == null) return;
            current.Body = body.ToString().TrimEnd();
            if (current.Kind != LegalSectionKind.Preamble || current.Body.Length > 0 || current.Heading.Length > 0)
            {
                current.SortOrder = result.Count;
                result.Add(current);
            }
            body.Clear();
        }

        void Start(LegalSectionKind kind, string number, string heading)
        {
            Flush();
            current = new LegalSection { Kind = kind, Number = number.Trim(), Heading = heading.Trim() };
            // Tiêu đề Chương/Mục/Phần thường nằm ở dòng kế tiếp
            needHeading = kind != LegalSectionKind.Article && kind != LegalSectionKind.Preamble && current.Heading.Length == 0;
        }

        foreach (var line in lines)
        {
            Match m;
            if ((m = ArticleRx.Match(line)).Success) Start(LegalSectionKind.Article, m.Groups[1].Value, m.Groups[2].Value);
            else if ((m = ChapterRx.Match(line)).Success) Start(LegalSectionKind.Chapter, m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value);
            else if ((m = SectionRx.Match(line)).Success) Start(LegalSectionKind.Section, m.Groups[1].Value, m.Groups[2].Value);
            else if ((m = PartRx.Match(line)).Success) Start(LegalSectionKind.Part, m.Groups[1].Value.ToLowerInvariant().StartsWith("th") ? m.Groups[1].Value : m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value);
            else if (ClosingRx.IsMatch(line) && (current?.Kind == LegalSectionKind.Article || result.Any(s => s.Kind == LegalSectionKind.Article)))
            {
                Start(LegalSectionKind.Preamble, "", "Nơi nhận, chữ ký");
                body.AppendLine(line);
            }
            else
            {
                if (current == null) Start(LegalSectionKind.Preamble, "", "Phần mở đầu");
                if (needHeading && line.Length <= 300)
                {
                    current!.Heading = line;
                    needHeading = false;
                    continue;
                }
                needHeading = false;
                body.AppendLine(line);
            }
        }
        Flush();
        return result;
    }

    /// <summary>Chuẩn hóa Unicode dựng sẵn, bỏ dòng trống và khoảng trắng thừa.</summary>
    private static IEnumerable<string> NormalizeLines(string text)
    {
        text = text.Normalize(NormalizationForm.FormC).Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ');
        foreach (var raw in text.Split('\n'))
        {
            var line = Regex.Replace(raw, @"[ \t]+", " ").Trim();
            if (line.Length > 0) yield return line;
        }
    }

    // "Số: 13/2019/NĐ-CP", "Luật số: 93/2025/QH15", "Số 1234/QĐ-EEMC"
    private static readonly Regex NumberRx = new(@"^(?:\p{L}+\s+)?Số\s*:?\s*(\d+[/\-][\p{L}\d/\-]+)", Opt);
    private static readonly Regex DateRx = new(@"ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", Opt);
    private static readonly (string Word, LegalDocType Type)[] TypeWords =
    {
        ("BỘ LUẬT", LegalDocType.Law), ("LUẬT", LegalDocType.Law), ("NGHỊ QUYẾT", LegalDocType.Resolution), ("NGHỊ ĐỊNH", LegalDocType.Decree),
        ("THÔNG TƯ", LegalDocType.Circular), ("QUYẾT ĐỊNH", LegalDocType.Decision), ("QUY CHẾ", LegalDocType.Internal), ("QUY ĐỊNH", LegalDocType.Internal),
        ("TIÊU CHUẨN", LegalDocType.Standard), ("QUY CHUẨN", LegalDocType.Standard), ("HƯỚNG DẪN", LegalDocType.Guidance)
    };

    /// <summary>
    /// Đọc số hiệu, loại văn bản và ngày ban hành ghi ở phần đầu văn bản (chỉ đọc chữ có trong văn bản, không suy đoán).
    /// Kết quả để gợi ý điền sẵn; người nhập phải kiểm tra lại.
    /// </summary>
    public static (string? Number, LegalDocType? Type, DateTime? Issued) ReadHeader(string? fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText)) return (null, null, null);
        var head = NormalizeLines(fullText).Take(40).ToList();
        string? number = head.Select(l => NumberRx.Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value.TrimEnd('.', ',');
        LegalDocType? type = null;
        foreach (var l in head)
        {
            // Tên loại văn bản là một dòng viết hoa riêng: "NGHỊ ĐỊNH", "LUẬT", "THÔNG TƯ"...
            if (l.Length > 30 || l != l.ToUpperInvariant()) continue;
            var hit = TypeWords.FirstOrDefault(t => l == t.Word || l.StartsWith(t.Word + " "));
            if (hit.Word != null) { type = hit.Type; break; }
        }
        DateTime? issued = null;
        foreach (var l in head)
        {
            var m = DateRx.Match(l);
            if (!m.Success) continue;
            try { issued = new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); } catch (ArgumentOutOfRangeException) { }
            break;
        }
        return (number, type, issued);
    }

    /// <summary>Số điều trong chuỗi căn cứ: "Điều 5, khoản 1" → "5"; null nếu không có.</summary>
    public static string? ArticleOf(string? citation) => ParseCitation(citation).Article;

    // ---------------------------------------------------------------- Khoản, điểm và trích dẫn

    private static readonly Regex ClauseRx = new(@"^(\d+[a-z]?)\.\s+\S", RegexOptions.CultureInvariant);
    private static readonly Regex PointRx = new(@"^([a-zđ])\)\s+\S", RegexOptions.CultureInvariant);
    private static readonly Regex CiteClauseRx = new(@"khoản\s+(\d+[a-z]?)", Opt);
    private static readonly Regex CitePointRx = new(@"điểm\s+([a-zđ])(?![\p{L}\d])", Opt);

    /// <summary>
    /// Tách nội dung một điều thành phần dẫn, các khoản ("1.", "2."...) và điểm ("a)", "b)"...). Điểm đứng ngay sau điều
    /// (không có khoản) được gom vào khoản có số rỗng. Dòng tiếp theo không đánh số thuộc khoản/điểm đứng trước.
    /// </summary>
    public static ArticleBody SplitBody(string? body)
    {
        var r = new ArticleBody();
        LegalClause? clause = null;
        LegalSubPoint? point = null;
        foreach (var raw in (body ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            Match m;
            if ((m = ClauseRx.Match(line)).Success)
            {
                clause = new LegalClause { Number = m.Groups[1].Value };
                clause.Lines.Add(line);
                r.Clauses.Add(clause);
                point = null;
            }
            else if ((m = PointRx.Match(line)).Success)
            {
                if (clause == null) { clause = new LegalClause(); r.Clauses.Add(clause); }
                point = new LegalSubPoint { Letter = m.Groups[1].Value };
                point.Lines.Add(line);
                clause.Points.Add(point);
            }
            else if (point != null) point.Lines.Add(line);
            else if (clause != null) clause.Lines.Add(line);
            else r.Intro.Add(line);
        }
        return r;
    }

    /// <summary>Đọc điều, khoản, điểm trong căn cứ theo mọi thứ tự: "Điều 5, khoản 2, điểm a", "điểm a khoản 2 Điều 5".</summary>
    public static LegalCitation ParseCitation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        text = text.Normalize(NormalizationForm.FormC);
        string? G(Regex rx) { var m = rx.Match(text); return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null; }
        return new LegalCitation(G(CitationArticleRx), G(CiteClauseRx), G(CitePointRx));
    }

    /// <summary>Viết trích dẫn theo cách viết văn bản quy phạm: "điểm a khoản 2 Điều 5".</summary>
    public static string Cite(string article, string? clause = null, string? point = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(point)) parts.Add($"điểm {point}");
        if (!string.IsNullOrEmpty(clause)) parts.Add($"khoản {clause}");
        parts.Add($"Điều {article}");
        var s = string.Join(" ", parts);
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>Mã neo trên trang toàn văn: sec-{mục}[-k{khoản}][-{điểm}] (khoản rỗng → k0).</summary>
    public static string Anchor(int sortOrder, string? clause = null, string? point = null) =>
        $"sec-{sortOrder}" + (clause == null ? "" : $"-k{(clause.Length == 0 ? "0" : clause)}") + (point == null ? "" : $"-{point}");

    /// <summary>
    /// Tìm vị trí căn cứ trong toàn văn đã tách. Trả về mã neo sâu nhất tìm được và lỗi (null nếu căn cứ khớp đầy đủ).
    /// Không có điều nào trong toàn văn thì không kiểm tra (Anchor null, Error null).
    /// </summary>
    public static (string? Anchor, string? Error) Locate(IEnumerable<LegalSection> sections, LegalCitation c)
    {
        var articles = sections.Where(s => s.Kind == LegalSectionKind.Article).ToList();
        if (articles.Count == 0) return (null, null);
        if (c.Article == null) return (null, "chưa ghi số Điều");
        var a = articles.FirstOrDefault(s => s.Number.Equals(c.Article, StringComparison.OrdinalIgnoreCase));
        if (a == null) return (null, $"toàn văn không có Điều {c.Article}");
        if (c.Clause == null && c.Point == null) return (Anchor(a.SortOrder), null);
        var body = SplitBody(a.Body);
        var clause = c.Clause == null ? body.Clauses.FirstOrDefault(x => x.Number.Length == 0) : body.Clauses.FirstOrDefault(x => x.Number == c.Clause);
        if (clause == null)
            return (Anchor(a.SortOrder), c.Clause == null ? $"Điều {c.Article} không có điểm {c.Point} đứng riêng (cần ghi khoản)" : $"Điều {c.Article} không có khoản {c.Clause}");
        if (c.Point == null) return (Anchor(a.SortOrder, clause.Number), null);
        return clause.Points.Any(p => p.Letter == c.Point)
            ? (Anchor(a.SortOrder, clause.Number, c.Point), null)
            : (Anchor(a.SortOrder, clause.Number), $"{Cite(c.Article, c.Clause)} không có điểm {c.Point}");
    }

    /// <summary>Mức thụt lề khi hiển thị một dòng trong điều: 0 thường, 1 khoản "1.", 2 điểm "a)", 3 gạch đầu dòng.</summary>
    public static int Indent(string line)
    {
        if (Regex.IsMatch(line, @"^\d+[a-z]?\.\s")) return 1;
        if (Regex.IsMatch(line, @"^[a-zđ]\)\s", Opt)) return 2;
        if (line.StartsWith("- ") || line.StartsWith("+ ") || line.StartsWith("– ")) return 3;
        return 0;
    }

    /// <summary>Tên đầy đủ để hiển thị trong mục lục: "Chương II. Tên chương", "Điều 5. Tên điều".</summary>
    public static string Title(LegalSection s)
    {
        var prefix = s.Kind switch
        {
            LegalSectionKind.Part => $"Phần {s.Number}",
            LegalSectionKind.Chapter => $"Chương {s.Number}",
            LegalSectionKind.Section => $"Mục {s.Number}",
            LegalSectionKind.Article => $"Điều {s.Number}",
            _ => ""
        };
        if (prefix.Length == 0) return s.Heading;
        return s.Heading.Length == 0 ? prefix : $"{prefix}. {s.Heading}";
    }

    /// <summary>Số điều trong từng chương (dùng cho đồ họa cấu trúc). Điều không thuộc chương nào gom vào "Không thuộc chương".</summary>
    public static List<(string Chapter, int Articles)> ArticlesPerChapter(IEnumerable<LegalSection> sections)
    {
        var list = new List<(string, int)>();
        string? chapter = null;
        int count = 0;
        foreach (var s in sections.OrderBy(x => x.SortOrder))
        {
            if (s.Kind == LegalSectionKind.Chapter)
            {
                if (chapter != null || count > 0) list.Add((chapter ?? "Không thuộc chương", count));
                chapter = Title(s);
                count = 0;
            }
            else if (s.Kind == LegalSectionKind.Article) count++;
        }
        if (chapter != null || count > 0) list.Add((chapter ?? "Không thuộc chương", count));
        return list;
    }
}
