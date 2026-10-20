using System.Globalization;
using System.Text;

namespace EEMC.WM.Core.Text;

/// <summary>Chuẩn hóa tiếng Việt để tìm kiếm không phân biệt hoa/thường và dấu.</summary>
public static class TextNormalizer
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var lower = s.Trim().ToLowerInvariant().Replace('đ', 'd');
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool lastSpace = false;
        foreach (var c in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            lastSpace = false;
            sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
