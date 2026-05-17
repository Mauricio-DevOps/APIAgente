using System.ComponentModel.DataAnnotations;

namespace AtendenteWhatssApp.Options;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; init; } = string.Empty;

    [Required]
    public string AuthToken { get; init; } = string.Empty;

    public int MaxSendAttempts { get; init; } = 3;
}
