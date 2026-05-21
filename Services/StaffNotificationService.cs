namespace AtendenteWhatssApp.Services;

public sealed class StaffNotificationService
{
    private readonly WhatsappRepository _repository;
    private readonly TwilioMessageClient _twilioMessageClient;
    private readonly ILogger<StaffNotificationService> _logger;

    public StaffNotificationService(
        WhatsappRepository repository,
        TwilioMessageClient twilioMessageClient,
        ILogger<StaffNotificationService> logger)
    {
        _repository = repository;
        _twilioMessageClient = twilioMessageClient;
        _logger = logger;
    }

    public Task NotifyHumanHandoffRequestedAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        return SendStaffNotificationAsync(
            storeId,
            phoneNumber,
            "O cliente {0} solicitou um atendimento.",
            cancellationToken);
    }

    public Task NotifyOrderFinalizedAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        return SendStaffNotificationAsync(
            storeId,
            phoneNumber,
            "O cliente {0} finalizou um pedido.",
            cancellationToken);
    }

    public Task NotifyImageReceivedAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        return SendStaffNotificationAsync(
            storeId,
            phoneNumber,
            "O cliente {0} enviou uma imagem.",
            cancellationToken);
    }

    private async Task SendStaffNotificationAsync(
        string storeId,
        string phoneNumber,
        string messageFormat,
        CancellationToken cancellationToken)
    {
        try
        {
            var trimmedStoreId = storeId.Trim();
            var trimmedPhoneNumber = phoneNumber.Trim();
            var settings = await _repository.GetAgentNotificationSettingsAsync(
                trimmedStoreId,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.StaffNotificationPhoneNumber))
            {
                _logger.LogInformation(
                    "Skipping staff notification for store {StoreId} because no responsible phone number is configured.",
                    trimmedStoreId);
                return;
            }

            var customerLabel = await ResolveCustomerLabelAsync(
                trimmedStoreId,
                trimmedPhoneNumber,
                cancellationToken);

            await _twilioMessageClient.SendWhatsappMessageAsync(
                from: trimmedStoreId,
                to: settings.StaffNotificationPhoneNumber,
                body: string.Format(messageFormat, customerLabel),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not send staff notification for store {StoreId} and phone {PhoneNumber}.",
                storeId,
                phoneNumber);
        }
    }

    private async Task<string> ResolveCustomerLabelAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var customer = await _repository.FindCustomerByPhoneAsync(storeId, phoneNumber, cancellationToken);
        if (!string.IsNullOrWhiteSpace(customer?.ClienteNome))
        {
            return customer.ClienteNome.Trim();
        }

        var digits = PhoneNumberNormalizer.NormalizeDigits(phoneNumber);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return phoneNumber.Trim();
        }

        return digits.StartsWith("55", StringComparison.Ordinal)
            ? $"+{digits}"
            : $"+55{PhoneNumberNormalizer.ToBrazilNationalPhone(phoneNumber)}";
    }
}
