namespace AtendenteWhatssApp.Services;

public sealed class StaffNotificationService
{
    private readonly WhatsappRepository _repository;
    private readonly TwilioMessageClient _twilioMessageClient;
    private readonly ApplicationLogService _applicationLogService;
    private readonly ILogger<StaffNotificationService> _logger;

    public StaffNotificationService(
        WhatsappRepository repository,
        TwilioMessageClient twilioMessageClient,
        ApplicationLogService applicationLogService,
        ILogger<StaffNotificationService> logger)
    {
        _repository = repository;
        _twilioMessageClient = twilioMessageClient;
        _applicationLogService = applicationLogService;
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
            "human_handoff",
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
            "order_finalized",
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
            "image_received",
            "O cliente {0} enviou uma imagem.",
            cancellationToken);
    }

    private async Task SendStaffNotificationAsync(
        string storeId,
        string phoneNumber,
        string notificationType,
        string messageFormat,
        CancellationToken cancellationToken)
    {
        try
        {
            var trimmedStoreId = storeId.Trim();
            var trimmedPhoneNumber = phoneNumber.Trim();
            await _applicationLogService.RecordAsync(
                $"Preparing staff notification. Type={notificationType}; StoreId={trimmedStoreId}; CustomerPhone={trimmedPhoneNumber}.",
                cancellationToken);
            _logger.LogInformation(
                "Preparing staff notification {NotificationType}. StoreId={StoreId}; CustomerPhone={CustomerPhone}.",
                notificationType,
                trimmedStoreId,
                trimmedPhoneNumber);

            var settings = await _repository.GetAgentNotificationSettingsAsync(
                trimmedStoreId,
                cancellationToken);
            await _applicationLogService.RecordAsync(
                $"Staff notification settings loaded. Type={notificationType}; StoreId={trimmedStoreId}; HasResponsiblePhone={!string.IsNullOrWhiteSpace(settings.StaffNotificationPhoneNumber)}; ResponsiblePhone={settings.StaffNotificationPhoneNumber}; UpdatedAtUtc={settings.UpdatedAtUtc}.",
                cancellationToken);
            _logger.LogInformation(
                "Staff notification settings loaded for {NotificationType}. StoreId={StoreId}; HasResponsiblePhone={HasResponsiblePhone}; ResponsiblePhone={ResponsiblePhone}; UpdatedAtUtc={UpdatedAtUtc}.",
                notificationType,
                trimmedStoreId,
                !string.IsNullOrWhiteSpace(settings.StaffNotificationPhoneNumber),
                settings.StaffNotificationPhoneNumber,
                settings.UpdatedAtUtc);

            if (string.IsNullOrWhiteSpace(settings.StaffNotificationPhoneNumber))
            {
                await _applicationLogService.RecordAsync(
                    $"Skipping staff notification. Type={notificationType}; StoreId={trimmedStoreId}; Reason=no responsible phone configured for exact StoreId.",
                    cancellationToken);
                _logger.LogWarning(
                    "Skipping staff notification {NotificationType} for store {StoreId} because no responsible phone number is configured for this exact StoreId.",
                    notificationType,
                    trimmedStoreId);
                return;
            }

            var customerLabel = await ResolveCustomerLabelAsync(
                trimmedStoreId,
                trimmedPhoneNumber,
                cancellationToken);

            await _applicationLogService.RecordAsync(
                $"Sending staff notification. Type={notificationType}; From={trimmedStoreId}; To={settings.StaffNotificationPhoneNumber}; CustomerLabel={customerLabel}.",
                cancellationToken);
            _logger.LogInformation(
                "Sending staff notification {NotificationType}. From={StoreId}; To={ResponsiblePhone}; CustomerLabel={CustomerLabel}.",
                notificationType,
                trimmedStoreId,
                settings.StaffNotificationPhoneNumber,
                customerLabel);

            var sendResult = await _twilioMessageClient.SendWhatsappMessageAsync(
                from: trimmedStoreId,
                to: settings.StaffNotificationPhoneNumber,
                body: string.Format(messageFormat, customerLabel),
                cancellationToken);

            await _applicationLogService.RecordAsync(
                $"Staff notification sent. Type={notificationType}; StoreId={trimmedStoreId}; ResponsiblePhone={settings.StaffNotificationPhoneNumber}; TwilioSid={sendResult.Sid}; TwilioStatus={sendResult.Status}.",
                cancellationToken);
            _logger.LogInformation(
                "Staff notification {NotificationType} sent. StoreId={StoreId}; ResponsiblePhone={ResponsiblePhone}; TwilioSid={TwilioSid}; TwilioStatus={TwilioStatus}.",
                notificationType,
                trimmedStoreId,
                settings.StaffNotificationPhoneNumber,
                sendResult.Sid,
                sendResult.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ExternalApiException ex)
        {
            await _applicationLogService.RecordAsync(
                $"Twilio rejected staff notification. Type={notificationType}; StoreId={storeId}; CustomerPhone={phoneNumber}; StatusCode={ex.StatusCode}; ResponseBody={ex.ResponseBody}.",
                cancellationToken);
            _logger.LogWarning(
                ex,
                "Twilio rejected staff notification {NotificationType}. StoreId={StoreId}; CustomerPhone={CustomerPhone}; StatusCode={StatusCode}; ResponseBody={ResponseBody}.",
                notificationType,
                storeId,
                phoneNumber,
                ex.StatusCode,
                ex.ResponseBody);
        }
        catch (Exception ex)
        {
            await _applicationLogService.RecordAsync(
                $"Staff notification failed. Type={notificationType}; StoreId={storeId}; CustomerPhone={phoneNumber}; Error={ex.Message}.",
                cancellationToken);
            _logger.LogWarning(
                ex,
                "Could not send staff notification {NotificationType} for store {StoreId} and phone {PhoneNumber}.",
                notificationType,
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
