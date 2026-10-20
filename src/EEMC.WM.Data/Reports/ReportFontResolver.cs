using PdfSharp.Fonts;

namespace EEMC.WM.Data.Reports;

/// <summary>
/// Nạp phông chữ cho PDF (hỗ trợ tiếng Việt). Tìm Times New Roman/Arial trong thư mục Fonts của Windows;
/// trên máy chủ Linux có thể chép tệp .ttf vào thư mục "fonts" cạnh chương trình.
/// </summary>
public sealed class ReportFontResolver : IFontResolver
{
    private static readonly string[] Dirs =
    {
        Path.Combine(AppContext.BaseDirectory, "fonts"),
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "/usr/share/fonts/truetype/msttcorefonts",
        "/usr/share/fonts/truetype/dejavu",
    };

    private static readonly Dictionary<string, string[]> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["regular"] = new[] { "times.ttf", "Times New Roman.ttf", "arial.ttf", "DejaVuSerif.ttf" },
        ["bold"] = new[] { "timesbd.ttf", "Times New Roman Bold.ttf", "arialbd.ttf", "DejaVuSerif-Bold.ttf" },
        ["italic"] = new[] { "timesi.ttf", "Times New Roman Italic.ttf", "ariali.ttf", "DejaVuSerif-Italic.ttf" },
        ["bolditalic"] = new[] { "timesbi.ttf", "Times New Roman Bold Italic.ttf", "arialbi.ttf", "DejaVuSerif-BoldItalic.ttf" },
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new((bold, italic) switch
        {
            (true, true) => "bolditalic",
            (true, false) => "bold",
            (false, true) => "italic",
            _ => "regular"
        });

    public byte[]? GetFont(string faceName)
    {
        if (!Files.TryGetValue(faceName, out var names)) names = Files["regular"];
        foreach (var dir in Dirs.Where(Directory.Exists))
            foreach (var n in names)
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return File.ReadAllBytes(p);
            }
        throw new FileNotFoundException("Không tìm thấy phông chữ Times New Roman/Arial để tạo PDF. Hãy chép times.ttf, timesbd.ttf, timesi.ttf, timesbi.ttf vào thư mục 'fonts' cạnh chương trình.");
    }
}
