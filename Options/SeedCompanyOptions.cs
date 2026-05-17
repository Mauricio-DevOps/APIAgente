namespace AtendenteWhatssApp.Options;

public sealed class SeedCompanyOptions
{
    public const string SectionName = "SeedCompany";

    public string CompanyName { get; set; } = "Empresa Demo";

    public string CompanyPhone { get; set; } = "whatsapp:+5500000000000";

    public string InitialPassword { get; set; } = "123456";

    public List<SeedCompanyEntry> AdditionalCompanies { get; set; } = new();
}

public sealed class SeedCompanyEntry
{
    public string CompanyName { get; set; } = string.Empty;

    public string CompanyPhone { get; set; } = string.Empty;

    public string InitialPassword { get; set; } = string.Empty;
}
