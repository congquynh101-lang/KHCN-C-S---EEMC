using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace EEMC.WM.Data.Infrastructure;

/// <summary>Đọc chữ từ PDF (có lớp chữ), DOCX, TXT. Không nhận dạng ảnh quét (OCR).</summary>
public static class TextExtractor
{
    public static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt" };

    public static string Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = ext switch
        {
            ".pdf" => ReadPdf(path),
            ".docx" => ReadDocx(path),
            ".txt" => File.ReadAllText(path),
            _ => ""
        };
        return text.Normalize(NormalizationForm.FormC).Trim();
    }

    private static string ReadPdf(string path)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(path);
        foreach (var page in pdf.GetPages())
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        return sb.ToString();
    }

    private static string ReadDocx(string path)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";
        foreach (var p in body.Descendants<W.Paragraph>())
        {
            foreach (var el in p.Descendants())
            {
                if (el is W.Text t) sb.Append(t.Text);
                else if (el is W.TabChar) sb.Append(' ');
                else if (el is W.Break) sb.Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
