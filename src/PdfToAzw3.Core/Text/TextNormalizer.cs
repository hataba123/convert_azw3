using System.Text;
using System.Text.RegularExpressions;

namespace PdfToAzw3.Core.Text;

public static partial class TextNormalizer
{
    private static readonly char[] ZeroWidthCharacters = ['\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF'];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormC)
            .Replace('\u00AD'.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace("ﬁ", "fi", StringComparison.Ordinal)
            .Replace("ﬂ", "fl", StringComparison.Ordinal);

        normalized = normalized.Trim(ZeroWidthCharacters.Concat([' ', '\t', '\r', '\n']).ToArray());
        normalized = MultipleWhitespaceRegex().Replace(normalized, " ");
        return normalized.Normalize(NormalizationForm.FormC);
    }

    public static string JoinLines(IReadOnlyList<string> lines, bool repairHyphenatedWords = true)
    {
        var result = new StringBuilder();

        for (var index = 0; index < lines.Count; index++)
        {
            var current = Normalize(lines[index]);
            if (current.Length == 0)
            {
                continue;
            }

            if (result.Length > 0)
            {
                var previous = result.ToString();
                if (repairHyphenatedWords && previous.EndsWith("-", StringComparison.Ordinal) && IsWordHyphen(previous))
                {
                    result.Length--;
                }
                else
                {
                    result.Append(' ');
                }
            }

            result.Append(current);
        }

        return Normalize(result.ToString());
    }

    public static string RepairHyphenatedLineBreak(string previousLine, string nextLine)
    {
        var previous = Normalize(previousLine);
        var next = Normalize(nextLine);
        if (previous.EndsWith("-", StringComparison.Ordinal) && IsWordHyphen(previous))
        {
            return Normalize(previous[..^1] + next);
        }

        return Normalize($"{previous} {next}");
    }

    private static bool IsWordHyphen(string value)
    {
        if (value.Length < 2 || !char.IsLetter(value[^2]))
        {
            return false;
        }

        return value.Length < 3 || char.IsLetter(value[^3]);
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleWhitespaceRegex();
}
