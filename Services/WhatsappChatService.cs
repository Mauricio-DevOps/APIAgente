using System.Globalization;
using System.Text;
using System.Text.Json;
using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public sealed class WhatsappChatService
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxPromptProductDescriptionChars = 12000;
    private static readonly IReadOnlyList<string> AllRequiredCustomerFields =
    [
        "nome",
        "cpfCnpj",
        "email",
        "endereco"
    ];

    private const string CustomerStatusComplete = "CADASTRADO_COMPLETO";
    private const string CustomerStatusIncomplete = "CADASTRADO_INCOMPLETO";
    private const string CustomerStatusNotRegistered = "NAO_CADASTRADO";

    private readonly WhatsappRepository _repository;
    private readonly PromptApiClient _promptApiClient;
    private readonly OrderRegistrationService _orderRegistrationService;
    private readonly OrderConsultationService _orderConsultationService;
    private readonly CustomerHistoryConsultationService _customerHistoryConsultationService;
    private readonly HumanHandoffService _humanHandoffService;
    private readonly AgentFeedbackService _agentFeedbackService;

    public WhatsappChatService(
        WhatsappRepository repository,
        PromptApiClient promptApiClient,
        OrderRegistrationService orderRegistrationService,
        OrderConsultationService orderConsultationService,
        CustomerHistoryConsultationService customerHistoryConsultationService,
        HumanHandoffService humanHandoffService,
        AgentFeedbackService agentFeedbackService)
    {
        _repository = repository;
        _promptApiClient = promptApiClient;
        _orderRegistrationService = orderRegistrationService;
        _orderConsultationService = orderConsultationService;
        _customerHistoryConsultationService = customerHistoryConsultationService;
        _humanHandoffService = humanHandoffService;
        _agentFeedbackService = agentFeedbackService;
    }

    public async Task<string> ProcessAsync(
        string message,
        string phoneNumber,
        string storeId,
        CancellationToken cancellationToken)
    {
        return await ProcessAsync(
            message,
            phoneNumber,
            storeId,
            sourceMessageId: null,
            cancellationToken);
    }

    public async Task<string> ProcessAsync(
        string message,
        string phoneNumber,
        string storeId,
        string? sourceMessageId,
        CancellationToken cancellationToken)
    {
        var promptId = await _repository.GetPromptIdAsync(storeId, cancellationToken);
        if (promptId is null)
        {
            throw new InvalidOperationException($"No prompt mapping was found for storeId '{storeId}'.");
        }

        var pendingActionResponse = await _customerHistoryConsultationService.TryHandlePendingActionAsync(
            storeId,
            phoneNumber,
            message,
            sourceMessageId,
            cancellationToken);
        if (pendingActionResponse is not null)
        {
            return pendingActionResponse;
        }

        var conversation = await _repository.GetConversationAsync(storeId, phoneNumber, cancellationToken);

        var conversationId = string.IsNullOrWhiteSpace(conversation?.ConversationId)
            ? null
            : conversation.ConversationId;

        var products = await _repository.GetActiveProductCatalogAsync(storeId, cancellationToken);
        var persona = await _repository.GetAgentPersonaAsync(storeId, cancellationToken, activeFaqsOnly: true);
        var customerContext = await CreateCustomerContextAsync(storeId, phoneNumber, cancellationToken);
        var productDetails = await _repository.SearchActiveProductDetailsByNameAsync(
            storeId,
            message,
            limit: 3,
            cancellationToken);
        var messageWithCatalog = BuildMessageWithContext(message, products, productDetails, persona, customerContext);

        var externalResponse = await _promptApiClient.SendMessageAsync(
            promptId,
            new PromptApiMessageRequest(messageWithCatalog, conversationId),
            cancellationToken);

        await _repository.UpsertConversationAsync(
            storeId,
            phoneNumber,
            externalResponse.ConversationId,
            externalResponse.ResponseId,
            cancellationToken);

        var outputTextResponse = DeserializeOutputText(externalResponse.OutputText);
        switch (outputTextResponse.Tipo)
        {
            case 2:
                customerContext = await CreateCustomerContextAsync(storeId, phoneNumber, cancellationToken);
                if (customerContext.MissingFields.Count > 0)
                {
                    return BuildCustomerRegistrationRequestText(customerContext.Customer, customerContext.MissingFields);
                }

                var orderPayload = EnsureOrderHasCustomerAddress(outputTextResponse.Pedido, customerContext.Customer);
                var orderRegistration = await _orderRegistrationService.RegistrarPedidoAsync(
                    new OrderRegistrationCommand(
                        storeId,
                        phoneNumber,
                        string.IsNullOrWhiteSpace(sourceMessageId) ? externalResponse.ResponseId : sourceMessageId,
                        externalResponse.ResponseId,
                        externalResponse.ConversationId,
                        message,
                        outputTextResponse.Texto,
                        externalResponse.OutputText,
                        orderPayload),
                    cancellationToken);

                return BuildOrderResponseText(outputTextResponse.Texto, orderRegistration);

            case 3:
                return await _orderConsultationService.ConsultarPedidosAtivosAsync(
                    storeId,
                    phoneNumber,
                    outputTextResponse.Texto,
                    cancellationToken);

            case 4:
                return await _humanHandoffService.SolicitarAtendimentoHumanoAsync(
                    storeId,
                    phoneNumber,
                    message,
                    cancellationToken);

            case 5:
                await _agentFeedbackService.RecordDetectedFeedbackAsync(
                    storeId,
                    phoneNumber,
                    message,
                    outputTextResponse.Texto,
                    externalResponse.OutputText,
                    externalResponse.ResponseId,
                    externalResponse.ConversationId,
                    outputTextResponse.Feedback,
                    cancellationToken);

                return outputTextResponse.Texto;

            case 6:
                return await HandleCustomerRegistrationAsync(
                    storeId,
                    phoneNumber,
                    outputTextResponse,
                    cancellationToken);

            case 7:
                return await _customerHistoryConsultationService.ConsultarHistoricoClienteAsync(
                    storeId,
                    phoneNumber,
                    outputTextResponse.ConsultaCliente,
                    outputTextResponse.Texto,
                    cancellationToken);

            default:
                return outputTextResponse.Texto;
        }
    }

    public async Task<string> ProcessSolicitedFeedbackResponseAsync(
        string message,
        string phoneNumber,
        string storeId,
        string feedbackSolicitationId,
        CancellationToken cancellationToken)
    {
        var promptId = await _repository.GetPromptIdAsync(storeId, cancellationToken);
        if (promptId is null)
        {
            throw new InvalidOperationException($"No prompt mapping was found for storeId '{storeId}'.");
        }

        var conversation = await _repository.GetConversationAsync(storeId, phoneNumber, cancellationToken);
        var conversationId = string.IsNullOrWhiteSpace(conversation?.ConversationId)
            ? null
            : conversation.ConversationId;

        var messageWithContext = BuildSolicitedFeedbackMessage(message);
        var externalResponse = await _promptApiClient.SendMessageAsync(
            promptId,
            new PromptApiMessageRequest(messageWithContext, conversationId),
            cancellationToken);

        await _repository.UpsertConversationAsync(
            storeId,
            phoneNumber,
            externalResponse.ConversationId,
            externalResponse.ResponseId,
            cancellationToken);

        var outputTextResponse = DeserializeOutputText(externalResponse.OutputText);
        var responseText = outputTextResponse.Tipo == 5
            ? outputTextResponse.Texto
            : "Obrigado pelo feedback! Registramos sua resposta.";

        await _agentFeedbackService.RecordSolicitedTextFeedbackAsync(
            feedbackSolicitationId,
            storeId,
            phoneNumber,
            message,
            outputTextResponse.Texto,
            externalResponse.OutputText,
            externalResponse.ResponseId,
            externalResponse.ConversationId,
            outputTextResponse.Tipo == 5 ? outputTextResponse.Feedback : null,
            cancellationToken);

        return responseText;
    }

    private static string BuildMessageWithContext(
        string message,
        IReadOnlyList<ProductCatalogItem> products,
        IReadOnlyList<ProductResponse> productDetails,
        AgentPersonaSettingsResponse persona,
        CustomerContext customerContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[CONTEXTO DINAMICO - PERSONA DO AGENTE]");
        builder.AppendLine($"Tom de voz: {AgentPersonaTones.ToLabel(persona.Tone)}.");
        if (!string.IsNullOrWhiteSpace(persona.CustomInstructions))
        {
            builder.AppendLine("Instrucoes personalizadas:");
            builder.AppendLine(persona.CustomInstructions.Trim());
        }
        else
        {
            builder.AppendLine("Instrucoes personalizadas: nenhuma.");
        }

        builder.AppendLine();
        builder.AppendLine("[FAQS ATIVAS]");
        if (persona.Faqs.Count == 0)
        {
            builder.AppendLine("Nenhuma FAQ ativa cadastrada.");
        }
        else
        {
            foreach (var faq in persona.Faqs.OrderBy(faq => faq.SortOrder))
            {
                builder.AppendLine($"Pergunta: {faq.Question}");
                builder.AppendLine($"Resposta: {faq.Answer}");
                builder.AppendLine("---");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[CONTEXTO DINAMICO - CLIENTE]");
        builder.AppendLine($"Status do cadastro: {customerContext.Status}.");
        builder.AppendLine($"Telefone WhatsApp: {customerContext.PhoneNumber}.");
        if (customerContext.Customer is null)
        {
            builder.AppendLine("Cliente nao encontrado na base desta empresa.");
        }
        else
        {
            builder.AppendLine($"Nome: {FormatOptionalCatalogText(customerContext.Customer.ClienteNome, "Nao informado.")}");
            builder.AppendLine($"CPF/CNPJ: {FormatOptionalCatalogText(customerContext.Customer.CpfCnpj, "Nao informado.")}");
            builder.AppendLine($"Email: {FormatOptionalCatalogText(customerContext.Customer.ClienteEmail, "Nao informado.")}");
            builder.AppendLine($"Endereco: {FormatOptionalCatalogText(customerContext.Customer.ClienteEndereco, "Nao informado.")}");
        }

        builder.AppendLine($"Campos cadastrais faltantes: {FormatMissingCustomerFields(customerContext.MissingFields)}.");
        builder.AppendLine("Antes de finalizar pedido, use tipo 6 se o status do cadastro for NAO_CADASTRADO ou CADASTRADO_INCOMPLETO. Nao peca telefone, pois o telefone vem do WhatsApp.");
        builder.AppendLine("Formato tipo 6 para solicitar dados: {\"texto\":\"mensagem para coletar dados\",\"tipo\":6,\"cadastro\":{\"acao\":\"SOLICITAR_DADOS\",\"camposFaltantes\":[\"nome\",\"cpfCnpj\",\"email\",\"endereco\"],\"cliente\":null}}");
        builder.AppendLine("Formato tipo 6 para salvar cadastro: {\"texto\":\"mensagem informando cadastro salvo, resumo do pedido e pedido de confirmacao\",\"tipo\":6,\"cadastro\":{\"acao\":\"SALVAR_CADASTRO\",\"camposFaltantes\":[],\"cliente\":{\"nome\":\"Nome do cliente\",\"cpfCnpj\":\"00000000000\",\"email\":\"cliente@email.com\",\"endereco\":\"Rua Exemplo, 123\"}}}");
        builder.AppendLine("Depois de salvar cadastro por tipo 6, nao finalize o pedido na mesma resposta. Aguarde a confirmacao seguinte para usar tipo 2.");

        builder.AppendLine();
        builder.AppendLine("[CONTEXTO DINAMICO - CATALOGO DA EMPRESA]");
        builder.AppendLine("Use esta lista como fonte atual de nomes de produtos ativos. Ela traz somente nomes para manter o contexto leve.");
        builder.AppendLine();

        if (products.Count == 0)
        {
            builder.AppendLine("Nenhum produto ativo cadastrado.");
        }
        else
        {
            foreach (var product in products.OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Produto: {product.Name}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[DETALHES DE PRODUTOS MENCIONADOS]");
        builder.AppendLine("Detalhes completos so aparecem quando a mensagem do cliente cita um produto especifico.");
        if (productDetails.Count == 0)
        {
            builder.AppendLine("Nenhum produto especifico identificado na mensagem atual.");
        }
        else
        {
            foreach (var product in productDetails)
            {
                builder.AppendLine($"Produto: {product.Name}");
                builder.AppendLine($"Tipo: {FormatOptionalCatalogText(product.Type, "Sem tipo cadastrado.")}");
                builder.AppendLine($"Marca: {FormatOptionalCatalogText(product.Brand, "Sem marca cadastrada.")}");
                builder.AppendLine($"Descricao: {FormatPromptProductDescription(product.Description)}");
                builder.AppendLine($"Preco varejo: {FormatPrice(ToCents(product.RetailPrice))}");
                builder.AppendLine($"Preco promocional: {FormatOptionalPrice(product.PromotionalPrice is null ? null : ToCents(product.PromotionalPrice.Value))}");
                builder.AppendLine($"Preco atacado: {FormatPrice(ToCents(product.WholesalePrice))}");
                builder.AppendLine($"Aliases: {FormatAliases(product.Aliases)}");
                builder.AppendLine("---");
            }
        }

        builder.AppendLine("[MENSAGEM DO CLIENTE]");
        builder.Append(message);
        return builder.ToString();
    }

    private static string BuildSolicitedFeedbackMessage(string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[CONTEXTO DINAMICO - FEEDBACK SOLICITADO]");
        builder.AppendLine("A mensagem abaixo e uma resposta de cliente a uma solicitacao de feedback.");
        builder.AppendLine("Nao registre pedido, nao consulte pedido e nao acione atendimento humano neste fluxo.");
        builder.AppendLine("Retorne um JSON com tipo 5 e, quando possivel, preencha feedback.categoria, feedback.sentimento, feedback.classificacaoCliente, feedback.pontuacao e feedback.resumo.");
        builder.AppendLine();
        builder.AppendLine("[MENSAGEM DO CLIENTE]");
        builder.Append(message);
        return builder.ToString();
    }

    private static string BuildOrderResponseText(string responseText, OrderRegistrationResult orderRegistration)
    {
        if (!string.IsNullOrWhiteSpace(orderRegistration.PaymentCheckoutUrl))
        {
            return string.Concat(
                responseText.Trim(),
                "\n\nPara finalizar, pague seu pedido por este link: ",
                orderRegistration.PaymentCheckoutUrl.Trim());
        }

        if (!string.IsNullOrWhiteSpace(orderRegistration.PaymentMessage))
        {
            return string.Concat(
                responseText.Trim(),
                "\n\n",
                orderRegistration.PaymentMessage.Trim());
        }

        return responseText;
    }

    private static string FormatDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? "Sem descricao cadastrada."
            : description.Trim();
    }

    private static string FormatPromptProductDescription(string? description)
    {
        var formatted = FormatDescription(description);
        return formatted.Length <= MaxPromptProductDescriptionChars
            ? formatted
            : formatted[..MaxPromptProductDescriptionChars] + "... [descricao truncada para manter o contexto leve]";
    }

    private static string FormatOptionalCatalogText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatAliases(IReadOnlyList<ProductAliasCatalogItem> aliases)
    {
        return aliases.Count == 0
            ? "Sem aliases cadastrados."
            : string.Join(", ", aliases.Select(alias => alias.Alias));
    }

    private static string FormatAliases(IReadOnlyList<string> aliases)
    {
        return aliases.Count == 0
            ? "Sem aliases cadastrados."
            : string.Join(", ", aliases);
    }

    private static string FormatPrice(long priceCents)
    {
        return $"R$ {(priceCents / 100m).ToString("N2", BrazilianCulture)}";
    }

    private static string FormatOptionalPrice(long? priceCents)
    {
        return priceCents is null ? "Sem preco promocional cadastrado." : FormatPrice(priceCents.Value);
    }

    private static long ToCents(decimal value)
    {
        return decimal.ToInt64(decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private async Task<CustomerContext> CreateCustomerContextAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var customer = await _repository.FindCustomerByPhoneAsync(storeId, phoneNumber, cancellationToken);
        return CreateCustomerContext(customer, phoneNumber);
    }

    private async Task<string> HandleCustomerRegistrationAsync(
        string storeId,
        string phoneNumber,
        PromptOutputTextResponse outputTextResponse,
        CancellationToken cancellationToken)
    {
        var action = NormalizeRegistrationAction(outputTextResponse.Cadastro?.Acao);
        if (!string.Equals(action, CustomerRegistrationActions.SalvarCadastro, StringComparison.Ordinal))
        {
            return outputTextResponse.Texto;
        }

        var existingCustomer = await _repository.FindCustomerByPhoneAsync(storeId, phoneNumber, cancellationToken);
        var draft = MergeCustomerRegistrationDraft(existingCustomer, outputTextResponse.Cadastro?.Cliente);
        var missingFields = GetMissingCustomerFields(draft);
        if (missingFields.Count > 0)
        {
            return BuildCustomerRegistrationRequestText(existingCustomer, missingFields);
        }

        var request = new CustomerUpsertRequest(
            storeId.Trim(),
            draft.Nome,
            draft.CpfCnpj,
            draft.Email,
            draft.Endereco,
            existingCustomer?.ClienteTelefoneCelular ?? PhoneNumberNormalizer.ToBrazilNationalPhone(phoneNumber));

        var result = existingCustomer is null
            ? await _repository.CreateCustomerAsync(request, cancellationToken)
            : await _repository.UpdateCustomerAsync(storeId, existingCustomer.Id, request, cancellationToken);

        if (result.Status == CustomerSaveStatus.Saved)
        {
            return outputTextResponse.Texto;
        }

        var conflictFields = string.Equals(result.ConflictField, "cpfCnpj", StringComparison.OrdinalIgnoreCase)
            ? new[] { "cpfCnpj" }
            : Array.Empty<string>();
        var conflictFieldText = conflictFields.Length == 0
            ? "esses dados"
            : string.Join(", ", conflictFields.Select(ToCustomerFieldLabel));
        return $"Nao consegui salvar o cadastro porque ja existe outro cliente com {conflictFieldText}. Poderia revisar as informacoes ou pedir atendimento humano?";
    }

    private static CustomerContext CreateCustomerContext(CustomerResponse? customer, string phoneNumber)
    {
        var missingFields = GetMissingCustomerFields(customer);
        var status = customer is null
            ? CustomerStatusNotRegistered
            : missingFields.Count == 0
                ? CustomerStatusComplete
                : CustomerStatusIncomplete;

        return new CustomerContext(customer, phoneNumber.Trim(), status, missingFields);
    }

    private static CustomerRegistrationDraft MergeCustomerRegistrationDraft(
        CustomerResponse? existingCustomer,
        PromptCustomerRegistrationCustomerPayload? registrationCustomer)
    {
        return new CustomerRegistrationDraft(
            FirstNonEmpty(registrationCustomer?.Nome, existingCustomer?.ClienteNome),
            FirstNonEmpty(registrationCustomer?.CpfCnpj, existingCustomer?.CpfCnpj),
            FirstNonEmpty(registrationCustomer?.Email, existingCustomer?.ClienteEmail),
            FirstNonEmpty(registrationCustomer?.Endereco, existingCustomer?.ClienteEndereco));
    }

    private static IReadOnlyList<string> GetMissingCustomerFields(CustomerResponse? customer)
    {
        if (customer is null)
        {
            return AllRequiredCustomerFields;
        }

        return GetMissingCustomerFields(new CustomerRegistrationDraft(
            customer.ClienteNome,
            customer.CpfCnpj,
            customer.ClienteEmail,
            customer.ClienteEndereco));
    }

    private static IReadOnlyList<string> GetMissingCustomerFields(CustomerRegistrationDraft draft)
    {
        var missingFields = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Nome))
        {
            missingFields.Add("nome");
        }

        if (string.IsNullOrWhiteSpace(draft.CpfCnpj))
        {
            missingFields.Add("cpfCnpj");
        }

        if (string.IsNullOrWhiteSpace(draft.Email))
        {
            missingFields.Add("email");
        }

        if (string.IsNullOrWhiteSpace(draft.Endereco))
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

        var fields = string.Join(", ", missingFields.Select(ToCustomerFieldLabel));
        return $"Encontrei seu cadastro, mas preciso completar estes dados antes de finalizar o pedido: {fields}. Poderia me enviar essas informacoes?";
    }

    private static PromptOrderPayload? EnsureOrderHasCustomerAddress(
        PromptOrderPayload? orderPayload,
        CustomerResponse? customer)
    {
        if (orderPayload is null || string.IsNullOrWhiteSpace(customer?.ClienteEndereco))
        {
            return orderPayload;
        }

        var address = customer.ClienteEndereco.Trim();
        var addressObservation = $"Endereco de entrega: {address}";
        var observation = NormalizeOptionalText(orderPayload.ObservacaoGeral);
        if (observation is null)
        {
            return orderPayload with { ObservacaoGeral = addressObservation };
        }

        if (observation.Contains(address, StringComparison.OrdinalIgnoreCase))
        {
            return orderPayload;
        }

        return orderPayload with { ObservacaoGeral = $"{observation}\n{addressObservation}" };
    }

    private static string? NormalizeRegistrationAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || !CustomerRegistrationActions.IsKnown(action))
        {
            return null;
        }

        return string.Equals(action, CustomerRegistrationActions.SalvarCadastro, StringComparison.OrdinalIgnoreCase)
            ? CustomerRegistrationActions.SalvarCadastro
            : CustomerRegistrationActions.SolicitarDados;
    }

    private static string FormatMissingCustomerFields(IReadOnlyList<string> missingFields)
    {
        return missingFields.Count == 0 ? "nenhum" : string.Join(", ", missingFields);
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

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? FirstNonEmpty(string? first, string? second)
    {
        return NormalizeOptionalText(first) ?? NormalizeOptionalText(second);
    }

    private static PromptOutputTextResponse DeserializeOutputText(string outputText)
    {
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new ExternalApiException(StatusCodes.Status502BadGateway, "Prompt API returned an empty outputText.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PromptOutputTextPayload>(
                outputText,
                JsonOptions);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Texto) || payload.Tipo is null)
            {
                throw new ExternalApiException(StatusCodes.Status502BadGateway, "Prompt API returned an invalid outputText.");
            }

            return new PromptOutputTextResponse(
                payload.Texto,
                payload.Tipo.Value,
                payload.Pedido,
                payload.Feedback,
                payload.Cadastro,
                payload.ConsultaCliente);
        }
        catch (JsonException ex)
        {
            throw new ExternalApiException(
                StatusCodes.Status502BadGateway,
                $"Prompt API returned an outputText that is not valid JSON. {ex.Message}");
        }
    }

    private sealed record PromptOutputTextPayload(
        string? Texto,
        int? Tipo,
        PromptOrderPayload? Pedido,
        PromptFeedbackPayload? Feedback,
        PromptCustomerRegistrationPayload? Cadastro,
        PromptCustomerHistoryQueryPayload? ConsultaCliente);

    private sealed record CustomerContext(
        CustomerResponse? Customer,
        string PhoneNumber,
        string Status,
        IReadOnlyList<string> MissingFields);

    private sealed record CustomerRegistrationDraft(
        string? Nome,
        string? CpfCnpj,
        string? Email,
        string? Endereco);
}
