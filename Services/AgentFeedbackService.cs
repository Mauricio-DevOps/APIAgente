using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public sealed class AgentFeedbackService
{
    private static readonly TimeSpan WorkerBatchInterval = TimeSpan.FromMinutes(1);

    private readonly WhatsappRepository _repository;
    private readonly TwilioMessageClient _twilioMessageClient;

    public AgentFeedbackService(
        WhatsappRepository repository,
        TwilioMessageClient twilioMessageClient)
    {
        _repository = repository;
        _twilioMessageClient = twilioMessageClient;
    }

    public async Task<AgentFeedbackSettingsResponse> GetSettingsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAgentFeedbackSettingsAsync(storeId, cancellationToken);
    }

    public async Task<AgentFeedbackSettingsResponse> SaveSettingsAsync(
        AgentFeedbackSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return await _repository.UpsertAgentFeedbackSettingsAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentFeedbackSolicitationResponse>> GetSolicitationsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAgentFeedbackSolicitationsAsync(storeId, limit: 50, cancellationToken);
    }

    public async Task<AgentFeedbackSolicitationResponse?> SendSolicitationAsync(
        string storeId,
        string solicitationId,
        CancellationToken cancellationToken)
    {
        var solicitation = await _repository.GetAgentFeedbackSolicitationAsync(
            storeId,
            solicitationId,
            cancellationToken);
        if (solicitation is null)
        {
            return null;
        }

        if (solicitation.Status != AgentFeedbackSolicitationStatuses.Responded)
        {
            await SendSolicitationAsync(solicitation, cancellationToken);
        }

        return await _repository.GetAgentFeedbackSolicitationAsync(storeId, solicitationId, cancellationToken);
    }

    public async Task<bool> TryRecordResponseAsync(
        string storeId,
        string phoneNumber,
        string? text,
        string? mediaUrl,
        string? mediaContentType,
        CancellationToken cancellationToken)
    {
        return await _repository.TryRecordAgentFeedbackResponseAsync(
            storeId,
            phoneNumber,
            text,
            mediaUrl,
            mediaContentType,
            cancellationToken);
    }

    public async Task<AgentFeedbackIncomingResponseResult> TryPrepareIncomingResponseAsync(
        string storeId,
        string phoneNumber,
        string? text,
        string? mediaUrl,
        string? mediaContentType,
        CancellationToken cancellationToken)
    {
        var target = await _repository.GetOpenAgentFeedbackResponseTargetAsync(
            storeId,
            phoneNumber,
            cancellationToken);
        if (target is null)
        {
            return AgentFeedbackIncomingResponseResult.None;
        }

        var normalizedText = NormalizeOptionalText(text);
        var normalizedMediaUrl = NormalizeOptionalText(mediaUrl);
        var normalizedMediaContentType = NormalizeOptionalText(mediaContentType);
        var isAudio = normalizedMediaUrl is not null &&
            normalizedMediaContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

        if (isAudio && AgentFeedbackFormats.AcceptsAudio(target.AcceptedFormat))
        {
            var recorded = await _repository.TryRecordAgentFeedbackResponseAsync(
                storeId,
                phoneNumber,
                text,
                mediaUrl,
                mediaContentType,
                cancellationToken);

            return recorded
                ? AgentFeedbackIncomingResponseResult.Recorded("Obrigado pelo feedback! Registramos sua resposta.")
                : AgentFeedbackIncomingResponseResult.None;
        }

        if (normalizedText is not null && AgentFeedbackFormats.AcceptsText(target.AcceptedFormat))
        {
            return AgentFeedbackIncomingResponseResult.RequiresAnalysis(target.SolicitationId);
        }

        return AgentFeedbackIncomingResponseResult.None;
    }

    public async Task RecordDetectedFeedbackAsync(
        string storeId,
        string phoneNumber,
        string customerMessage,
        string aiResponseText,
        string aiOutputJson,
        string? promptResponseId,
        string? conversationId,
        PromptFeedbackPayload? feedback,
        CancellationToken cancellationToken)
    {
        await _repository.RecordDetectedAgentFeedbackAsync(
            CreateRegistrationCommand(
                storeId,
                phoneNumber,
                customerMessage,
                aiResponseText,
                aiOutputJson,
                promptResponseId,
                conversationId,
                feedback),
            cancellationToken);
    }

    public async Task<bool> RecordSolicitedTextFeedbackAsync(
        string solicitationId,
        string storeId,
        string phoneNumber,
        string customerMessage,
        string aiResponseText,
        string aiOutputJson,
        string? promptResponseId,
        string? conversationId,
        PromptFeedbackPayload? feedback,
        CancellationToken cancellationToken)
    {
        return await _repository.RecordSolicitedAgentFeedbackTextResponseAsync(
            solicitationId,
            CreateRegistrationCommand(
                storeId,
                phoneNumber,
                customerMessage,
                aiResponseText,
                aiOutputJson,
                promptResponseId,
                conversationId,
                feedback),
            cancellationToken);
    }

    public async Task ProcessDueSolicitationsAsync(CancellationToken cancellationToken)
    {
        var dueSolicitations = await _repository.GetDueAgentFeedbackSolicitationsAsync(
            DateTimeOffset.UtcNow,
            limit: 25,
            cancellationToken);

        foreach (var solicitation in dueSolicitations)
        {
            await SendSolicitationAsync(solicitation, cancellationToken);
        }
    }

    public async Task ProcessPeriodicSurveysAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueSettings = await _repository.GetDuePeriodicFeedbackSettingsAsync(now, cancellationToken);

        foreach (var settings in dueSettings)
        {
            var candidates = await _repository.GetPeriodicFeedbackCandidatesAsync(
                settings.StoreId,
                settings.PeriodicSurveySampleSize,
                cancellationToken);

            if (candidates.Count == 0)
            {
                await _repository.UpdateFeedbackPeriodicSurveyRunAsync(settings.StoreId, now, cancellationToken);
                continue;
            }

            var solicitations = await _repository.CreatePeriodicFeedbackSolicitationsAsync(
                settings,
                candidates,
                now,
                cancellationToken);

            foreach (var solicitation in solicitations)
            {
                await SendSolicitationAsync(solicitation, cancellationToken);
            }
        }
    }

    public static TimeSpan GetWorkerBatchInterval()
    {
        return WorkerBatchInterval;
    }

    private async Task SendSolicitationAsync(
        AgentFeedbackSolicitationResponse solicitation,
        CancellationToken cancellationToken)
    {
        var whatsappAddress = PhoneNumberNormalizer.ToWhatsappAddress(solicitation.PhoneNumber);
        TwilioMessageSendResult sendResult;
        try
        {
            sendResult = await _twilioMessageClient.SendWhatsappMessageAsync(
                from: solicitation.StoreId,
                to: whatsappAddress,
                body: solicitation.Message,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryRecordSolicitationMessageAsync(
                solicitation,
                whatsappAddress,
                twilioMessageSid: null,
                WhatsappConversationMessageStatuses.Failed,
                ex.Message,
                cancellationToken);

            await _repository.MarkAgentFeedbackSolicitationFailedAsync(
                solicitation.Id,
                ex.Message,
                cancellationToken);
            return;
        }

        await TryRecordSolicitationMessageAsync(
            solicitation,
            whatsappAddress,
            sendResult.Sid,
            WhatsappConversationMessageStatuses.Sent,
            error: null,
            cancellationToken);

        await _repository.MarkAgentFeedbackSolicitationSentAsync(solicitation.Id, cancellationToken);
    }

    private async Task TryRecordSolicitationMessageAsync(
        AgentFeedbackSolicitationResponse solicitation,
        string whatsappAddress,
        string? twilioMessageSid,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordWhatsappConversationMessageAsync(
                solicitation.StoreId,
                whatsappAddress,
                WhatsappConversationMessageDirections.Outbound,
                WhatsappConversationMessageTypes.System,
                solicitation.Message,
                twilioMessageSid,
                sourceJobId: null,
                status,
                error,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Delivery state remains authoritative even if the optional chat history write fails.
        }
    }

    private static AgentFeedbackRegistrationCommand CreateRegistrationCommand(
        string storeId,
        string phoneNumber,
        string customerMessage,
        string aiResponseText,
        string aiOutputJson,
        string? promptResponseId,
        string? conversationId,
        PromptFeedbackPayload? feedback)
    {
        return new AgentFeedbackRegistrationCommand(
            storeId,
            phoneNumber,
            customerMessage,
            aiResponseText,
            aiOutputJson,
            promptResponseId,
            conversationId,
            NormalizeAnalysis(feedback));
    }

    private static AgentFeedbackAnalysisData NormalizeAnalysis(PromptFeedbackPayload? feedback)
    {
        if (feedback is null)
        {
            return new AgentFeedbackAnalysisData(
                AgentFeedbackCategories.Indefinido,
                AgentFeedbackSentiments.Indefinido,
                AgentFeedbackCustomerClassifications.Indefinido,
                Score: null,
                Summary: null);
        }

        return new AgentFeedbackAnalysisData(
            AgentFeedbackCategories.Normalize(feedback.Categoria),
            AgentFeedbackSentiments.Normalize(feedback.Sentimento),
            AgentFeedbackCustomerClassifications.Normalize(feedback.ClassificacaoCliente),
            feedback.Pontuacao is null ? null : Math.Clamp(feedback.Pontuacao.Value, -100, 100),
            NormalizeOptionalText(feedback.Resumo));
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record AgentFeedbackIncomingResponseResult(
    bool IsFeedback,
    bool ShouldAnalyzeText,
    string? SolicitationId,
    string? ImmediateResponse)
{
    public static AgentFeedbackIncomingResponseResult None { get; } = new(false, false, null, null);

    public static AgentFeedbackIncomingResponseResult Recorded(string responseMessage)
    {
        return new AgentFeedbackIncomingResponseResult(true, false, null, responseMessage);
    }

    public static AgentFeedbackIncomingResponseResult RequiresAnalysis(string solicitationId)
    {
        return new AgentFeedbackIncomingResponseResult(true, true, solicitationId, null);
    }
}
