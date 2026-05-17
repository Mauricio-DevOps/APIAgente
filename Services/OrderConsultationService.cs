using System.Globalization;
using System.Text;

namespace AtendenteWhatssApp.Services;

public sealed class OrderConsultationService
{
    private readonly WhatsappRepository _repository;

    public OrderConsultationService(WhatsappRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> ConsultarPedidosAtivosAsync(
        string storeId,
        string phoneNumber,
        string aiResponseText,
        CancellationToken cancellationToken)
    {
        var orders = await _repository.GetActiveOrdersAsync(
            storeId.Trim(),
            phoneNumber.Trim(),
            cancellationToken);

        var message = new StringBuilder(aiResponseText.Trim());
        message.AppendLine();
        message.AppendLine();

        if (orders.Count == 0)
        {
            message.Append("Não encontrei nenhum pedido ativo para este número.");
            return message.ToString();
        }

        message.AppendLine(orders.Count == 1
            ? "Encontrei este pedido ativo:"
            : "Encontrei estes pedidos ativos:");

        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];

            message.AppendLine();
            message.AppendLine($"Pedido {index + 1}:");
            message.AppendLine($"Status: {FormatStatus(order.Status)}");
            message.AppendLine($"Criado em: {FormatDate(order.CreatedAtUtc)}");
            message.AppendLine($"Total: {FormatMoney(order.TotalCents)}");

            if (!string.IsNullOrWhiteSpace(order.SaleType))
            {
                message.AppendLine($"Tipo de venda: {order.SaleType}");
            }

            if (order.Items.Count == 0)
            {
                continue;
            }

            message.AppendLine("Itens:");
            foreach (var item in order.Items)
            {
                var productName = string.IsNullOrWhiteSpace(item.ProductNameSnapshot)
                    ? item.RequestedProductName
                    : item.ProductNameSnapshot;

                message.Append($"- {item.Quantity}x {productName}");

                if (item.TotalPriceCents is not null)
                {
                    message.Append($" ({FormatMoney(item.TotalPriceCents.Value)})");
                }

                if (!string.IsNullOrWhiteSpace(item.Observation))
                {
                    message.Append($" - {item.Observation}");
                }

                message.AppendLine();
            }
        }

        return message.ToString().TrimEnd();
    }

    private static string FormatStatus(string status)
    {
        return status switch
        {
            OrderStatuses.EmProducao => "em produção",
            OrderStatuses.EmRotaEntrega => "em rota de entrega",
            OrderStatuses.Concluido => "concluído",
            OrderStatuses.PendingReview => "pendente de revisão",
            _ => status
        };
    }

    private static string FormatDate(string date)
    {
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            return date;
        }

        var localDate = parsedDate.ToLocalTime();
        return localDate.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
    }

    private static string FormatMoney(long cents)
    {
        return (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
    }
}
