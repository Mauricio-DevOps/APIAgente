namespace AtendenteWhatssApp.Services;

public static class PhoneNumberNormalizer
{
    private const string BrazilCountryCode = "55";
    private const string WhatsappPrefix = "whatsapp:";

    public static string ToBrazilNationalPhone(string? value)
    {
        var digits = NormalizeDigits(value);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return string.Empty;
        }

        if ((digits.Length == 12 || digits.Length == 13) &&
            digits.StartsWith(BrazilCountryCode, StringComparison.Ordinal))
        {
            return digits[BrazilCountryCode.Length..];
        }

        if ((digits.Length == 11 || digits.Length == 12) && digits[0] == '0')
        {
            return digits[1..];
        }

        return digits;
    }

    public static IReadOnlySet<string> GetLookupKeys(string? value)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddPhoneVariants(keys, NormalizeDigits(value));
        AddPhoneVariants(keys, ToBrazilNationalPhone(value));
        return keys;
    }

    public static IReadOnlyList<string> GetStorageCandidates(string? value)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            values.Add(trimmed);
        }

        foreach (var key in GetLookupKeys(value))
        {
            values.Add(key);

            if (IsBrazilNationalPhone(key))
            {
                values.Add($"{BrazilCountryCode}{key}");
                values.Add($"+{BrazilCountryCode}{key}");
                values.Add($"{WhatsappPrefix}+{BrazilCountryCode}{key}");
            }
        }

        return values.ToArray();
    }

    public static string ToWhatsappAddress(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith(WhatsappPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var nationalPhone = ToBrazilNationalPhone(trimmed);
        if (!string.IsNullOrWhiteSpace(nationalPhone))
        {
            return $"{WhatsappPrefix}+{BrazilCountryCode}{nationalPhone}";
        }

        var digits = NormalizeDigits(trimmed);
        return string.IsNullOrWhiteSpace(digits)
            ? trimmed
            : $"{WhatsappPrefix}+{digits}";
    }

    public static string NormalizeDigits(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsDigit).ToArray());
    }

    private static void AddPhoneVariants(HashSet<string> keys, string digits)
    {
        if (string.IsNullOrWhiteSpace(digits))
        {
            return;
        }

        keys.Add(digits);

        var nationalPhone = ToBrazilNationalPhone(digits);
        if (string.IsNullOrWhiteSpace(nationalPhone))
        {
            return;
        }

        keys.Add(nationalPhone);
        keys.Add($"{BrazilCountryCode}{nationalPhone}");

        if (nationalPhone.Length == 11 && nationalPhone[2] == '9')
        {
            var withoutNinthDigit = string.Concat(nationalPhone.AsSpan(0, 2), nationalPhone.AsSpan(3));
            keys.Add(withoutNinthDigit);
            keys.Add($"{BrazilCountryCode}{withoutNinthDigit}");
        }
        else if (nationalPhone.Length == 10)
        {
            var withNinthDigit = string.Concat(nationalPhone.AsSpan(0, 2), "9", nationalPhone.AsSpan(2));
            keys.Add(withNinthDigit);
            keys.Add($"{BrazilCountryCode}{withNinthDigit}");
        }
    }

    private static bool IsBrazilNationalPhone(string value)
    {
        return value.Length is 10 or 11;
    }
}
