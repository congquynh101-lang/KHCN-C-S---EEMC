using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace EEMC.WM.Data.Reports;

/// <summary>
/// Nạp phông chữ cho PDF (hỗ trợ tiếng Việt). Tìm Times New Roman/Arial trong thư mục Fonts của Windows;
/// trên máy chủ Linux dùng Liberation Serif hoặc DejaVu Serif, hoặc chép tệp .ttf vào thư mục "fonts" cạnh chương trình.
/// Thiếu tệp nghiêng/đậm thì dùng bản thường và để PDFsharp giả lập kiểu chữ.
/// </summary>
public sealed class ReportFontResolver : IFontResolver
{
    private static readonly string[] Dirs =
    {
        Path.Combine(AppContext.BaseDirectory, "fonts"),
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "/usr/share/fonts/truetype/msttcorefonts",
        "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/truetype/liberation2",
        "/usr/share/fonts/truetype/dejavu",
    };

    private static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["regular"] = new[] { "times.ttf", "Times New Roman.ttf", "arial.ttf", "LiberationSerif-Regular.ttf", "DejaVuSerif.ttf" },
        ["bold"] = new[] { "timesbd.ttf", "Times New Roman Bold.ttf", "arialbd.ttf", "LiberationSerif-Bold.ttf", "DejaVuSerif-Bold.ttf" },
        ["italic"] = new[] { "timesi.ttf", "Times New Roman Italic.ttf", "ariali.ttf", "LiberationSerif-Italic.ttf", "DejaVuSerif-Italic.ttf" },
        ["bolditalic"] = new[] { "timesbi.ttf", "Times New Roman Bold Italic.ttf", "arialbi.ttf", "LiberationSerif-BoldItalic.ttf", "DejaVuSerif-BoldItalic.ttf" },
    };

    private static readonly ConcurrentDictionary<string, string?> Found = new(StringComparer.OrdinalIgnoreCase);

    private static string? Find(string face) => Found.GetOrAdd(face, f =>
    {
        foreach (var dir in Dirs.Where(d => d.Length > 0 && Directory.Exists(d)))
            foreach (var n in Files[f])
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
        return null;
    });

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular"
        };
        if (Find(face) != null) return new FontResolverInfo(face);
        if (bold && Find("bold") != null) return new FontResolverInfo("bold", false, italic);
        if (Find("regular") != null) return new FontResolverInfo("regular", bold, italic);
        throw MissingFonts();
    }

    public byte[]? GetFont(string faceName) =>
        Find(Files.ContainsKey(faceName) ? faceName : "regular") is { } p ? File.ReadAllBytes(p) : throw MissingFonts();

    private static FileNotFoundException MissingFonts() =>
        new("Không tìm thấy phông chữ Times New Roman/Arial để tạo PDF. Hãy chép times.ttf, timesbd.ttf, timesi.ttf, timesbi.ttf vào thư mục 'fonts' cạnh chương trình.");
}
