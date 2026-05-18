using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public sealed class OrderRegistrationService
{
    private readonly WhatsappRepository _repository;

    public OrderRegistrationService(WhatsappRepository repository)
    {
        _repository = repository;
    }

    public async Task<OrderRegistrationResult> RegistrarPedidoAsync(
        OrderRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        var storeId = command.StoreId.Trim();
        var conversationPhoneNumber = command.PhoneNumber.Trim();
        var orderPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(conversationPhoneNumber);
        var sourceMessageId = ResolveSourceMessageId(command);
        var orderId = Guid.NewGuid().ToString("N");
        var saleType = NormalizeSaleType(command.Pedido?.TipoVenda);
        var hasPendingIssue = saleType is null;

        var products = await _repository.GetActiveProductCatalogAsync(storeId, cancellationToken);
        var lookup = ProductLookup.Create(products);
        var items = new List<OrderItemRegistrationData>();

        if (command.Pedido?.Itens is null || command.Pedido.Itens.Count == 0)
        {
            hasPendingIssue = true;
        }
        else
        {
            foreach (var requestedItem in command.Pedido.Itens)
            {
                var item = CreateOrderItem(
                    orderId,
                    requestedItem,
                    saleType,
                    lookup,
                    out var itemHasPendingIssue);

                items.Add(item);
                hasPendingIssue = hasPendingIssue || itemHasPendingIssue;
            }
        }

        var status = hasPendingIssue ? OrderStatuses.PendingReview : OrderStatuses.EmProducao;
        var totalCents = hasPendingIssue
            ? 0
            : items.Sum(item => item.TotalPriceCents ?? 0);

        var order = new OrderRegistrationData(
            orderId,
            storeId,
            orderPhoneNumber,
            sourceMessageId,
            string.IsNullOrWhiteSpace(command.PromptResponseId) ? null : command.PromptResponseId.Trim(),
            string.IsNullOrWhiteSpace(command.ConversationId) ? null : command.ConversationId.Trim(),
            saleType,
            status,
            string.IsNullOrWhiteSpace(command.CustomerMessage) ? null : command.CustomerMessage.Trim(),
            command.AiResponseText.Trim(),
            command.AiOutputJson,
            string.IsNullOrWhiteSpace(command.Pedido?.ObservacaoGeral) ? null : command.Pedido.ObservacaoGeral.Trim(),
            totalCents,
            items);

        var result = await _repository.SaveOrderAsync(order, cancellationToken);

        await _repository.ClearConversationAsync(storeId, conversationPhoneNumber, cancellationToken);

        return result;
    }

    private static OrderItemRegistrationData CreateOrderItem(
        string orderId,
        PromptOrderItemPayload requestedItem,
        string? saleType,
        ProductLookup lookup,
        out bool hasPendingIssue)
    {
        hasPendingIssue = false;

        var requestedProductName = requestedItem.Produto?.Trim() ?? string.Empty;
        var quantity = requestedItem.Quantidade.GetValueOrDefault();
        var observation = string.IsNullOrWhiteSpace(requestedItem.Observacao)
            ? null
            : requestedItem.Observacao.Trim();

        if (string.IsNullOrWhiteSpace(requestedProductName))
        {
            hasPendingIssue = true;
            return CreatePendingItem(orderId, requestedProductName, quantity, observation, "InvalidProduct");
        }

        if (quantity <= 0)
        {
            hasPendingIssue = true;
            return CreatePendingItem(orderId, requestedProductName, quantity, observation, "InvalidQuantity");
        }

        var normalizedProductName = TextNormalizer.NormalizeForLookup(requestedProductName);
        var matchResult = lookup.Find(normalizedProductName);
        if (matchResult.IsAmbiguous)
        {
            hasPendingIssue = true;
            return CreatePendingItem(orderId, requestedProductName, quantity, observation, "Ambiguous");
        }

        if (matchResult.Product is null)
        {
            hasPendingIssue = true;
            return CreatePendingItem(orderId, requestedProductName, quantity, observation, "NotFound");
        }

        if (saleType is null)
        {
            hasPendingIssue = true;
            return new OrderItemRegistrationData(
                Guid.NewGuid().ToString("N"),
                orderId,
                matchResult.Product.Id,
                requestedProductName,
                matchResult.Product.Name,
                quantity,
                null,
                null,
                observation,
                "MatchedMissingSaleType");
        }

        var unitPriceCents = saleType == "varejo"
            ? matchResult.Product.RetailPriceCents
            : matchResult.Product.WholesalePriceCents;

        return new OrderItemRegistrationData(
            Guid.NewGuid().ToString("N"),
            orderId,
            matchResult.Product.Id,
            requestedProductName,
            matchResult.Product.Name,
            quantity,
            unitPriceCents,
            unitPriceCents * quantity,
            observation,
            "Matched");
    }

    private static OrderItemRegistrationData CreatePendingItem(
        string orderId,
        string requestedProductName,
        int quantity,
        string? observation,
        string matchStatus)
    {
        return new OrderItemRegistrationData(
            Guid.NewGuid().ToString("N"),
            orderId,
            null,
            requestedProductName,
            null,
            quantity,
            null,
            null,
            observation,
            matchStatus);
    }

    private static string ResolveSourceMessageId(OrderRegistrationCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.SourceMessageId))
        {
            return command.SourceMessageId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(command.PromptResponseId))
        {
            return command.PromptResponseId.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string? NormalizeSaleType(string? saleType)
    {
        var normalized = TextNormalizer.NormalizeForLookup(saleType);
        return normalized switch
        {
            "varejo" => "varejo",
            "atacado" => "atacado",
            _ => null
        };
    }

    private sealed class ProductLookup
    {
        private readonly Dictionary<string, ProductCatalogItem> _productsByKey;
        private readonly HashSet<string> _ambiguousKeys;

        private ProductLookup(
            Dictionary<string, ProductCatalogItem> productsByKey,
            HashSet<string> ambiguousKeys)
        {
            _productsByKey = productsByKey;
            _ambiguousKeys = ambiguousKeys;
        }

        public static ProductLookup Create(IReadOnlyList<ProductCatalogItem> products)
        {
            var productsByKey = new Dictionary<string, ProductCatalogItem>(StringComparer.Ordinal);
            var ambiguousKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var product in products)
            {
                AddKey(productsByKey, ambiguousKeys, product.NormalizedName, product);

                foreach (var alias in product.Aliases)
                {
                    AddKey(productsByKey, ambiguousKeys, alias.NormalizedAlias, product);
                }
            }

            return new ProductLookup(productsByKey, ambiguousKeys);
        }

        public ProductMatchResult Find(string normalizedProductName)
        {
            if (_ambiguousKeys.Contains(normalizedProductName))
            {
                return new ProductMatchResult(null, IsAmbiguous: true);
            }

            return _productsByKey.TryGetValue(normalizedProductName, out var product)
                ? new ProductMatchResult(product, false)
                : new ProductMatchResult(null, false);
        }

        private static void AddKey(
            Dictionary<string, ProductCatalogItem> productsByKey,
            HashSet<string> ambiguousKeys,
            string key,
            ProductCatalogItem product)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (productsByKey.TryGetValue(key, out var existingProduct) &&
                !string.Equals(existingProduct.Id, product.Id, StringComparison.Ordinal))
            {
                ambiguousKeys.Add(key);
                return;
            }

            productsByKey[key] = product;
        }
    }

    private sealed record ProductMatchResult(ProductCatalogItem? Product, bool IsAmbiguous);
}
