using System.ComponentModel.DataAnnotations;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/customers")]
public sealed class AdminCustomersController : ControllerBase
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    [HttpGet]
    public async Task<IActionResult> ListCustomers(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid customer query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var customers = await repository.ListCustomersAsync(storeId.Trim(), cancellationToken);
        return Ok(customers);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CustomerUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateCustomerRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var result = await repository.CreateCustomerAsync(NormalizeRequest(request), cancellationToken);
        return result.Status switch
        {
            CustomerSaveStatus.Saved => Ok(result.Customer),
            CustomerSaveStatus.Conflict => Problem(
                title: "Duplicate customer",
                detail: GetConflictDetail(result.ConflictField),
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(
                title: "Customer not saved",
                detail: "Customer could not be saved.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPut("{customerId}")]
    public async Task<IActionResult> UpdateCustomer(
        [FromRoute] string customerId,
        [FromBody] CustomerUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Problem(
                title: "Invalid customer",
                detail: "customerId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var validationProblem = ValidateCustomerRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var normalizedRequest = NormalizeRequest(request);
        var result = await repository.UpdateCustomerAsync(
            normalizedRequest.StoreId,
            customerId.Trim(),
            normalizedRequest,
            cancellationToken);

        return result.Status switch
        {
            CustomerSaveStatus.Saved => Ok(result.Customer),
            CustomerSaveStatus.Conflict => Problem(
                title: "Duplicate customer",
                detail: GetConflictDetail(result.ConflictField),
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound()
        };
    }

    [HttpDelete("{customerId}")]
    public async Task<IActionResult> DeleteCustomer(
        [FromRoute] string customerId,
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid customer",
                detail: "customerId and storeId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var deleted = await repository.DeleteCustomerAsync(
            storeId.Trim(),
            customerId.Trim(),
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportCustomers(
        [FromBody] CustomerImportRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid customer import",
                detail: "StoreId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rows = request.Rows ?? Array.Empty<CustomerImportRowRequest>();
        if (rows.Count == 0)
        {
            return Ok(new CustomerImportResponse(0, 0, 0, Array.Empty<CustomerImportErrorResponse>()));
        }

        var storeId = request.StoreId.Trim();
        var customers = await repository.ListCustomersAsync(storeId, cancellationToken);
        var customersById = customers.ToDictionary(customer => customer.Id, StringComparer.Ordinal);
        var customerIdsByPhone = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var customer in customers)
        {
            AddCustomerPhoneLookup(customerIdsByPhone, customer.ClienteTelefoneCelular, customer.Id);
        }
        var customerIdsByCpfCnpj = customers
            .Where(customer => !string.IsNullOrWhiteSpace(customer.CpfCnpj))
            .ToDictionary(
                customer => customer.CpfCnpj!.Trim(),
                customer => customer.Id,
                StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<CustomerImportErrorResponse>();

        foreach (var row in rows)
        {
            if (string.Equals(row.Action, CustomerImportActions.Skip, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (!CustomerImportActions.IsKnown(row.Action))
            {
                errors.Add(new CustomerImportErrorResponse(row.RowNumber, "Acao de importacao invalida."));
                continue;
            }

            var validationError = ValidateImportRow(row);
            if (validationError is not null)
            {
                errors.Add(new CustomerImportErrorResponse(row.RowNumber, validationError));
                continue;
            }

            var phone = PhoneNumberNormalizer.ToBrazilNationalPhone(row.ClienteTelefoneCelular);
            var cpfCnpj = NormalizeOptionalText(row.CpfCnpj);
            if (string.Equals(row.Action, CustomerImportActions.Create, StringComparison.OrdinalIgnoreCase))
            {
                if (HasCustomerPhone(customerIdsByPhone, phone))
                {
                    errors.Add(new CustomerImportErrorResponse(row.RowNumber, "Ja existe um cliente cadastrado com este telefone."));
                    continue;
                }

                if (cpfCnpj is not null && customerIdsByCpfCnpj.ContainsKey(cpfCnpj))
                {
                    errors.Add(new CustomerImportErrorResponse(row.RowNumber, "Ja existe um cliente cadastrado com este CPF/CNPJ."));
                    continue;
                }

                var createResult = await repository.CreateCustomerAsync(
                    CreateImportUpsertRequest(storeId, row),
                    cancellationToken);

                if (createResult.Status == CustomerSaveStatus.Conflict || createResult.Customer is null)
                {
                    errors.Add(new CustomerImportErrorResponse(row.RowNumber, GetConflictDetail(createResult.ConflictField)));
                    continue;
                }

                customersById[createResult.Customer.Id] = createResult.Customer;
                AddCustomerPhoneLookup(customerIdsByPhone, createResult.Customer.ClienteTelefoneCelular, createResult.Customer.Id);
                if (!string.IsNullOrWhiteSpace(createResult.Customer.CpfCnpj))
                {
                    customerIdsByCpfCnpj[createResult.Customer.CpfCnpj!.Trim()] = createResult.Customer.Id;
                }

                created++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.CustomerId) ||
                !customersById.TryGetValue(row.CustomerId.Trim(), out var existingCustomer))
            {
                errors.Add(new CustomerImportErrorResponse(row.RowNumber, "Cliente existente nao encontrado para sobrescrever."));
                continue;
            }

            var updateResult = await repository.UpdateCustomerAsync(
                storeId,
                existingCustomer.Id,
                CreateImportUpsertRequest(storeId, row),
                cancellationToken);

            if (updateResult.Status == CustomerSaveStatus.Conflict)
            {
                errors.Add(new CustomerImportErrorResponse(row.RowNumber, GetConflictDetail(updateResult.ConflictField)));
                continue;
            }

            if (updateResult.Status == CustomerSaveStatus.NotFound || updateResult.Customer is null)
            {
                errors.Add(new CustomerImportErrorResponse(row.RowNumber, "Cliente existente nao encontrado para sobrescrever."));
                continue;
            }

            RemoveCustomerPhoneLookup(customerIdsByPhone, existingCustomer.ClienteTelefoneCelular, existingCustomer.Id);
            RemoveCustomerLookup(customerIdsByCpfCnpj, existingCustomer.CpfCnpj, existingCustomer.Id);

            customersById[updateResult.Customer.Id] = updateResult.Customer;
            AddCustomerPhoneLookup(customerIdsByPhone, updateResult.Customer.ClienteTelefoneCelular, updateResult.Customer.Id);
            if (!string.IsNullOrWhiteSpace(updateResult.Customer.CpfCnpj))
            {
                customerIdsByCpfCnpj[updateResult.Customer.CpfCnpj!.Trim()] = updateResult.Customer.Id;
            }

            updated++;
        }

        return Ok(new CustomerImportResponse(created, updated, skipped, errors));
    }

    private ObjectResult? ValidateCustomerRequest(CustomerUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(PhoneNumberNormalizer.ToBrazilNationalPhone(request.ClienteTelefoneCelular)))
        {
            return Problem(
                title: "Invalid customer",
                detail: "StoreId and ClienteTelefoneCelular are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var email = NormalizeOptionalText(request.ClienteEmail);
        if (email is not null && !EmailValidator.IsValid(email))
        {
            return Problem(
                title: "Invalid customer",
                detail: "ClienteEmail is invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static string? ValidateImportRow(CustomerImportRowRequest row)
    {
        if (string.IsNullOrWhiteSpace(PhoneNumberNormalizer.ToBrazilNationalPhone(row.ClienteTelefoneCelular)))
        {
            return "Informe o telefone celular do cliente.";
        }

        var email = NormalizeOptionalText(row.ClienteEmail);
        if (email is not null && !EmailValidator.IsValid(email))
        {
            return "Email invalido.";
        }

        return null;
    }

    private static CustomerUpsertRequest CreateImportUpsertRequest(string storeId, CustomerImportRowRequest row)
    {
        return new CustomerUpsertRequest(
            storeId,
            NormalizeOptionalText(row.ClienteNome),
            NormalizeOptionalText(row.CpfCnpj),
            NormalizeOptionalText(row.ClienteEmail),
            NormalizeOptionalText(row.ClienteEndereco),
            PhoneNumberNormalizer.ToBrazilNationalPhone(row.ClienteTelefoneCelular));
    }

    private static CustomerUpsertRequest NormalizeRequest(CustomerUpsertRequest request)
    {
        return new CustomerUpsertRequest(
            request.StoreId.Trim(),
            NormalizeOptionalText(request.ClienteNome),
            NormalizeOptionalText(request.CpfCnpj),
            NormalizeOptionalText(request.ClienteEmail),
            NormalizeOptionalText(request.ClienteEndereco),
            PhoneNumberNormalizer.ToBrazilNationalPhone(request.ClienteTelefoneCelular));
    }

    private static bool HasCustomerPhone(Dictionary<string, string> lookup, string phone)
    {
        return PhoneNumberNormalizer.GetLookupKeys(phone).Any(lookup.ContainsKey);
    }

    private static void AddCustomerPhoneLookup(Dictionary<string, string> lookup, string phone, string customerId)
    {
        foreach (var key in PhoneNumberNormalizer.GetLookupKeys(phone))
        {
            lookup[key] = customerId;
        }
    }

    private static void RemoveCustomerPhoneLookup(Dictionary<string, string> lookup, string? key, string customerId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        foreach (var normalizedKey in PhoneNumberNormalizer.GetLookupKeys(key))
        {
            if (lookup.TryGetValue(normalizedKey, out var mappedCustomerId) &&
                string.Equals(mappedCustomerId, customerId, StringComparison.Ordinal))
            {
                lookup.Remove(normalizedKey);
            }
        }
    }

    private static void RemoveCustomerLookup(Dictionary<string, string> lookup, string? key, string customerId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalizedKey = key.Trim();
        if (lookup.TryGetValue(normalizedKey, out var mappedCustomerId) &&
            string.Equals(mappedCustomerId, customerId, StringComparison.Ordinal))
        {
            lookup.Remove(normalizedKey);
        }
    }

    private static string GetConflictDetail(string? field)
    {
        return string.Equals(field, "cpfCnpj", StringComparison.OrdinalIgnoreCase)
            ? "Another customer already uses this CPF/CNPJ for the selected store."
            : "Another customer already uses this phone number for the selected store.";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
