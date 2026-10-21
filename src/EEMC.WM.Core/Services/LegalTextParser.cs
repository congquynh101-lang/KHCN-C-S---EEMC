using System.Text;
using System.Text.RegularExpressions;
using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Services;

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
        ("TIÊU CHUẨN", LegalDocType.Standard), ("QUY CHUẨN", LegalDocType.Standard)
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
    public static string? ArticleOf(string? citation)
    {
        if (string.IsNullOrWhiteSpace(citation)) return null;
        var m = CitationArticleRx.Match(citation);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
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
