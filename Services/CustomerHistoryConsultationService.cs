using System.Globalization;
using System.Text;
using System.Text.Json;
using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public sealed class CustomerHistoryConsultationService
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PendingActionLifetime = TimeSpan.FromMinutes(30);
    private const int DefaultListLimit = 10;
    private const int MaxListLimit = 20;

    private readonly WhatsappRepository _repository;
    private readonly OrderRegistrationService _orderRegistrationService;

    public CustomerHistoryConsultationService(
        WhatsappRepository repository,
        OrderRegistrationService orderRegistrationService)
    {
        _repository = repository;
        _orderRegistrationService = orderRegistrationService;
    }

    public async Task<string?> TryHandlePendingActionAsync(
        string storeId,
        string phoneNumber,
        string message,
        string? sourceMessageId,
        CancellationToken cancellationToken)
    {
        var pendingAction = await _repository.GetOpenPendingCustomerActionAsync(
            storeId,
            phoneNumber,
            PendingCustomerActionTypes.Reorder,
            cancellationToken);
        if (pendingAction is null)
        {
            return null;
        }

        if (IsCancellation(message))
        {
            await _repository.CancelPendingCustomerActionAsync(pendingAction.Id, cancellationToken);
            return "Combinado, cancelei essa recompra. Se quiser consultar ou montar outro pedido, e so me chamar.";
        }

        if (!IsConfirmation(message))
        {
            return null;
        }

        var customer = await _repository.FindCustomerByPhoneAsync(storeId, phoneNumber, cancellationToken);
        var missingFields = GetMissingCustomerFields(customer);
        if (missingFields.Count > 0)
        {
            return BuildCustomerRegistrationRequestText(customer, missingFields) +
                "\n\nDepois de completar o cadastro, me envie \"confirmo\" para registrar essa recompra.";
        }

        PendingReorderPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PendingReorderPayload>(pendingAction.PayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.Items.Count == 0)
        {
            await _repository.CancelPendingCustomerActionAsync(pendingAction.Id, cancellationToken);
            return "Nao consegui recuperar os dados dessa recompra. Pode me pedir novamente, por favor?";
        }

        var orderPayload = CreateOrderPayload(payload, customer);
        var responseText = BuildReorderRegisteredText(payload);
        var aiOutputJson = JsonSerializer.Serialize(
            new
            {
                source = "customer-history-reorder",
                pendingActionId = pendingAction.Id,
                payload
            },
            JsonOptions);

        var result = await _orderRegistrationService.RegistrarPedidoAsync(
            new OrderRegistrationCommand(
                storeId,
                phoneNumber,
                string.IsNullOrWhiteSpace(sourceMessageId) ? Guid.NewGuid().ToString("N") : sourceMessageId,
                null,
                null,
                message,
                responseText,
                aiOutputJson,
                orderPayload),
            cancellationToken);

        await _repository.CompletePendingCustomerActionAsync(pendingAction.Id, cancellationToken);

        if (string.Equals(result.Status, OrderStatuses.PendingReview, StringComparison.Ordinal))
        {
            return responseText +
                "\n\nAlguns itens precisam de revisao pela equipe antes de seguir, mas ja deixei tudo registrado.";
        }

        return responseText;
    }

    public async Task<string> ConsultarHistoricoClienteAsync(
        string storeId,
        string phoneNumber,
        PromptCustomerHistoryQueryPayload? query,
        string aiResponseText,
        CancellationToken cancellationToken)
    {
        var customer = await _repository.FindCustomerByPhoneAsync(storeId, phoneNumber, cancellationToken);
        if (customer is null)
        {
            return "Nao encontrei seu cadastro na base desta empresa. Para consultar seu historico, preciso identificar seu cadastro primeiro.";
        }

        var intention = CustomerHistoryQueryIntentions.Normalize(query?.Intencao);
        var action = CustomerHistoryQueryActions.Normalize(query?.Acao);
        var limit = NormalizeLimit(query?.Limite);
        var productReference = query?.ProdutoReferencia?.Trim();

        return intention switch
        {
            CustomerHistoryQueryIntentions.ItensJaComprados =>
                await BuildPurchasedItemsResponseAsync(storeId, phoneNumber, limit, cancellationToken),
            CustomerHistoryQueryIntentions.ItemMaisComprado =>
                await BuildMostPurchasedItemResponseAsync(storeId, phoneNumber, action, cancellationToken),
            CustomerHistoryQueryIntentions.ItemMaisCaroComprado =>
                await BuildMostExpensiveItemResponseAsync(storeId, phoneNumber, action, cancellationToken),
            CustomerHistoryQueryIntentions.UltimoPedido =>
                await BuildLastOrderResponseAsync(storeId, phoneNumber, askForConfirmation: false, cancellationToken),
            CustomerHistoryQueryIntentions.ReplicarUltimoPedido =>
                await BuildLastOrderResponseAsync(storeId, phoneNumber, askForConfirmation: true, cancellationToken),
            CustomerHistoryQueryIntentions.RecomprarItemHistorico =>
                await BuildReorderHistoricalItemResponseAsync(storeId, phoneNumber, productReference, cancellationToken),
            CustomerHistoryQueryIntentions.VerificarSeJaComprouItem =>
                await BuildPurchasedItemCheckResponseAsync(storeId, phoneNumber, productReference, cancellationToken),
            _ =>
                await BuildHistorySummaryResponseAsync(storeId, phoneNumber, limit, cancellationToken)
        };
    }

    private async Task<string> BuildPurchasedItemsResponseAsync(
        string storeId,
        string phoneNumber,
        int limit,
        CancellationToken cancellationToken)
    {
        var items = await _repository.GetCustomerPurchasedItemsAsync(storeId, phoneNumber, limit, cancellationToken);
        if (items.Count == 0)
        {
            return "Nao encontrei itens comprados no seu historico.";
        }

        var message = new StringBuilder("Encontrei estes itens no seu historico de compras:");
        foreach (var item in items)
        {
            message.AppendLine();
            message.Append("- ");
            message.Append(item.ProductName);
            message.Append($": {item.TotalQuantity} unidade(s), em {item.OrderCount} pedido(s)");
            message.Append($", ultima compra em {FormatDate(item.LastPurchasedAtUtc)}");
            message.Append($", total historico {FormatMoney(item.TotalSpentCents)}");
            message.Append('.');
        }

        return message.ToString();
    }

    private async Task<string> BuildMostPurchasedItemResponseAsync(
        string storeId,
        string phoneNumber,
        string action,
        CancellationToken cancellationToken)
    {
        var item = (await _repository.GetCustomerPurchasedItemsAsync(storeId, phoneNumber, limit: 1, cancellationToken))
            .FirstOrDefault();
        if (item is null)
        {
            return "Nao encontrei compras concluidas no seu historico.";
        }

        if (string.Equals(action, CustomerHistoryQueryActions.PedirConfirmacaoRecompra, StringComparison.Ordinal))
        {
            return await CreateSingleItemPendingReorderResponseAsync(
                storeId,
                phoneNumber,
                item.ProductName,
                quantity: 1,
                item.MaxUnitPriceCents,
                observation: null,
                $"o item que voce mais comprou ({item.ProductName})",
                cancellationToken);
        }

        return $"O item que voce mais compra e {item.ProductName}. Voce ja comprou {item.TotalQuantity} unidade(s), em {item.OrderCount} pedido(s). A ultima compra foi em {FormatDate(item.LastPurchasedAtUtc)}.";
    }

    private async Task<string> BuildMostExpensiveItemResponseAsync(
        string storeId,
        string phoneNumber,
        string action,
        CancellationToken cancellationToken)
    {
        var item = await _repository.GetCustomerMostExpensivePurchasedItemAsync(storeId, phoneNumber, cancellationToken);
        if (item is null)
        {
            return "Nao encontrei compras concluidas no seu historico.";
        }

        if (string.Equals(action, CustomerHistoryQueryActions.PedirConfirmacaoRecompra, StringComparison.Ordinal))
        {
            return await CreateSingleItemPendingReorderResponseAsync(
                storeId,
                phoneNumber,
                item.ProductName,
                Math.Max(1, item.Quantity),
                item.UnitPriceCents,
                item.Observation,
                $"o item mais caro que voce ja comprou ({item.ProductName})",
                cancellationToken);
        }

        var priceText = item.UnitPriceCents is null
            ? "sem preco unitario registrado"
            : FormatMoney(item.UnitPriceCents.Value);
        return $"O item mais caro que encontrei no seu historico foi {item.ProductName}, comprado em {FormatDate(item.PurchasedAtUtc)}, com preco unitario de {priceText}.";
    }

    private async Task<string> BuildLastOrderResponseAsync(
        string storeId,
        string phoneNumber,
        bool askForConfirmation,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetLastCompletedOrderAsync(storeId, phoneNumber, cancellationToken);
        if (order is null)
        {
            return "Nao encontrei pedidos concluidos no seu historico.";
        }

        if (!askForConfirmation)
        {
            return FormatOrderSummary("Seu ultimo pedido concluido foi este:", order);
        }

        var payload = CreatePendingReorderPayloadFromOrder(order, "seu ultimo pedido");
        await SavePendingReorderAsync(storeId, phoneNumber, payload, cancellationToken);

        return FormatOrderSummary("Encontrei seu ultimo pedido concluido:", order) +
            "\n\nPosso registrar novamente esse pedido para voce? Responda \"confirmo\" para registrar ou \"cancelar\" para desistir.";
    }

    private async Task<string> BuildReorderHistoricalItemResponseAsync(
        string storeId,
        string phoneNumber,
        string? productReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productReference))
        {
            return "Qual item do seu historico voce quer comprar novamente?";
        }

        var item = await FindPurchasedItemByReferenceAsync(storeId, phoneNumber, productReference, cancellationToken);
        if (item is null)
        {
            return $"Nao encontrei \"{productReference}\" no seu historico de compras.";
        }

        return await CreateSingleItemPendingReorderResponseAsync(
            storeId,
            phoneNumber,
            item.ProductName,
            quantity: 1,
            item.MaxUnitPriceCents,
            observation: null,
            item.ProductName,
            cancellationToken);
    }

    private async Task<string> BuildPurchasedItemCheckResponseAsync(
        string storeId,
        string phoneNumber,
        string? productReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productReference))
        {
            return "Qual produto voce quer que eu confira no seu historico?";
        }

        var item = await FindPurchasedItemByReferenceAsync(storeId, phoneNumber, productReference, cancellationToken);
        if (item is null)
        {
            return $"Nao encontrei \"{productReference}\" no seu historico de compras.";
        }

        return $"Sim, voce ja comprou {item.ProductName}. Foram {item.TotalQuantity} unidade(s), em {item.OrderCount} pedido(s). A ultima compra foi em {FormatDate(item.LastPurchasedAtUtc)}.";
    }

    private async Task<string> BuildHistorySummaryResponseAsync(
        string storeId,
        string phoneNumber,
        int limit,
        CancellationToken cancellationToken)
    {
        var orderCount = await _repository.CountCustomerCompletedOrdersAsync(storeId, phoneNumber, cancellationToken);
        var items = await _repository.GetCustomerPurchasedItemsAsync(storeId, phoneNumber, Math.Min(limit, 5), cancellationToken);
        var lastOrder = await _repository.GetLastCompletedOrderAsync(storeId, phoneNumber, cancellationToken);

        if (orderCount == 0 || items.Count == 0)
        {
            return "Nao encontrei compras concluidas no seu historico.";
        }

        var message = new StringBuilder();
        message.Append($"Encontrei {orderCount} pedido(s) concluido(s) no seu historico.");
        if (lastOrder is not null)
        {
            message.Append($" Seu ultimo pedido foi em {FormatDate(lastOrder.CreatedAtUtc)}.");
        }

        message.AppendLine();
        message.AppendLine();
        message.AppendLine("Itens principais:");
        foreach (var item in items)
        {
            message.AppendLine($"- {item.ProductName}: {item.TotalQuantity} unidade(s), em {item.OrderCount} pedido(s)");
        }

        return message.ToString().TrimEnd();
    }

    private async Task<CustomerPurchasedItemData?> FindPurchasedItemByReferenceAsync(
        string storeId,
        string phoneNumber,
        string productReference,
        CancellationToken cancellationToken)
    {
        var normalizedReference = TextNormalizer.NormalizeForLookup(productReference);
        if (string.IsNullOrWhiteSpace(normalizedReference))
        {
            return null;
        }

        var items = await _repository.GetCustomerPurchasedItemsAsync(storeId, phoneNumber, limit: 200, cancellationToken);
        return items.FirstOrDefault(item =>
        {
            var normalizedName = TextNormalizer.NormalizeForLookup(item.ProductName);
            return normalizedName.Contains(normalizedReference, StringComparison.Ordinal) ||
                normalizedReference.Contains(normalizedName, StringComparison.Ordinal);
        });
    }

    private async Task<string> CreateSingleItemPendingReorderResponseAsync(
        string storeId,
        string phoneNumber,
        string productName,
        int quantity,
        long? unitPriceCents,
        string? observation,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        var normalizedQuantity = Math.Max(1, quantity);
        var estimatedTotalCents = unitPriceCents.GetValueOrDefault() * normalizedQuantity;
        var payload = new PendingReorderPayload(
            sourceDescription,
            ResolveSaleType(estimatedTotalCents, preferredSaleType: null),
            estimatedTotalCents,
            [new PendingReorderItemPayload(productName, normalizedQuantity, observation, unitPriceCents, estimatedTotalCents == 0 ? null : estimatedTotalCents)],
            "Recompra baseada no historico do cliente.");

        await SavePendingReorderAsync(storeId, phoneNumber, payload, cancellationToken);

        var priceText = unitPriceCents is null ? string.Empty : $" Preco historico: {FormatMoney(unitPriceCents.Value)}.";
        return $"Encontrei {sourceDescription} no seu historico.{priceText}\n\nPosso registrar {normalizedQuantity}x {productName} para voce? Responda \"confirmo\" para registrar ou \"cancelar\" para desistir.";
    }

    private async Task SavePendingReorderAsync(
        string storeId,
        string phoneNumber,
        PendingReorderPayload payload,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var action = new PendingCustomerAction(
            Guid.NewGuid().ToString("N"),
            storeId.Trim(),
            PhoneNumberNormalizer.ToBrazilNationalPhone(phoneNumber),
            PendingCustomerActionTypes.Reorder,
            JsonSerializer.Serialize(payload, JsonOptions),
            PendingCustomerActionStatuses.Active,
            now.ToString("O", CultureInfo.InvariantCulture),
            now.Add(PendingActionLifetime).ToString("O", CultureInfo.InvariantCulture));

        await _repository.SavePendingCustomerActionAsync(action, cancellationToken);
    }

    private static PendingReorderPayload CreatePendingReorderPayloadFromOrder(
        ActiveOrderData order,
        string sourceDescription)
    {
        var items = order.Items
            .Where(item => item.Quantity > 0)
            .Select(item => new PendingReorderItemPayload(
                ResolveProductName(item),
                item.Quantity,
                item.Observation,
                item.UnitPriceCents,
                item.TotalPriceCents))
            .ToArray();

        return new PendingReorderPayload(
            sourceDescription,
            ResolveSaleType(order.TotalCents, order.SaleType),
            order.TotalCents,
            items,
            "Recompra baseada no ultimo pedido concluido.");
    }

    private static PromptOrderPayload CreateOrderPayload(
        PendingReorderPayload payload,
        CustomerResponse? customer)
    {
        var observation = payload.GeneralObservation;
        if (!string.IsNullOrWhiteSpace(customer?.ClienteEndereco))
        {
            var addressObservation = $"Endereco de entrega: {customer.ClienteEndereco.Trim()}";
            observation = string.IsNullOrWhiteSpace(observation)
                ? addressObservation
                : $"{observation}\n{addressObservation}";
        }

        return new PromptOrderPayload(
            payload.SaleType,
            payload.Items
                .Select(item => new PromptOrderItemPayload(item.ProductName, item.Quantity, item.Observation))
                .ToArray(),
            observation);
    }

    private static string BuildReorderRegisteredText(PendingReorderPayload payload)
    {
        var message = new StringBuilder("Pedido registrado com sucesso!\n\nItens registrados:");
        foreach (var item in payload.Items)
        {
            message.AppendLine();
            message.Append($"- {item.Quantity}x {item.ProductName}");
            if (item.TotalPriceCents is not null)
            {
                message.Append($" ({FormatMoney(item.TotalPriceCents.Value)})");
            }
        }

        if (payload.EstimatedTotalCents > 0)
        {
            message.AppendLine();
            message.Append($"Total historico de referencia: {FormatMoney(payload.EstimatedTotalCents)}");
        }

        message.AppendLine();
        message.AppendLine();
        message.Append("Ja encaminhei seu pedido para a nossa equipe e vamos preparar tudo o mais rapido possivel. Estarei encerrando a nossa comunicacao, mas qualquer duvida, pode mandar mensagem que a gente responde.");
        return message.ToString();
    }

    private static string FormatOrderSummary(string title, ActiveOrderData order)
    {
        var message = new StringBuilder(title);
        message.AppendLine();
        message.AppendLine($"Data: {FormatDate(order.CreatedAtUtc)}");
        message.AppendLine($"Total: {FormatMoney(order.TotalCents)}");
        if (!string.IsNullOrWhiteSpace(order.SaleType))
        {
            message.AppendLine($"Tipo de venda: {order.SaleType}");
        }

        if (order.Items.Count > 0)
        {
            message.AppendLine();
            message.AppendLine("Itens:");
            foreach (var item in order.Items)
            {
                message.Append($"- {item.Quantity}x {ResolveProductName(item)}");
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

    private static string ResolveProductName(ActiveOrderItemData item)
    {
        return string.IsNullOrWhiteSpace(item.ProductNameSnapshot)
            ? item.RequestedProductName
            : item.ProductNameSnapshot;
    }

    private static string ResolveSaleType(long totalCents, string? preferredSaleType)
    {
        var normalized = TextNormalizer.NormalizeForLookup(preferredSaleType);
        if (normalized is "varejo" or "atacado")
        {
            return normalized;
        }

        return totalCents > 30000 ? "atacado" : "varejo";
    }

    private static int NormalizeLimit(int? limit)
    {
        return Math.Clamp(limit.GetValueOrDefault(DefaultListLimit), 1, MaxListLimit);
    }

    private static bool IsConfirmation(string message)
    {
        var normalized = TextNormalizer.NormalizeForLookup(message);
        return normalized is "sim" or "confirmo" or "confirma" or "ok" or "pode" or "pode sim" or "isso" or "isso mesmo" or "esta certo" or "ta certo" ||
            normalized.StartsWith("sim ", StringComparison.Ordinal) ||
            normalized.Contains("confirmo", StringComparison.Ordinal) ||
            normalized.Contains("pode registrar", StringComparison.Ordinal) ||
            normalized.Contains("pode fazer", StringComparison.Ordinal) ||
            normalized.Contains("confirmar", StringComparison.Ordinal);
    }

    private static bool IsCancellation(string message)
    {
        var normalized = TextNormalizer.NormalizeForLookup(message);
        return normalized is "nao" or "nao quero" or "cancela" or "cancelar" or "deixa pra la" or "desistir" ||
            normalized.Contains("cancela", StringComparison.Ordinal) ||
            normalized.Contains("nao precisa", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetMissingCustomerFields(CustomerResponse? customer)
    {
        if (customer is null)
        {
            return ["nome", "cpfCnpj", "email", "endereco"];
        }

        var missingFields = new List<string>();
        if (string.IsNullOrWhiteSpace(customer.ClienteNome))
        {
            missingFields.Add("nome");
        }

        if (string.IsNullOrWhiteSpace(customer.CpfCnpj))
        {
            missingFields.Add("cpfCnpj");
        }

        if (string.IsNullOrWhiteSpace(customer.ClienteEmail))
        {
            missingFields.Add("email");
        }

        if (string.IsNullOrWhiteSpace(customer.ClienteEndereco))
        {
            missingFields.Add("endereco");
        }

        return missingFields;
    }

    private static string BuildCustomerRegistrationRequestText(
        CustomerResponse? customer,
        IReadOnlyList<string> missingFields)
    {
        if (customer is null)
        {
            return "Voce ainda nao esta cadastrado no sistema. Para eu finalizar seu pedido com seguranca, poderia me passar nome completo, CPF ou CNPJ, email e endereco de entrega?";
        }

        if (missingFields.Count == 1 && string.Equals(missingFields[0], "endereco", StringComparison.Ordinal))
        {
            return "Encontrei seu cadastro, mas ainda preciso do endereco de entrega para finalizar o pedido. Poderia me enviar o endereco completo?";
        }

        return $"Encontrei seu cadastro, mas preciso completar estes dados antes de finalizar o pedido: {string.Join(", ", missingFields.Select(ToCustomerFieldLabel))}. Poderia me enviar essas informacoes?";
    }

    private static string ToCustomerFieldLabel(string field)
    {
        return field switch
        {
            "nome" => "nome completo",
            "cpfCnpj" => "CPF ou CNPJ",
            "email" => "email",
            "endereco" => "endereco de entrega",
            _ => field
        };
    }

    private static string FormatDate(string date)
    {
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
        {
            return date;
        }

        return parsedDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm", BrazilianCulture);
    }

    private static string FormatMoney(long cents)
    {
        return (cents / 100m).ToString("C", BrazilianCulture);
    }

    private sealed record PendingReorderPayload(
        string SourceDescription,
        string SaleType,
        long EstimatedTotalCents,
        IReadOnlyList<PendingReorderItemPayload> Items,
        string? GeneralObservation);

    private sealed record PendingReorderItemPayload(
        string ProductName,
        int Quantity,
        string? Observation,
        long? UnitPriceCents,
        long? TotalPriceCents);
}
