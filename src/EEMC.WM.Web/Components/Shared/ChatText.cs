using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EEMC.WM.Web.Services;
using Microsoft.AspNetCore.Components;

namespace EEMC.WM.Web.Components.Shared;

/// <summary>
/// Hiển thị câu trả lời của trợ lý: mã hóa HTML trước (chống chèn mã), rồi định dạng tối thiểu kiểu Markdown
/// (tiêu đề, in đậm, gạch đầu dòng) và đổi dấu [n] thành liên kết tới nguồn trích dẫn.
/// </summary>
public static class ChatText
{
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex Mark = new(@"\[(\d{1,2})\]", RegexOptions.Compiled);

    public static MarkupString Render(string text, IReadOnlyList<ChatSource> sources)
    {
        var sb = new StringBuilder();
        bool inList = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = WebUtility.HtmlEncode(raw.TrimEnd());
            line = Bold.Replace(line, "<strong>$1</strong>");
            line = Mark.Replace(line, m =>
            {
                var n = int.Parse(m.Groups[1].Value);
                var s = sources.FirstOrDefault(x => x.N == n);
                if (s == null) return m.Value;
                var external = s.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                return $"<a class=\"ai-cite\" href=\"{WebUtility.HtmlEncode(s.Url)}\" title=\"{WebUtility.HtmlEncode(s.Title)}\"{(external ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "")}>{n}</a>";
            });
            var trimmed = line.TrimStart();
            bool bullet = trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ");
            if (bullet && !inList) { sb.Append("<ul>"); inList = true; }
            if (!bullet && inList) { sb.Append("</ul>"); inList = false; }
            if (bullet) sb.Append("<li>").Append(trimmed[2..]).Append("</li>");
            else if (trimmed.StartsWith("#")) sb.Append("<p><strong>").Append(trimmed.TrimStart('#', ' ')).Append("</strong></p>");
            else if (trimmed.Length == 0) sb.Append("<div class=\"ai-gap\"></div>");
            else sb.Append("<p>").Append(line).Append("</p>");
        }
        if (inList) sb.Append("</ul>");
        return new MarkupString(sb.ToString());
    }
}
