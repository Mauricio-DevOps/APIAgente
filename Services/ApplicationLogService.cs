namespace AtendenteWhatssApp.Services;

public sealed class ApplicationLogService
{
    private readonly WhatsappRepository _repository;
    private readonly ILogger<ApplicationLogService> _logger;

    public ApplicationLogService(
        WhatsappRepository repository,
        ILogger<ApplicationLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            await _repository.RecordApplicationLogAsync(text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write application log to the database.");
        }
    }
}
