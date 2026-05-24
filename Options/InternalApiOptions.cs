namespace AtendenteWhatssApp.Options;

public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";

    public string BaseUrl { get; set; } = "http://localhost:5100";

    public string ServiceKey { get; set; } = "";
}
