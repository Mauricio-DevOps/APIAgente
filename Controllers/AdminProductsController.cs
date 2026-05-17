using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/products")]
public sealed class AdminProductsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> UpsertProduct(
        [FromBody] ProductUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateProductRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var product = await repository.UpsertProductAsync(
            request with
            {
                StoreId = request.StoreId.Trim(),
                Name = request.Name.Trim()
            },
            cancellationToken);

        return Ok(product);
    }

    [HttpPut("{productId}")]
    public async Task<IActionResult> UpdateProduct(
        [FromRoute] string productId,
        [FromBody] ProductUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return Problem(
                title: "Invalid product",
                detail: "productId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var validationProblem = ValidateProductRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var result = await repository.UpdateProductAsync(
            request.StoreId.Trim(),
            productId.Trim(),
            request with
            {
                StoreId = request.StoreId.Trim(),
                Name = request.Name.Trim()
            },
            cancellationToken);

        return result.Status switch
        {
            ProductSaveStatus.Saved => Ok(result.Product),
            ProductSaveStatus.Conflict => Problem(
                title: "Duplicate product",
                detail: "Another product already uses this name for the selected store.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound()
        };
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> InactivateProduct(
        [FromRoute] string productId,
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid product",
                detail: "productId and storeId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var updated = await repository.InactivateProductAsync(
            storeId.Trim(),
            productId.Trim(),
            cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    [HttpGet]
    public async Task<IActionResult> ListProducts(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid product query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var products = await repository.ListProductsAsync(storeId.Trim(), cancellationToken);
        return Ok(products);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportProducts(
        [FromBody] ProductImportRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid product import",
                detail: "StoreId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rows = request.Rows ?? Array.Empty<ProductImportRowRequest>();
        if (rows.Count == 0)
        {
            return Ok(new ProductImportResponse(0, 0, 0, Array.Empty<ProductImportErrorResponse>()));
        }

        var storeId = request.StoreId.Trim();
        var products = await repository.ListProductsAsync(storeId, cancellationToken);
        var productsById = products.ToDictionary(product => product.Id, StringComparer.Ordinal);
        var productIdsByName = products.ToDictionary(
            product => TextNormalizer.NormalizeForLookup(product.Name),
            product => product.Id,
            StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<ProductImportErrorResponse>();

        foreach (var row in rows)
        {
            if (string.Equals(row.Action, ProductImportActions.Skip, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (!ProductImportActions.IsKnown(row.Action))
            {
                errors.Add(new ProductImportErrorResponse(row.RowNumber, "Acao de importacao invalida."));
                continue;
            }

            var validationError = ValidateImportRow(row);
            if (validationError is not null)
            {
                errors.Add(new ProductImportErrorResponse(row.RowNumber, validationError));
                continue;
            }

            var normalizedName = TextNormalizer.NormalizeForLookup(row.Name);
            if (string.Equals(row.Action, ProductImportActions.Create, StringComparison.OrdinalIgnoreCase))
            {
                if (productIdsByName.ContainsKey(normalizedName))
                {
                    errors.Add(new ProductImportErrorResponse(row.RowNumber, "Ja existe um produto cadastrado com este nome."));
                    continue;
                }

                var product = await repository.UpsertProductAsync(
                    CreateImportUpsertRequest(storeId, row, existingProduct: null),
                    cancellationToken);

                productsById[product.Id] = product;
                productIdsByName[TextNormalizer.NormalizeForLookup(product.Name)] = product.Id;
                created++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.ProductId) ||
                !productsById.TryGetValue(row.ProductId.Trim(), out var existingProduct))
            {
                errors.Add(new ProductImportErrorResponse(row.RowNumber, "Produto existente nao encontrado para sobrescrever."));
                continue;
            }

            var result = await repository.UpdateProductAsync(
                storeId,
                existingProduct.Id,
                CreateImportUpsertRequest(storeId, row, existingProduct),
                cancellationToken);

            if (result.Status == ProductSaveStatus.Conflict)
            {
                errors.Add(new ProductImportErrorResponse(row.RowNumber, "Outro produto ja usa este nome."));
                continue;
            }

            if (result.Status == ProductSaveStatus.NotFound || result.Product is null)
            {
                errors.Add(new ProductImportErrorResponse(row.RowNumber, "Produto existente nao encontrado para sobrescrever."));
                continue;
            }

            productIdsByName.Remove(TextNormalizer.NormalizeForLookup(existingProduct.Name));
            productsById[result.Product.Id] = result.Product;
            productIdsByName[TextNormalizer.NormalizeForLookup(result.Product.Name)] = result.Product.Id;
            updated++;
        }

        return Ok(new ProductImportResponse(created, updated, skipped, errors));
    }

    private ObjectResult? ValidateProductRequest(ProductUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(
                title: "Invalid product",
                detail: "StoreId and name are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.RetailPrice < 0 || request.WholesalePrice < 0 || request.PromotionalPrice < 0)
        {
            return Problem(
                title: "Invalid product",
                detail: "RetailPrice, promotionalPrice and wholesalePrice cannot be negative.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PromotionalPrice > request.RetailPrice)
        {
            return Problem(
                title: "Invalid product",
                detail: "PromotionalPrice cannot be greater than retailPrice.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.StockQuantity < 0 || request.LowStockThreshold < 0)
        {
            return Problem(
                title: "Invalid product",
                detail: "StockQuantity and lowStockThreshold cannot be negative.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static string? ValidateImportRow(ProductImportRowRequest row)
    {
        if (string.IsNullOrWhiteSpace(row.Name))
        {
            return "Informe o nome do produto.";
        }

        if (row.RetailPrice < 0)
        {
            return "O preco cheio nao pode ser negativo.";
        }

        if (row.PromotionalPrice < 0)
        {
            return "O preco promocional nao pode ser negativo.";
        }

        if (row.PromotionalPrice > row.RetailPrice)
        {
            return "O preco promocional nao pode ser maior que o preco cheio.";
        }

        return null;
    }

    private static ProductUpsertRequest CreateImportUpsertRequest(
        string storeId,
        ProductImportRowRequest row,
        ProductResponse? existingProduct)
    {
        return new ProductUpsertRequest(
            storeId,
            row.Name.Trim(),
            NormalizeOptionalText(row.Description),
            NormalizeOptionalText(row.Type),
            NormalizeOptionalText(row.Brand),
            row.RetailPrice,
            row.PromotionalPrice,
            existingProduct?.WholesalePrice ?? 0,
            existingProduct?.Aliases ?? Array.Empty<string>(),
            existingProduct?.StockQuantity,
            existingProduct?.LowStockThreshold,
            row.IsActive);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
