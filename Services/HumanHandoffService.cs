namespace AtendenteWhatssApp.Services;

public sealed class HumanHandoffService
{
    private const string HumanHandoffMessage = "Vou encaminhar sua mensagem para um atendente e ele ir\u00e1 retornar o mais r\u00e1pido poss\u00edvel.";

    private readonly WhatsappRepository _repository;
    private readonly StaffNotificationService _staffNotificationService;
    private readonly ApplicationLogService _applicationLogService;

    public HumanHandoffService(
        WhatsappRepository repository,
        StaffNotificationService staffNotificationService,
        ApplicationLogService applicationLogService)
    {
        _repository = repository;
        _staffNotificationService = staffNotificationService;
        _applicationLogService = applicationLogService;
    }

    public async Task<string> SolicitarAtendimentoHumanoAsync(
        string storeId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        await _applicationLogService.RecordAsync(
            $"Human handoff requested. StoreId={storeId}; PhoneNumber={phoneNumber}.",
            cancellationToken);

        await _repository.SetWhatsappAgentEnabledAsync(
            storeId,
            phoneNumber,
            isAgentEnabled: false,
            cancellationToken);

        await _applicationLogService.RecordAsync(
            $"WhatsApp agent disabled for human handoff. StoreId={storeId}; PhoneNumber={phoneNumber}.",
            cancellationToken);

        await _staffNotificationService.NotifyHumanHandoffRequestedAsync(
            storeId,
            phoneNumber,
            cancellationToken);

        return HumanHandoffMessage;
    }
}
