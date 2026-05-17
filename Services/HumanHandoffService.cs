namespace AtendenteWhatssApp.Services;

public sealed class HumanHandoffService
{
    public Task<string> SolicitarAtendimentoHumanoAsync(
        string storeId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        return Task.FromResult("Encaminhamos sua mensagem para um de nossos atendentes, ele irá entrar em contato com você o mais rápido possível.");
    }
}
