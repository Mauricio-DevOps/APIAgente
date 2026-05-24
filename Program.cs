using AtendenteWhatssApp.Extensions;
using AtendenteWhatssApp.Options;
using AtendenteWhatssApp.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
AddLocalSupabaseEnvironment(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddControllers();
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<SeedCompanyOptions>(builder.Configuration.GetSection(SeedCompanyOptions.SectionName));
builder.Services.Configure<InternalApiOptions>(builder.Configuration.GetSection(InternalApiOptions.SectionName));
builder.Services.Configure<TwilioOptions>(builder.Configuration.GetSection(TwilioOptions.SectionName));
builder.Services.AddHttpClient<PromptApiClient>();
builder.Services.AddHttpClient<RestaurantPaymentClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<InternalApiOptions>>().Value;
    client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost:5100" : options.BaseUrl);
});
builder.Services.AddHttpClient<TwilioMessageClient>(client =>
{
    client.BaseAddress = new Uri("https://api.twilio.com/");
});
builder.Services.AddSingleton<WhatsappRepository>();
builder.Services.AddSingleton<OrderRegistrationService>();
builder.Services.AddSingleton<OrderConsultationService>();
builder.Services.AddSingleton<CustomerHistoryConsultationService>();
builder.Services.AddSingleton<ApplicationLogService>();
builder.Services.AddSingleton<StaffNotificationService>();
builder.Services.AddSingleton<HumanHandoffService>();
builder.Services.AddSingleton<WhatsappChatService>();
builder.Services.AddSingleton<AgentService>();
builder.Services.AddSingleton<AgentFeedbackService>();
builder.Services.AddHostedService<WhatsappMessageWorker>();
builder.Services.AddHostedService<AgentAutomatedCampaignWorker>();
builder.Services.AddHostedService<AgentFeedbackWorker>();

var app = builder.Build();

await app.Services.GetRequiredService<WhatsappRepository>().EnsureInitializedAsync();

app.MapLocalSwagger();
app.MapControllers();

app.Run();

static void AddLocalSupabaseEnvironment(ConfigurationManager configuration, string contentRootPath)
{
    var directory = new DirectoryInfo(contentRootPath);
    while (directory is not null)
    {
        var envPath = Path.Combine(directory.FullName, ".env.supabase.local");
        if (File.Exists(envPath))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                if (!string.IsNullOrWhiteSpace(configuration[key]))
                {
                    continue;
                }

                values[key] = line[(separatorIndex + 1)..].Trim();
            }

            if (values.Count > 0)
            {
                configuration.AddInMemoryCollection(values);
            }

            return;
        }

        directory = directory.Parent;
    }
}
