namespace AtendenteWhatssApp.Services;

public sealed class AgentFeedbackWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly AgentFeedbackService _feedbackService;
    private readonly ILogger<AgentFeedbackWorker> _logger;

    public AgentFeedbackWorker(
        AgentFeedbackService feedbackService,
        ILogger<AgentFeedbackWorker> logger)
    {
        _feedbackService = feedbackService;
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
                await _feedbackService.ProcessDueSolicitationsAsync(stoppingToken);
                await _feedbackService.ProcessPeriodicSurveysAsync(stoppingToken);
                await Task.Delay(AgentFeedbackService.GetWorkerBatchInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing feedback solicitations.");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}
