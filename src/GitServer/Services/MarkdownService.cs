using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace GitServer.Services;

public class MarkdownService
{
    private static readonly Regex _heading = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
    private static readonly Regex _bold = new(@"\*\*(.+?)\*\*");
    private static readonly Regex _italic = new(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)");
    private static readonly Regex _inlineCode = new(@"`([^`]+)`");
    private static readonly Regex _link = new(@"\[([^\]]+)\]\((https?://[^\)]+)\)");
    private static readonly Regex _hr = new(@"^-{3,}$", RegexOptions.Multiline);

    public string Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Fenced code block
            if (line.StartsWith("```"))
            {
                var lang = HttpUtility.HtmlEncode(line[3..].Trim());
                sb.Append($"<pre><code class=\"lang-{lang}\">");
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```"))
                {
                    sb.AppendLine(HttpUtility.HtmlEncode(lines[i]));
                    i++;
                }
                sb.AppendLine("</code></pre>");
                i++;
                continue;
            }

            // Blockquote
            if (line.StartsWith("> "))
            {
                sb.Append("<blockquote><p>");
                while (i < lines.Length && lines[i].StartsWith("> "))
                {
                    sb.Append(RenderInline(lines[i][2..]) + " ");
                    i++;
                }
                sb.AppendLine("</p></blockquote>");
                continue;
            }

            // Unordered list
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                sb.AppendLine("<ul>");
                while (i < lines.Length && (lines[i].StartsWith("- ") || lines[i].StartsWith("* ")))
                {
                    sb.AppendLine($"<li>{RenderInline(lines[i][2..])}</li>");
                    i++;
                }
                sb.AppendLine("</ul>");
                continue;
            }

            // Ordered list
            if (Regex.IsMatch(line, @"^\d+\. "))
            {
                sb.AppendLine("<ol>");
                while (i < lines.Length && Regex.IsMatch(lines[i], @"^\d+\. "))
                {
                    var content = Regex.Replace(lines[i], @"^\d+\. ", "");
                    sb.AppendLine($"<li>{RenderInline(content)}</li>");
                    i++;
                }
                sb.AppendLine("</ol>");
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line, @"^-{3,}$") || Regex.IsMatch(line, @"^\*{3,}$"))
            {
                sb.AppendLine("<hr>");
                i++;
                continue;
            }

            // Headings
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = RenderInline(headingMatch.Groups[2].Value);
                var id = Regex.Replace(headingMatch.Groups[2].Value.ToLower(), @"[^\w]+", "-");
                sb.AppendLine($"<h{level} id=\"{id}\">{text}</h{level}>");
                i++;
                continue;
            }

            // Empty line = paragraph separator
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                // Collect paragraph
                if (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    // skip, next iteration handles it
                }
                continue;
            }

            // Paragraph
            var para = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])
                && !lines[i].StartsWith("#")
                && !lines[i].StartsWith("```")
                && !lines[i].StartsWith("> ")
                && !lines[i].StartsWith("- ")
                && !lines[i].StartsWith("* ")
                && !Regex.IsMatch(lines[i], @"^\d+\. ")
                && !Regex.IsMatch(lines[i], @"^-{3,}$"))
            {
                para.Append(RenderInline(lines[i]) + " ");
                i++;
            }
            if (para.Length > 0)
                sb.AppendLine($"<p>{para.ToString().TrimEnd()}</p>");
        }

        return sb.ToString();
    }

    private string RenderInline(string text)
    {
        text = HttpUtility.HtmlEncode(text);

        // Links (must be done before other patterns)
        text = Regex.Replace(text, @"\[([^\]]+)\]\((https?://[^\)]+)\)", m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            return $"<a href=\"{HttpUtility.HtmlAttributeEncode(url)}\" rel=\"nofollow noopener\">{label}</a>";
        });

        // Bold
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"__(.+?)__", "<strong>$1</strong>");

        // Italic
        text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");
        text = Regex.Replace(text, @"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", "<em>$1</em>");

        // Inline code
        text = Regex.Replace(text, @"`([^`]+)`", m =>
            $"<code>{HttpUtility.HtmlEncode(HttpUtility.HtmlDecode(m.Groups[1].Value))}</code>");

        return text;
    }
}
