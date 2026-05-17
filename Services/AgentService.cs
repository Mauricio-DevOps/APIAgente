using AtendenteWhatssApp.Models;
using System.Globalization;

namespace AtendenteWhatssApp.Services;

public sealed class AgentService
{
    private readonly WhatsappRepository _repository;
    private readonly TwilioMessageClient _twilioMessageClient;

    public AgentService(
        WhatsappRepository repository,
        TwilioMessageClient twilioMessageClient)
    {
        _repository = repository;
        _twilioMessageClient = twilioMessageClient;
    }

    public async Task<AgentProductCampaignPreviewResponse?> GetProductCampaignPreviewAsync(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        var product = await _repository.GetProductAsync(storeId, productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var customers = await _repository.GetProductCampaignCustomersAsync(
            storeId,
            productId,
            cancellationToken);
        var persona = await _repository.GetAgentPersonaAsync(storeId, cancellationToken);

        return new AgentProductCampaignPreviewResponse(
            product,
            BuildProductCampaignMessage(product.Name, persona.Tone),
            customers);
    }

    public async Task<AgentSendResultResponse?> SendProductCampaignAsync(
        string storeId,
        string productId,
        string message,
        CancellationToken cancellationToken)
    {
        var preview = await GetProductCampaignPreviewAsync(storeId, productId, cancellationToken);
        if (preview is null)
        {
            return null;
        }

        return await SendMessagesAsync(
            storeId,
            preview.Customers.Select(customer => customer.PhoneNumber).ToArray(),
            message,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AgentCustomerRecurrenceResponse>> GetCustomerRecurrencesAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAgentCustomerRecurrencesAsync(storeId, cancellationToken);
    }

    public async Task<(bool CustomerFound, AgentSendResultResponse? Result)> SendCustomerReminderAsync(
        string storeId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        var customers = await GetCustomerRecurrencesAsync(storeId, cancellationToken);
        var customer = customers.FirstOrDefault(item =>
            string.Equals(item.PhoneNumber, phoneNumber, StringComparison.Ordinal));

        if (customer is null)
        {
            return (false, null);
        }

        var result = await SendMessagesAsync(
            storeId,
            new[] { customer.PhoneNumber },
            message,
            cancellationToken);

        return (true, result);
    }

    public async Task<IReadOnlyList<AgentAutomatedCampaignResponse>> GetAutomatedCampaignsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAutomatedCampaignsAsync(storeId, cancellationToken);
    }

    public async Task<AgentAutomatedCampaignResponse?> SaveAutomatedCampaignAsync(
        AgentAutomatedCampaignUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return await _repository.UpsertAutomatedCampaignAsync(request, cancellationToken);
    }

    public async Task<bool> DeleteAutomatedCampaignAsync(
        string storeId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        return await _repository.DeleteAutomatedCampaignAsync(storeId, campaignId, cancellationToken);
    }

    public async Task<AgentAutomatedCampaignRunResponse?> RunAutomatedCampaignAsync(
        string storeId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        var campaign = await _repository.GetAutomatedCampaignAsync(storeId, campaignId, cancellationToken);
        return campaign is null
            ? null
            : await RunAutomatedCampaignAsync(campaign, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentAutomatedCampaignRunResponse>> RunDueAutomatedCampaignsAsync(
        DateTimeOffset localNow,
        CancellationToken cancellationToken)
    {
        var campaigns = await _repository.GetDueAutomatedCampaignsAsync(localNow, cancellationToken);
        var runs = new List<AgentAutomatedCampaignRunResponse>();

        foreach (var campaign in campaigns)
        {
            runs.Add(await RunAutomatedCampaignAsync(campaign, cancellationToken));
        }

        return runs;
    }

    private async Task<AgentAutomatedCampaignRunResponse> RunAutomatedCampaignAsync(
        AgentAutomatedCampaignResponse campaign,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        try
        {
            var recipients = await GetAutomatedCampaignRecipientsAsync(campaign, cancellationToken);
            var distinctRecipients = recipients
                .Where(phoneNumber => !string.IsNullOrWhiteSpace(phoneNumber))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var cooldownStart = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, campaign.CooldownDays));
            var recentRecipients = await _repository.GetRecentSuccessfulCampaignRecipientsAsync(
                campaign.Id,
                cooldownStart,
                cancellationToken);

            var sendRecipients = distinctRecipients
                .Where(phoneNumber => !recentRecipients.Contains(phoneNumber))
                .ToArray();
            var skippedCooldownCount = distinctRecipients.Length - sendRecipients.Length;

            var sendResult = sendRecipients.Length == 0
                ? new AgentSendResultResponse(0, 0, Array.Empty<AgentSendResultItemResponse>())
                : await SendMessagesAsync(campaign.StoreId, sendRecipients, campaign.Message, cancellationToken);

            var completedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return await _repository.RecordAutomatedCampaignRunAsync(
                campaign,
                startedAtUtc,
                completedAtUtc,
                distinctRecipients.Length,
                skippedCooldownCount,
                sendResult.Results,
                error: null,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var completedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            return await _repository.RecordAutomatedCampaignRunAsync(
                campaign,
                startedAtUtc,
                completedAtUtc,
                eligibleCount: 0,
                skippedCooldownCount: 0,
                Array.Empty<AgentSendResultItemResponse>(),
                ex.Message,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> GetAutomatedCampaignRecipientsAsync(
        AgentAutomatedCampaignResponse campaign,
        CancellationToken cancellationToken)
    {
        return AgentAutomatedCampaignTypes.Normalize(campaign.Type) switch
        {
            AgentAutomatedCampaignTypes.ProductStock => await GetProductStockCampaignRecipientsAsync(
                campaign,
                cancellationToken),
            AgentAutomatedCampaignTypes.Recurrence => (await _repository.GetAgentCustomerRecurrencesAsync(
                    campaign.StoreId,
                    cancellationToken))
                .Where(customer => customer.IsOverdue)
                .Select(customer => customer.PhoneNumber)
                .ToArray(),
            AgentAutomatedCampaignTypes.InactiveCustomers => (await _repository.GetAgentCustomerRecurrencesAsync(
                    campaign.StoreId,
                    cancellationToken))
                .Where(customer => customer.DaysSinceLastOrder >= (campaign.InactiveDaysThreshold ?? 30))
                .Select(customer => customer.PhoneNumber)
                .ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private async Task<IReadOnlyList<string>> GetProductStockCampaignRecipientsAsync(
        AgentAutomatedCampaignResponse campaign,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaign.ProductId))
        {
            return Array.Empty<string>();
        }

        var product = await _repository.GetProductAsync(
            campaign.StoreId,
            campaign.ProductId,
            cancellationToken);

        if (product is null ||
            product.StockQuantity is null ||
            product.LowStockThreshold is null ||
            product.StockQuantity > product.LowStockThreshold)
        {
            return Array.Empty<string>();
        }

        var customers = await _repository.GetProductCampaignCustomersAsync(
            campaign.StoreId,
            campaign.ProductId,
            cancellationToken);

        return customers.Select(customer => customer.PhoneNumber).ToArray();
    }

    private async Task<AgentSendResultResponse> SendMessagesAsync(
        string storeId,
        IReadOnlyList<string> phoneNumbers,
        string message,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentSendResultItemResponse>();

        foreach (var phoneNumber in phoneNumbers)
        {
            TwilioMessageSendResult sendResult;
            try
            {
                sendResult = await _twilioMessageClient.SendWhatsappMessageAsync(
                    from: storeId,
                    to: phoneNumber,
                    body: message,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await TryRecordAgentMessageAsync(
                    storeId,
                    phoneNumber,
                    message,
                    twilioMessageSid: null,
                    WhatsappConversationMessageStatuses.Failed,
                    ex.Message,
                    cancellationToken);

                results.Add(new AgentSendResultItemResponse(phoneNumber, Sent: false, ex.Message));
                continue;
            }

            await TryRecordAgentMessageAsync(
                storeId,
                phoneNumber,
                message,
                sendResult.Sid,
                WhatsappConversationMessageStatuses.Sent,
                error: null,
                cancellationToken);

            results.Add(new AgentSendResultItemResponse(phoneNumber, Sent: true, Error: null));
        }

        return new AgentSendResultResponse(
            results.Count(result => result.Sent),
            results.Count(result => !result.Sent),
            results);
    }

    private static string BuildProductCampaignMessage(string productName, string tone)
    {
        return AgentPersonaTones.Normalize(tone) switch
        {
            AgentPersonaTones.Formal =>
                $"Ola. Identificamos que voce ja comprou {productName} conosco. O produto esta em promocao e podemos registrar um novo pedido, se desejar.",
            AgentPersonaTones.Casual =>
                $"Oi! Passando para avisar que {productName}, que voce ja comprou com a gente, esta em promocao. Quer pedir de novo?",
            AgentPersonaTones.Vendedor =>
                $"Oferta especial para voce: {productName} esta em promocao por tempo limitado. Posso separar um pedido agora?",
            AgentPersonaTones.Objetivo =>
                $"{productName} esta em promocao. Deseja fazer um pedido?",
            _ =>
                $"Ola! Vi que voce ja comprou {productName} com a gente e ele esta em promocao. Gostaria de fazer um pedido?"
        };
    }

    private async Task TryRecordAgentMessageAsync(
        string storeId,
        string phoneNumber,
        string message,
        string? twilioMessageSid,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordWhatsappConversationMessageAsync(
                storeId,
                phoneNumber,
                WhatsappConversationMessageDirections.Outbound,
                WhatsappConversationMessageTypes.Agent,
                message,
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
            // Sending results should reflect Twilio delivery attempts even if history persistence fails.
        }
    }
}
