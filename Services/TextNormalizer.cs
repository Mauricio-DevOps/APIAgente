using System.Globalization;
using System.Text;

namespace AtendenteWhatssApp.Services;

internal static class TextNormalizer
{
    public static string NormalizeForLookup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasWhiteSpace = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhiteSpace)
                {
                    builder.Append(' ');
                    previousWasWhiteSpace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
    }
}
