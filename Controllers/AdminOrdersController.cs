using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private static readonly EmailAddressAttribute EmailValidator = new();
    private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");

    [HttpGet("manage")]
    public async Task<IActionResult> ListManagedOrders(
        [FromQuery] string? storeId,
        [FromQuery] string? status,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid order query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = OrderStatuses.Normalize(status);
            if (normalizedStatus is null)
            {
                return Problem(
                    title: "Invalid order status",
                    detail: "Accepted statuses are PendingReview, EmProducao, EmRotaEntrega and Concluido.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var orders = await repository.GetManagedOrdersAsync(
            storeId.Trim(),
            normalizedStatus,
            cancellationToken);

        return Ok(orders);
    }

    [HttpPatch("{orderId}/status")]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] string orderId,
        [FromBody] UpdateOrderStatusRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Problem(
                title: "Invalid order",
                detail: "orderId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid order",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var status = OrderStatuses.Normalize(request.Status);
        if (status is null)
        {
            return Problem(
                title: "Invalid order status",
                detail: "Accepted statuses are PendingReview, EmProducao, EmRotaEntrega and Concluido.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var updated = await repository.UpdateOrderStatusAsync(
            request.StoreId.Trim(),
            orderId.Trim(),
            status,
            cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    [HttpPost("import-history")]
    public async Task<IActionResult> ImportHistory(
        [FromBody] OrderHistoryImportRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid order history import",
                detail: "StoreId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rows = request.Rows ?? Array.Empty<OrderHistoryImportRowRequest>();
        if (rows.Count == 0)
        {
            return Ok(new OrderHistoryImportResponse(0, 0, 0, Array.Empty<OrderHistoryImportErrorResponse>()));
        }

        var storeId = request.StoreId.Trim();
        var products = await repository.ListProductsAsync(storeId, cancellationToken);
        var productLookup = HistoricalProductLookup.Create(products);
        var preparedRows = new List<PreparedHistoryImportRow>();
        var errors = new List<OrderHistoryImportErrorResponse>();
        var importNow = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            var preparedRow = PrepareHistoryRow(row, productLookup, importNow, errors);
            if (preparedRow is not null)
            {
                preparedRows.Add(preparedRow);
            }
        }

        if (preparedRows.Count == 0)
        {
            return Ok(new OrderHistoryImportResponse(0, 0, 0, errors));
        }

        var groups = preparedRows
            .GroupBy(row => row.PedidoCodigo, StringComparer.Ordinal)
            .ToArray();
        var existingSources = new HashSet<string>(
            await repository.GetExistingOrderSourceMessageIdsAsync(
                storeId,
                groups.Select(group => CreateHistoricalSourceMessageId(group.Key)).ToArray(),
                cancellationToken),
            StringComparer.Ordinal);
        var customers = await repository.ListCustomersAsync(storeId, cancellationToken);
        var customersByPhone = customers.ToDictionary(
            customer => customer.ClienteTelefoneCelular.Trim(),
            customer => customer,
            StringComparer.Ordinal);
        var customersByCpfCnpj = customers
            .Where(customer => !string.IsNullOrWhiteSpace(customer.CpfCnpj))
            .ToDictionary(
                customer => customer.CpfCnpj!.Trim(),
                customer => customer,
                StringComparer.Ordinal);

        var createdOrders = 0;
        var createdItems = 0;
        var skippedOrders = 0;

        foreach (var group in groups)
        {
            var sourceMessageId = CreateHistoricalSourceMessageId(group.Key);
            var groupRows = FilterConsistentGroupRows(group.ToArray(), errors);
            if (groupRows.Count == 0)
            {
                continue;
            }

            if (existingSources.Contains(sourceMessageId))
            {
                skippedOrders++;
                errors.Add(new OrderHistoryImportErrorResponse(
                    groupRows[0].RowNumber,
                    $"Pedido {group.Key} ja foi importado antes e foi ignorado."));
                continue;
            }

            var customerResult = await UpsertImportedCustomerAsync(
                storeId,
                groupRows,
                customersByPhone,
                customersByCpfCnpj,
                repository,
                cancellationToken);
            if (customerResult.Error is not null)
            {
                errors.Add(new OrderHistoryImportErrorResponse(groupRows[0].RowNumber, customerResult.Error));
                continue;
            }

            var orderId = Guid.NewGuid().ToString("N");
            var saleType = groupRows[0].SaleType;
            var items = groupRows
                .Select(row => CreateHistoricalItem(orderId, row, saleType))
                .ToArray();
            var totalCents = items.Sum(item => item.TotalPriceCents ?? 0);
            var createdAtUtc = groupRows[0].CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var aiOutputJson = JsonSerializer.Serialize(new
            {
                source = "historical-import",
                pedidoCodigo = group.Key
            });

            var saveResult = await repository.SaveHistoricalOrderAsync(
                new HistoricalOrderRegistrationData(
                    orderId,
                    storeId,
                    groupRows[0].ClienteTelefoneCelular,
                    sourceMessageId,
                    saleType,
                    null,
                    "Pedido historico importado por Excel.",
                    aiOutputJson,
                    FirstNonEmpty(groupRows.Select(row => row.ObservacaoPedido)),
                    totalCents,
                    createdAtUtc,
                    items),
                cancellationToken);

            if (saveResult.AlreadyExisted)
            {
                skippedOrders++;
                errors.Add(new OrderHistoryImportErrorResponse(
                    groupRows[0].RowNumber,
                    $"Pedido {group.Key} ja foi importado antes e foi ignorado."));
                existingSources.Add(sourceMessageId);
                continue;
            }

            existingSources.Add(sourceMessageId);
            createdOrders++;
            createdItems += items.Length;
        }

        return Ok(new OrderHistoryImportResponse(createdOrders, createdItems, skippedOrders, errors));
    }

    private static PreparedHistoryImportRow? PrepareHistoryRow(
        OrderHistoryImportRowRequest row,
        HistoricalProductLookup productLookup,
        DateTimeOffset importNow,
        ICollection<OrderHistoryImportErrorResponse> errors)
    {
        var pedidoCodigo = NormalizeOptionalText(row.PedidoCodigo);
        if (pedidoCodigo is null)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Informe o PEDIDO_CODIGO."));
            return null;
        }

        var phone = NormalizeOptionalText(row.ClienteTelefoneCelular);
        if (phone is null)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Informe o telefone celular do cliente."));
            return null;
        }

        var productName = NormalizeOptionalText(row.ProdutoNome);
        if (productName is null)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Informe o nome do produto."));
            return null;
        }

        if (row.Quantidade <= 0)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "A quantidade deve ser maior que zero."));
            return null;
        }

        var email = NormalizeOptionalText(row.ClienteEmail);
        if (email is not null && !EmailValidator.IsValid(email))
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Email invalido."));
            return null;
        }

        if (!TryParseImportDate(row.PedidoData, importNow, out var createdAtUtc))
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Data do pedido invalida."));
            return null;
        }

        if (row.PrecoUnitario is < 0)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "O preco unitario nao pode ser negativo."));
            return null;
        }

        var saleType = NormalizeSaleType(row.TipoVenda);
        if (saleType is null)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Tipo de venda invalido. Use varejo ou atacado."));
            return null;
        }

        var match = productLookup.Find(productName);
        if (match.IsAmbiguous)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, "Produto ambiguo no cadastro. Ajuste o nome ou alias do produto."));
            return null;
        }

        if (match.Product is null)
        {
            errors.Add(new OrderHistoryImportErrorResponse(row.RowNumber, $"Produto nao encontrado no cadastro: {productName}."));
            return null;
        }

        return new PreparedHistoryImportRow(
            row.RowNumber,
            pedidoCodigo,
            createdAtUtc,
            NormalizeOptionalText(row.ClienteNome),
            NormalizeOptionalText(row.CpfCnpj),
            email,
            phone,
            saleType,
            productName,
            match.Product,
            row.Quantidade,
            row.PrecoUnitario,
            NormalizeOptionalText(row.ObservacaoItem),
            NormalizeOptionalText(row.ObservacaoPedido));
    }

    private static IReadOnlyList<PreparedHistoryImportRow> FilterConsistentGroupRows(
        IReadOnlyList<PreparedHistoryImportRow> rows,
        ICollection<OrderHistoryImportErrorResponse> errors)
    {
        var first = rows[0];
        var filteredRows = new List<PreparedHistoryImportRow> { first };

        foreach (var row in rows.Skip(1))
        {
            if (!string.Equals(row.ClienteTelefoneCelular, first.ClienteTelefoneCelular, StringComparison.Ordinal))
            {
                errors.Add(new OrderHistoryImportErrorResponse(
                    row.RowNumber,
                    "Mesmo PEDIDO_CODIGO com telefone de cliente diferente. A linha foi ignorada."));
                continue;
            }

            filteredRows.Add(row);
        }

        return filteredRows;
    }

    private static async Task<CustomerImportUpsertResult> UpsertImportedCustomerAsync(
        string storeId,
        IReadOnlyList<PreparedHistoryImportRow> groupRows,
        Dictionary<string, CustomerResponse> customersByPhone,
        Dictionary<string, CustomerResponse> customersByCpfCnpj,
        WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        var phone = groupRows[0].ClienteTelefoneCelular;
        var name = FirstNonEmpty(groupRows.Select(row => row.ClienteNome));
        var cpfCnpj = FirstNonEmpty(groupRows.Select(row => row.CpfCnpj));
        var email = FirstNonEmpty(groupRows.Select(row => row.ClienteEmail));

        customersByPhone.TryGetValue(phone, out var existingByPhone);
        if (cpfCnpj is not null &&
            customersByCpfCnpj.TryGetValue(cpfCnpj, out var existingByCpfCnpj) &&
            (existingByPhone is null || !string.Equals(existingByCpfCnpj.Id, existingByPhone.Id, StringComparison.Ordinal)))
        {
            return new CustomerImportUpsertResult("CPF/CNPJ ja esta cadastrado para outro cliente.");
        }

        var request = new CustomerUpsertRequest(
            storeId,
            name ?? existingByPhone?.ClienteNome,
            cpfCnpj ?? existingByPhone?.CpfCnpj,
            email ?? existingByPhone?.ClienteEmail,
            existingByPhone?.ClienteEndereco,
            phone);

        CustomerSaveResult saveResult;
        if (existingByPhone is null)
        {
            saveResult = await repository.CreateCustomerAsync(request, cancellationToken);
        }
        else
        {
            saveResult = await repository.UpdateCustomerAsync(storeId, existingByPhone.Id, request, cancellationToken);
            RemoveCustomerLookup(customersByCpfCnpj, existingByPhone.CpfCnpj, existingByPhone.Id);
        }

        if (saveResult.Status == CustomerSaveStatus.Conflict || saveResult.Customer is null)
        {
            return new CustomerImportUpsertResult(
                string.Equals(saveResult.ConflictField, "cpfCnpj", StringComparison.OrdinalIgnoreCase)
                    ? "CPF/CNPJ ja esta cadastrado para outro cliente."
                    : "Telefone ja esta cadastrado para outro cliente.");
        }

        customersByPhone[saveResult.Customer.ClienteTelefoneCelular.Trim()] = saveResult.Customer;
        if (!string.IsNullOrWhiteSpace(saveResult.Customer.CpfCnpj))
        {
            customersByCpfCnpj[saveResult.Customer.CpfCnpj!.Trim()] = saveResult.Customer;
        }

        return new CustomerImportUpsertResult(null);
    }

    private static HistoricalOrderItemRegistrationData CreateHistoricalItem(
        string orderId,
        PreparedHistoryImportRow row,
        string saleType)
    {
        var unitPrice = row.PrecoUnitario ??
            (string.Equals(saleType, "atacado", StringComparison.Ordinal)
                ? row.Product.WholesalePrice
                : row.Product.RetailPrice);
        var unitPriceCents = ToCents(unitPrice);

        return new HistoricalOrderItemRegistrationData(
            Guid.NewGuid().ToString("N"),
            orderId,
            row.Product.Id,
            row.ProdutoNome,
            row.Product.Name,
            row.Quantidade,
            unitPriceCents,
            unitPriceCents * row.Quantidade,
            row.ObservacaoItem,
            "Matched");
    }

    private static string CreateHistoricalSourceMessageId(string pedidoCodigo)
    {
        return $"historical-import:{pedidoCodigo.Trim()}";
    }

    private static string? NormalizeSaleType(string? saleType)
    {
        if (string.IsNullOrWhiteSpace(saleType))
        {
            return "varejo";
        }

        return TextNormalizer.NormalizeForLookup(saleType) switch
        {
            "varejo" => "varejo",
            "atacado" => "atacado",
            _ => null
        };
    }

    private static bool TryParseImportDate(string? value, DateTimeOffset fallback, out DateTimeOffset date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = fallback;
            return true;
        }

        foreach (var culture in new[] { PtBrCulture, CultureInfo.InvariantCulture, CultureInfo.CurrentCulture })
        {
            if (DateTimeOffset.TryParse(value.Trim(), culture, DateTimeStyles.AssumeLocal, out date))
            {
                date = date.ToUniversalTime();
                return true;
            }
        }

        date = fallback;
        return false;
    }

    private static long ToCents(decimal value)
    {
        return decimal.ToInt64(decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string? FirstNonEmpty(IEnumerable<string?> values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void RemoveCustomerLookup(Dictionary<string, CustomerResponse> lookup, string? key, string customerId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalizedKey = key.Trim();
        if (lookup.TryGetValue(normalizedKey, out var mappedCustomer) &&
            string.Equals(mappedCustomer.Id, customerId, StringComparison.Ordinal))
        {
            lookup.Remove(normalizedKey);
        }
    }

    private sealed record PreparedHistoryImportRow(
        int RowNumber,
        string PedidoCodigo,
        DateTimeOffset CreatedAtUtc,
        string? ClienteNome,
        string? CpfCnpj,
        string? ClienteEmail,
        string ClienteTelefoneCelular,
        string SaleType,
        string ProdutoNome,
        ProductResponse Product,
        int Quantidade,
        decimal? PrecoUnitario,
        string? ObservacaoItem,
        string? ObservacaoPedido);

    private sealed record CustomerImportUpsertResult(string? Error);

    private sealed class HistoricalProductLookup
    {
        private readonly Dictionary<string, ProductResponse> _productsByKey;
        private readonly HashSet<string> _ambiguousKeys;

        private HistoricalProductLookup(
            Dictionary<string, ProductResponse> productsByKey,
            HashSet<string> ambiguousKeys)
        {
            _productsByKey = productsByKey;
            _ambiguousKeys = ambiguousKeys;
        }

        public static HistoricalProductLookup Create(IReadOnlyList<ProductResponse> products)
        {
            var productsByKey = new Dictionary<string, ProductResponse>(StringComparer.Ordinal);
            var ambiguousKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var product in products)
            {
                AddKey(productsByKey, ambiguousKeys, product.Name, product);
                foreach (var alias in product.Aliases)
                {
                    AddKey(productsByKey, ambiguousKeys, alias, product);
                }
            }

            return new HistoricalProductLookup(productsByKey, ambiguousKeys);
        }

        public ProductMatchResult Find(string productName)
        {
            var normalizedProductName = TextNormalizer.NormalizeForLookup(productName);
            if (_ambiguousKeys.Contains(normalizedProductName))
            {
                return new ProductMatchResult(null, IsAmbiguous: true);
            }

            return _productsByKey.TryGetValue(normalizedProductName, out var product)
                ? new ProductMatchResult(product, false)
                : new ProductMatchResult(null, false);
        }

        private static void AddKey(
            Dictionary<string, ProductResponse> productsByKey,
            HashSet<string> ambiguousKeys,
            string key,
            ProductResponse product)
        {
            var normalizedKey = TextNormalizer.NormalizeForLookup(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return;
            }

            if (productsByKey.TryGetValue(normalizedKey, out var existingProduct) &&
                !string.Equals(existingProduct.Id, product.Id, StringComparison.Ordinal))
            {
                ambiguousKeys.Add(normalizedKey);
                return;
            }

            productsByKey[normalizedKey] = product;
        }
    }

    private sealed record ProductMatchResult(ProductResponse? Product, bool IsAmbiguous);
}
