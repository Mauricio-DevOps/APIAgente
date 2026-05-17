namespace AtendenteWhatssApp.Services;

public sealed class AgentAutomatedCampaignWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly AgentService _agentService;
    private readonly ILogger<AgentAutomatedCampaignWorker> _logger;

    public AgentAutomatedCampaignWorker(
        AgentService agentService,
        ILogger<AgentAutomatedCampaignWorker> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runs = await _agentService.RunDueAutomatedCampaignsAsync(
                    DateTimeOffset.Now,
                    stoppingToken);

                foreach (var run in runs)
                {
                    _logger.LogInformation(
                        "Automated campaign {CampaignId} run {RunId}: {SentCount} sent, {FailedCount} failed, {SkippedCount} skipped by cooldown.",
                        run.CampaignId,
                        run.Id,
                        run.SentCount,
                        run.FailedCount,
                        run.SkippedCooldownCount);
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing automated campaigns.");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}
