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

    /// <summary>
    /// Bỏ dấu, chữ thường, giữ nguyên độ dài chuỗi (mỗi ký tự thay bằng một ký tự) để vị trí tìm được
    /// trên chuỗi đã bỏ dấu dùng được để cắt trích đoạn trên chuỗi gốc.
    /// </summary>
    public static string Fold(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            var c = char.ToLowerInvariant(s[i]);
            if (c == 'đ') c = 'd';
            else if (c > 127 && char.IsLetter(c))
            {
                var d = c.ToString().Normalize(NormalizationForm.FormD);
                if (d.Length > 0 && d[0] < 128) c = d[0];
            }
            chars[i] = char.IsWhiteSpace(c) ? ' ' : c;
        }
        return new string(chars);
    }
}
