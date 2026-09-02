using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Print2Md.Core;

internal static class MarkdownEscaping
{
    private static readonly Regex UrlRegex = new Regex(@"^(?:https?|mailto):[^\s<>]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Inline(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                case '`':
                case '*':
                case '_':
                case '{':
                case '}':
                case '[':
                case ']':
                case '<':
                case '>':
                    builder.Append('\\');
                    break;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string TableCell(string value) => Inline(value).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    public static string LinkDestination(string value)
    {
        return value
            .Replace("\\", "/")
            .Replace(" ", "%20")
            .Replace("(", "%28")
            .Replace(")", "%29");
    }

    public static bool IsUrl(string value) => UrlRegex.IsMatch(value.Trim());
}

