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

            // Raw HTML block (line starts with a block-level HTML tag)
            if (Regex.IsMatch(line.TrimStart(), @"^</?[a-zA-Z][\w-]*(\s[^>]*)?>"))
            {
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    sb.AppendLine(lines[i]);
                    i++;
                }
                continue;
            }

            // Table (header row + separator row like |---|---|)
            if (IsTableRow(line) && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var headerCells = SplitTableRow(line);
                var aligns = ParseAligns(lines[i + 1]);
                sb.AppendLine("<table>");
                sb.Append("<thead><tr>");
                for (var c = 0; c < headerCells.Count; c++)
                {
                    var align = c < aligns.Count ? aligns[c] : null;
                    var style = align != null ? $" style=\"text-align:{align}\"" : "";
                    sb.Append($"<th{style}>{RenderInline(headerCells[c])}</th>");
                }
                sb.AppendLine("</tr></thead>");
                i += 2;
                sb.AppendLine("<tbody>");
                while (i < lines.Length && IsTableRow(lines[i]))
                {
                    var cells = SplitTableRow(lines[i]);
                    sb.Append("<tr>");
                    for (var c = 0; c < cells.Count; c++)
                    {
                        var align = c < aligns.Count ? aligns[c] : null;
                        var style = align != null ? $" style=\"text-align:{align}\"" : "";
                        sb.Append($"<td{style}>{RenderInline(cells[c])}</td>");
                    }
                    sb.AppendLine("</tr>");
                    i++;
                }
                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");
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
                && !Regex.IsMatch(lines[i], @"^-{3,}$")
                && !(IsTableRow(lines[i]) && i + 1 < lines.Length && IsTableSeparator(lines[i + 1])))
            {
                para.Append(RenderInline(lines[i]) + " ");
                i++;
            }
            if (para.Length > 0)
                sb.AppendLine($"<p>{para.ToString().TrimEnd()}</p>");
        }

        return sb.ToString();
    }

    private static bool IsTableRow(string line)
    {
        line = line.Trim();
        return line.StartsWith("|") || (line.Contains('|') && !string.IsNullOrWhiteSpace(line));
    }

    private static bool IsTableSeparator(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return false;
        if (!Regex.IsMatch(line, @"^\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?$")) return false;
        return line.Contains('-');
    }

    private static List<string> ParseAligns(string separatorLine)
    {
        var cells = SplitTableRow(separatorLine);
        var result = new List<string>();
        foreach (var cell in cells)
        {
            var c = cell.Trim();
            var left = c.StartsWith(":");
            var right = c.EndsWith(":");
            if (left && right) result.Add("center");
            else if (right) result.Add("right");
            else if (left) result.Add("left");
            else result.Add(null!);
        }
        return result;
    }

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|")) trimmed = trimmed[1..];
        if (trimmed.EndsWith("|")) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var current = new StringBuilder();
        for (var idx = 0; idx < trimmed.Length; idx++)
        {
            var ch = trimmed[idx];
            if (ch == '\\' && idx + 1 < trimmed.Length && trimmed[idx + 1] == '|')
            {
                current.Append('|');
                idx++;
            }
            else if (ch == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        cells.Add(current.ToString().Trim());
        return cells;
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
