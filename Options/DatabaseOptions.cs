namespace AtendenteWhatssApp.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? ConnectionString { get; set; }
}
