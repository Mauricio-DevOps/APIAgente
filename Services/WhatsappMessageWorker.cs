using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Options;
using Microsoft.Extensions.Options;

namespace AtendenteWhatssApp.Services;

public sealed class WhatsappMessageWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    private readonly WhatsappRepository _repository;
    private readonly WhatsappChatService _chatService;
    private readonly TwilioMessageClient _twilioMessageClient;
    private readonly ILogger<WhatsappMessageWorker> _logger;
    private readonly TwilioOptions _twilioOptions;

    public WhatsappMessageWorker(
        WhatsappRepository repository,
        WhatsappChatService chatService,
        TwilioMessageClient twilioMessageClient,
        IOptions<TwilioOptions> twilioOptions,
        ILogger<WhatsappMessageWorker> logger)
    {
        _repository = repository;
        _chatService = chatService;
        _twilioMessageClient = twilioMessageClient;
        _logger = logger;
        _twilioOptions = twilioOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _repository.ResetStaleWhatsappMessageJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _repository.TryClaimNextWhatsappMessageJobAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while processing WhatsApp jobs.");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(WhatsappMessageJob job, CancellationToken cancellationToken)
    {
        try
        {
            var outputText = string.IsNullOrWhiteSpace(job.FeedbackSolicitationId)
                ? await _chatService.ProcessAsync(
                    job.Message,
                    job.PhoneNumber,
                    job.StoreId,
                    job.Id,
                    cancellationToken)
                : await _chatService.ProcessSolicitedFeedbackResponseAsync(
                    job.Message,
                    job.PhoneNumber,
                    job.StoreId,
                    job.FeedbackSolicitationId,
                    cancellationToken);

            var sendResult = await _twilioMessageClient.SendWhatsappMessageAsync(
                from: job.StoreId,
                to: job.PhoneNumber,
                body: outputText,
                cancellationToken);

            try
            {
                await _repository.RecordWhatsappConversationMessageAsync(
                    job.StoreId,
                    job.PhoneNumber,
                    WhatsappConversationMessageDirections.Outbound,
                    WhatsappConversationMessageTypes.Ai,
                    outputText,
                    sendResult.Sid,
                    job.Id,
                    WhatsappConversationMessageStatuses.Sent,
                    error: null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp job {JobId} was sent but could not be recorded in conversation history.", job.Id);
            }

            await _repository.CompleteWhatsappMessageJobAsync(job.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            var shouldRetry = job.Attempts < Math.Max(1, _twilioOptions.MaxSendAttempts);
            await _repository.FailWhatsappMessageJobAsync(job.Id, ex.Message, shouldRetry, cancellationToken);

            if (shouldRetry)
            {
                _logger.LogWarning(ex, "WhatsApp job {JobId} failed and will be retried.", job.Id);
            }
            else
            {
                _logger.LogError(ex, "WhatsApp job {JobId} failed permanently.", job.Id);
            }
        }
    }
}
