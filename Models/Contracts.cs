using System.ComponentModel.DataAnnotations;

namespace AtendenteWhatssApp.Models;

public sealed record ChatWhatsappRequest(
    [param: Required] string Message,
    [param: Required] string PhoneNumber,
    [param: Required] string StoreId);

public sealed record StorePromptUpsertRequest(
    [param: Required] string StoreId,
    [param: Required] string PromptId);

public sealed record TwilioWhatsappWebhookRequest(
    string? Body,
    string? From,
    string? To,
    string? MessageSid,
    int? NumMedia,
    string? MediaUrl0,
    string? MediaContentType0);

public sealed record PromptApiMessageRequest(string Message, string? ConversationId);

public sealed record PromptApiMessageResponse(string ResponseId, string? ConversationId, string OutputText);

public sealed record PromptOutputTextResponse(
    string Texto,
    int Tipo,
    PromptOrderPayload? Pedido,
    PromptFeedbackPayload? Feedback,
    PromptCustomerRegistrationPayload? Cadastro,
    PromptCustomerHistoryQueryPayload? ConsultaCliente);

public sealed record PromptFeedbackPayload(
    string? Categoria,
    string? Sentimento,
    string? ClassificacaoCliente,
    int? Pontuacao,
    string? Resumo);

public sealed record PromptOrderPayload(
    string? TipoVenda,
    IReadOnlyList<PromptOrderItemPayload>? Itens,
    string? ObservacaoGeral);

public sealed record PromptOrderItemPayload(string? Produto, int? Quantidade, string? Observacao);

public sealed record PromptCustomerRegistrationPayload(
    string? Acao,
    IReadOnlyList<string>? CamposFaltantes,
    PromptCustomerRegistrationCustomerPayload? Cliente);

public sealed record PromptCustomerRegistrationCustomerPayload(
    string? Nome,
    string? CpfCnpj,
    string? Email,
    string? Endereco);

public sealed record PromptCustomerHistoryQueryPayload(
    string? Intencao,
    string? Acao,
    string? ProdutoReferencia,
    int? Limite);

public static class CustomerRegistrationActions
{
    public const string SolicitarDados = "SOLICITAR_DADOS";
    public const string SalvarCadastro = "SALVAR_CADASTRO";

    public static bool IsKnown(string? value)
    {
        return string.Equals(value, SolicitarDados, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, SalvarCadastro, StringComparison.OrdinalIgnoreCase);
    }
}

public static class CustomerHistoryQueryIntentions
{
    public const string ItensJaComprados = "ITENS_JA_COMPRADOS";
    public const string ItemMaisComprado = "ITEM_MAIS_COMPRADO";
    public const string ItemMaisCaroComprado = "ITEM_MAIS_CARO_COMPRADO";
    public const string UltimoPedido = "ULTIMO_PEDIDO";
    public const string ReplicarUltimoPedido = "REPLICAR_ULTIMO_PEDIDO";
    public const string RecomprarItemHistorico = "RECOMPRAR_ITEM_HISTORICO";
    public const string VerificarSeJaComprouItem = "VERIFICAR_SE_JA_COMPROU_ITEM";
    public const string ResumoHistorico = "RESUMO_HISTORICO";

    public static string Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

        return normalized switch
        {
            ItensJaComprados => ItensJaComprados,
            ItemMaisComprado => ItemMaisComprado,
            ItemMaisCaroComprado => ItemMaisCaroComprado,
            UltimoPedido => UltimoPedido,
            ReplicarUltimoPedido => ReplicarUltimoPedido,
            RecomprarItemHistorico => RecomprarItemHistorico,
            VerificarSeJaComprouItem => VerificarSeJaComprouItem,
            ResumoHistorico => ResumoHistorico,
            _ => ResumoHistorico
        };
    }
}

public static class CustomerHistoryQueryActions
{
    public const string Responder = "RESPONDER";
    public const string PedirConfirmacaoRecompra = "PEDIR_CONFIRMACAO_RECOMPRA";

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), PedirConfirmacaoRecompra, StringComparison.OrdinalIgnoreCase)
            ? PedirConfirmacaoRecompra
            : Responder;
    }
}

public static class PendingCustomerActionTypes
{
    public const string Reorder = "REORDER";
}

public static class PendingCustomerActionStatuses
{
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
}

public sealed record ProductUpsertRequest(
    [param: Required] string StoreId,
    [param: Required] string Name,
    string? Description,
    string? Type,
    string? Brand,
    decimal RetailPrice,
    decimal? PromotionalPrice,
    decimal WholesalePrice,
    IReadOnlyList<string>? Aliases,
    int? StockQuantity,
    int? LowStockThreshold,
    bool IsActive = true);

public sealed record ProductResponse(
    string Id,
    string StoreId,
    string Name,
    string? Description,
    string? Type,
    string? Brand,
    decimal RetailPrice,
    decimal? PromotionalPrice,
    decimal WholesalePrice,
    IReadOnlyList<string> Aliases,
    int? StockQuantity,
    int? LowStockThreshold,
    bool IsActive);

public sealed record ProductDetailLookupResponse(
    string Query,
    IReadOnlyList<ProductResponse> Products);

public static class ProductImportActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Skip = "Skip";

    public static bool IsKnown(string? action)
    {
        return string.Equals(action, Create, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, Update, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, Skip, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ProductImportRequest(
    [param: Required] string StoreId,
    IReadOnlyList<ProductImportRowRequest>? Rows);

public sealed record ProductImportRowRequest(
    int RowNumber,
    [param: Required] string Action,
    string? ProductId,
    [param: Required] string Name,
    string? Description,
    string? Type,
    string? Brand,
    decimal RetailPrice,
    decimal? PromotionalPrice,
    bool IsActive);

public sealed record ProductImportResponse(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<ProductImportErrorResponse> Errors);

public sealed record ProductImportErrorResponse(int RowNumber, string Message);

public sealed record CustomerUpsertRequest(
    [param: Required] string StoreId,
    string? ClienteNome,
    string? CpfCnpj,
    string? ClienteEmail,
    string? ClienteEndereco,
    [param: Required] string ClienteTelefoneCelular);

public sealed record CustomerResponse(
    string Id,
    string StoreId,
    string? ClienteNome,
    string? CpfCnpj,
    string? ClienteEmail,
    string? ClienteEndereco,
    string ClienteTelefoneCelular,
    string ClienteDataCriacao);

public static class WhatsappConversationMessageDirections
{
    public const string Inbound = "Inbound";
    public const string Outbound = "Outbound";

    public static bool IsKnown(string? value)
    {
        return string.Equals(value, Inbound, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Outbound, StringComparison.OrdinalIgnoreCase);
    }
}

public static class WhatsappConversationMessageTypes
{
    public const string Customer = "Customer";
    public const string Ai = "Ai";
    public const string Agent = "Agent";
    public const string System = "System";

    public static bool IsKnown(string? value)
    {
        return string.Equals(value, Customer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Ai, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, Agent, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, System, StringComparison.OrdinalIgnoreCase);
    }
}

public static class WhatsappConversationMessageStatuses
{
    public const string Received = "Received";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

public sealed record WhatsappConversationSummaryResponse(
    string PhoneNumber,
    string? CustomerId,
    string? CustomerName,
    bool IsAgentEnabled,
    string LastMessage,
    string LastMessageDirection,
    string LastMessageType,
    string LastMessageStatus,
    string LastMessageAtUtc,
    int MessageCount);

public sealed record WhatsappConversationMessageResponse(
    string Id,
    string PhoneNumber,
    string Direction,
    string MessageType,
    string Body,
    string? TwilioMessageSid,
    string? SourceJobId,
    string Status,
    string? Error,
    string CreatedAtUtc);

public sealed record WhatsappContactAgentUpdateRequest(
    [param: Required] string StoreId,
    bool IsAgentEnabled);

public sealed record WhatsappContactAgentResponse(
    string PhoneNumber,
    bool IsAgentEnabled);

public sealed record WhatsappManualMessageRequest(
    [param: Required] string StoreId,
    [param: Required] string Message);

public static class CustomerImportActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Skip = "Skip";

    public static bool IsKnown(string? action)
    {
        return string.Equals(action, Create, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, Update, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, Skip, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CustomerImportRequest(
    [param: Required] string StoreId,
    IReadOnlyList<CustomerImportRowRequest>? Rows);

public sealed record CustomerImportRowRequest(
    int RowNumber,
    [param: Required] string Action,
    string? CustomerId,
    string? ClienteNome,
    string? CpfCnpj,
    string? ClienteEmail,
    string? ClienteEndereco,
    [param: Required] string ClienteTelefoneCelular);

public sealed record CustomerImportResponse(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<CustomerImportErrorResponse> Errors);

public sealed record CustomerImportErrorResponse(int RowNumber, string Message);

public sealed record ProductSyncFromMenuRequest(
    [param: Required] string StoreId,
    string? ProductId,
    [param: Required] string Name,
    string? Description,
    decimal RetailPrice,
    bool IsActive);

public sealed record AgentCampaignCustomerResponse(
    string PhoneNumber,
    string LastOrderAtUtc,
    int TotalOrders);

public sealed record AgentProductCampaignPreviewResponse(
    ProductResponse Product,
    string SuggestedMessage,
    IReadOnlyList<AgentCampaignCustomerResponse> Customers);

public sealed record AgentProductCampaignSendRequest(
    [param: Required] string StoreId,
    [param: Required] string ProductId,
    [param: Required] string Message);

public sealed record AgentCustomerRecurrenceResponse(
    string PhoneNumber,
    string LastOrderAtUtc,
    int TotalOrders,
    decimal? AverageDaysBetweenOrders,
    decimal DaysSinceLastOrder,
    bool IsOverdue);

public sealed record AgentCustomerReminderSendRequest(
    [param: Required] string StoreId,
    [param: Required] string PhoneNumber,
    [param: Required] string Message);

public sealed record AgentSendResultResponse(
    int SentCount,
    int FailedCount,
    IReadOnlyList<AgentSendResultItemResponse> Results);

public sealed record AgentSendResultItemResponse(
    string PhoneNumber,
    bool Sent,
    string? Error);

public static class AgentFeedbackFormats
{
    public const string Text = "TEXT";
    public const string Audio = "AUDIO";
    public const string Both = "BOTH";

    public static string Normalize(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format)
            ? Both
            : format.Trim().ToUpperInvariant();

        return normalized is Text or Audio or Both ? normalized : Both;
    }

    public static bool AcceptsText(string? format)
    {
        var normalized = Normalize(format);
        return normalized is Text or Both;
    }

    public static bool AcceptsAudio(string? format)
    {
        var normalized = Normalize(format);
        return normalized is Audio or Both;
    }
}

public static class AgentFeedbackSolicitationStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Responded = "RESPONDED";
    public const string Failed = "FAILED";
}

public static class AgentFeedbackSolicitationKinds
{
    public const string Order = "ORDER";
    public const string Periodic = "PERIODIC";
    public const string AgentDetected = "AGENT_DETECTED";
}

public static class AgentFeedbackResponseTypes
{
    public const string Text = "TEXT";
    public const string Audio = "AUDIO";
}

public static class AgentFeedbackCategories
{
    public const string Elogio = "ELOGIO";
    public const string Reclamacao = "RECLAMACAO";
    public const string Opiniao = "OPINIAO";
    public const string Sugestao = "SUGESTAO";
    public const string Outro = "OUTRO";
    public const string Indefinido = "INDEFINIDO";

    public static string Normalize(string? category)
    {
        var normalized = string.IsNullOrWhiteSpace(category)
            ? Indefinido
            : category.Trim().ToUpperInvariant();

        return normalized is Elogio or Reclamacao or Opiniao or Sugestao or Outro or Indefinido
            ? normalized
            : Indefinido;
    }
}

public static class AgentFeedbackSentiments
{
    public const string Positivo = "POSITIVO";
    public const string Neutro = "NEUTRO";
    public const string Negativo = "NEGATIVO";
    public const string Indefinido = "INDEFINIDO";

    public static string Normalize(string? sentiment)
    {
        var normalized = string.IsNullOrWhiteSpace(sentiment)
            ? Indefinido
            : sentiment.Trim().ToUpperInvariant();

        return normalized is Positivo or Neutro or Negativo or Indefinido
            ? normalized
            : Indefinido;
    }
}

public static class AgentFeedbackCustomerClassifications
{
    public const string Promotor = "PROMOTOR";
    public const string Neutro = "NEUTRO";
    public const string Detrator = "DETRATOR";
    public const string Indefinido = "INDEFINIDO";

    public static string Normalize(string? classification)
    {
        var normalized = string.IsNullOrWhiteSpace(classification)
            ? Indefinido
            : classification.Trim().ToUpperInvariant();

        return normalized is Promotor or Neutro or Detrator or Indefinido
            ? normalized
            : Indefinido;
    }
}

public sealed record AgentFeedbackSettingsResponse(
    string StoreId,
    bool IsPostOrderEnabled,
    int PostOrderDelayMinutes,
    string AcceptedFormat,
    string RequestMessage,
    bool IsPeriodicSurveyEnabled,
    int PeriodicSurveyDays,
    int PeriodicSurveySampleSize,
    string? LastPeriodicSurveyRunAtUtc,
    string UpdatedAtUtc);

public sealed record AgentFeedbackSettingsUpsertRequest(
    [param: Required] string StoreId,
    bool IsPostOrderEnabled,
    int? PostOrderDelayMinutes,
    string? AcceptedFormat,
    string? RequestMessage,
    bool IsPeriodicSurveyEnabled,
    int? PeriodicSurveyDays,
    int? PeriodicSurveySampleSize);

public sealed record AgentFeedbackSolicitationResponse(
    string Id,
    string StoreId,
    string? OrderId,
    string PhoneNumber,
    string Kind,
    string Status,
    string Message,
    string DueAtUtc,
    string? SentAtUtc,
    string? RespondedAtUtc,
    string? LastError,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    AgentFeedbackResponseResponse? Response);

public sealed record AgentFeedbackResponseResponse(
    string Id,
    string SolicitationId,
    string StoreId,
    string PhoneNumber,
    string ResponseType,
    string? Text,
    string? MediaUrl,
    string? MediaContentType,
    string? Category,
    string? Sentiment,
    string? CustomerClassification,
    int? Score,
    string? Summary,
    string? AnalyzedAtUtc,
    string? PromptResponseId,
    string? ConversationId,
    string? AiOutputJson,
    string CreatedAtUtc);

public sealed record AgentFeedbackResponseTarget(string SolicitationId, string AcceptedFormat);

public sealed record AgentFeedbackAnalysisData(
    string Category,
    string Sentiment,
    string CustomerClassification,
    int? Score,
    string? Summary);

public sealed record AgentFeedbackRegistrationCommand(
    string StoreId,
    string PhoneNumber,
    string CustomerMessage,
    string AiResponseText,
    string AiOutputJson,
    string? PromptResponseId,
    string? ConversationId,
    AgentFeedbackAnalysisData Analysis);

public static class AgentAutomatedCampaignTypes
{
    public const string ProductStock = "PRODUCT_STOCK";
    public const string Recurrence = "RECURRENCE";
    public const string InactiveCustomers = "INACTIVE_CUSTOMERS";

    public static string Normalize(string? type)
    {
        var normalized = string.IsNullOrWhiteSpace(type)
            ? string.Empty
            : type.Trim().ToUpperInvariant();

        return IsValid(normalized) ? normalized : string.Empty;
    }

    public static bool IsValid(string? type)
    {
        return string.Equals(type, ProductStock, StringComparison.Ordinal) ||
            string.Equals(type, Recurrence, StringComparison.Ordinal) ||
            string.Equals(type, InactiveCustomers, StringComparison.Ordinal);
    }

    public static string ToLabel(string? type)
    {
        return Normalize(type) switch
        {
            ProductStock => "Produto por estoque",
            Recurrence => "Recorrencia",
            InactiveCustomers => "Clientes inativos",
            _ => "Campanha"
        };
    }
}

public sealed record AgentAutomatedCampaignResponse(
    string Id,
    string StoreId,
    string Type,
    string Name,
    string? ProductId,
    string? ProductName,
    string Message,
    bool IsActive,
    string DailyRunTime,
    int CooldownDays,
    int? InactiveDaysThreshold,
    string? LastRunAtUtc,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    AgentAutomatedCampaignRunResponse? LastRun,
    IReadOnlyList<AgentAutomatedCampaignDeliveryResponse> RecentDeliveries);

public sealed record AgentAutomatedCampaignUpsertRequest(
    [param: Required] string StoreId,
    string? Id,
    [param: Required] string Type,
    [param: Required] string Name,
    string? ProductId,
    [param: Required] string Message,
    bool IsActive,
    string? DailyRunTime,
    int? CooldownDays,
    int? InactiveDaysThreshold);

public sealed record AgentAutomatedCampaignRunResponse(
    string Id,
    string CampaignId,
    string StoreId,
    string StartedAtUtc,
    string CompletedAtUtc,
    int EligibleCount,
    int SkippedCooldownCount,
    int SentCount,
    int FailedCount,
    string? Error,
    IReadOnlyList<AgentAutomatedCampaignDeliveryResponse> Deliveries);

public sealed record AgentAutomatedCampaignDeliveryResponse(
    string Id,
    string CampaignId,
    string RunId,
    string PhoneNumber,
    bool Sent,
    string? Error,
    string CreatedAtUtc);

public static class AgentPersonaTones
{
    public const string Amigavel = "AMIGAVEL";
    public const string Formal = "FORMAL";
    public const string Casual = "CASUAL";
    public const string Vendedor = "VENDEDOR";
    public const string Objetivo = "OBJETIVO";

    public static string Normalize(string? tone)
    {
        var normalized = string.IsNullOrWhiteSpace(tone)
            ? Amigavel
            : tone.Trim().ToUpperInvariant();

        return IsValid(normalized) ? normalized : Amigavel;
    }

    public static bool IsValid(string? tone)
    {
        return string.Equals(tone, Amigavel, StringComparison.Ordinal) ||
            string.Equals(tone, Formal, StringComparison.Ordinal) ||
            string.Equals(tone, Casual, StringComparison.Ordinal) ||
            string.Equals(tone, Vendedor, StringComparison.Ordinal) ||
            string.Equals(tone, Objetivo, StringComparison.Ordinal);
    }

    public static string ToLabel(string? tone)
    {
        return Normalize(tone) switch
        {
            Formal => "Formal",
            Casual => "Casual",
            Vendedor => "Vendedor",
            Objetivo => "Objetivo",
            _ => "Amigavel"
        };
    }
}

public sealed record AgentPersonaSettingsResponse(
    string StoreId,
    string Tone,
    string CustomInstructions,
    IReadOnlyList<AgentPersonaFaqResponse> Faqs);

public sealed record AgentPersonaSettingsUpsertRequest(
    [param: Required] string StoreId,
    [param: Required] string Tone,
    string? CustomInstructions,
    IReadOnlyList<AgentPersonaFaqUpsert>? Faqs);

public sealed record AgentPersonaFaqResponse(
    string Id,
    string Question,
    string Answer,
    bool IsActive,
    int SortOrder);

public sealed record AgentPersonaFaqUpsert(
    string? Id,
    string Question,
    string Answer,
    bool IsActive,
    int SortOrder);

public sealed record ProductCatalogItem(
    string Id,
    string StoreId,
    string Name,
    string? Description,
    string NormalizedName,
    string? Type,
    string? Brand,
    long RetailPriceCents,
    long? PromotionalPriceCents,
    long WholesalePriceCents,
    IReadOnlyList<ProductAliasCatalogItem> Aliases,
    bool IsActive);

public sealed record ProductAliasCatalogItem(string Alias, string NormalizedAlias);

public sealed record RegisterOrderRequest(
    [param: Required] string StoreId,
    [param: Required] string PhoneNumber,
    [param: Required] string SourceMessageId,
    [param: Required] string Texto,
    PromptOrderPayload? Pedido,
    string? CustomerMessage = null);

public sealed record OrderRegistrationCommand(
    string StoreId,
    string PhoneNumber,
    string SourceMessageId,
    string? PromptResponseId,
    string? ConversationId,
    string? CustomerMessage,
    string AiResponseText,
    string AiOutputJson,
    PromptOrderPayload? Pedido);

public sealed record OrderRegistrationResult(
    string OrderId,
    string Status,
    long TotalCents,
    bool AlreadyExisted);

public sealed record UpdateOrderStatusRequest(
    [param: Required] string StoreId,
    [param: Required] string Status);

public sealed record OrderHistoryImportRequest(
    [param: Required] string StoreId,
    IReadOnlyList<OrderHistoryImportRowRequest>? Rows);

public sealed record OrderHistoryImportRowRequest(
    int RowNumber,
    [param: Required] string PedidoCodigo,
    string? PedidoData,
    string? ClienteNome,
    string? CpfCnpj,
    string? ClienteEmail,
    [param: Required] string ClienteTelefoneCelular,
    string? TipoVenda,
    [param: Required] string ProdutoNome,
    int Quantidade,
    decimal? PrecoUnitario,
    string? ObservacaoItem,
    string? ObservacaoPedido);

public sealed record OrderHistoryImportResponse(
    int CreatedOrderCount,
    int CreatedItemCount,
    int SkippedOrderCount,
    IReadOnlyList<OrderHistoryImportErrorResponse> Errors);

public sealed record OrderHistoryImportErrorResponse(int RowNumber, string Message);

public sealed record OrderManagementCustomerResponse(
    string PhoneNumber,
    int TotalOrders,
    int OpenOrders,
    string LastOrderAtUtc,
    IReadOnlyList<OrderManagementOrderResponse> Orders);

public sealed record OrderManagementOrderResponse(
    string Id,
    string Status,
    string? SaleType,
    long TotalCents,
    string? GeneralObservation,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    IReadOnlyList<OrderManagementOrderItemResponse> Items);

public sealed record OrderManagementOrderItemResponse(
    string RequestedProductName,
    string? ProductName,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string MatchStatus);

public sealed record LoginRequest(
    [param: Required] string Username,
    [param: Required] string Password);

public sealed record CompanyLoginResponse(
    string CompanyId,
    string CompanyName,
    string CompanyPhone,
    string Username);

public sealed record BrandingSettingsResponse(
    string StoreId,
    string SiteName,
    string PaletteKey,
    string PrimaryColor,
    string SecondaryColor,
    string BackgroundColor,
    string MenuTheme,
    string MenuMode,
    string? LogoDataUrl,
    string UpdatedAtUtc);

public sealed record BrandingSettingsUpsertRequest(
    [param: Required] string StoreId,
    [param: Required] string SiteName,
    [param: Required] string PaletteKey,
    string? LogoDataUrl,
    bool RemoveLogo);

public sealed record InternalCompanySyncRequest(
    [param: Required] string CompanyName,
    [param: Required] string CompanyPhone,
    string? PreviousCompanyPhone);

public sealed record DashboardResponse(
    int TotalOrders,
    long TotalSoldCents,
    long AverageTicketCents,
    DashboardTopProductResponse? TopProduct,
    int PendingReviewOrders,
    int LateOrders,
    IReadOnlyList<DashboardStatusCountResponse> StatusCounts,
    IReadOnlyList<DashboardTopProductResponse> TopProducts,
    IReadOnlyList<DashboardRecentOrderResponse> RecentOrders);

public sealed record DashboardTopProductResponse(
    string ProductName,
    int Quantity,
    long TotalCents);

public sealed record DashboardStatusCountResponse(
    string Status,
    int Count);

public sealed record DashboardRecentOrderResponse(
    string Id,
    string PhoneNumber,
    string Status,
    string? SaleType,
    long TotalCents,
    string? GeneralObservation,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    bool IsLate,
    IReadOnlyList<DashboardRecentOrderItemResponse> Items);

public sealed record DashboardRecentOrderItemResponse(
    string RequestedProductName,
    string? ProductName,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string MatchStatus);

public sealed record ActiveOrderData(
    string Id,
    string Status,
    string? SaleType,
    long TotalCents,
    string CreatedAtUtc,
    string UpdatedAtUtc,
    IReadOnlyList<ActiveOrderItemData> Items);

public sealed record ActiveOrderItemData(
    string RequestedProductName,
    string? ProductNameSnapshot,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string MatchStatus);

public sealed record CustomerPurchasedItemData(
    string ProductName,
    string? ProductId,
    int TotalQuantity,
    int OrderCount,
    long TotalSpentCents,
    long? MaxUnitPriceCents,
    string LastPurchasedAtUtc);

public sealed record CustomerHistoricalOrderItemData(
    string ProductName,
    string? ProductId,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string PurchasedAtUtc);

public sealed record PendingCustomerAction(
    string Id,
    string StoreId,
    string PhoneNumber,
    string ActionType,
    string PayloadJson,
    string Status,
    string CreatedAtUtc,
    string ExpiresAtUtc);

public sealed record OrderRegistrationData(
    string Id,
    string StoreId,
    string PhoneNumber,
    string SourceMessageId,
    string? PromptResponseId,
    string? ConversationId,
    string? SaleType,
    string Status,
    string? CustomerMessage,
    string AiResponseText,
    string AiOutputJson,
    string? GeneralObservation,
    long TotalCents,
    IReadOnlyList<OrderItemRegistrationData> Items);

public sealed record OrderItemRegistrationData(
    string Id,
    string OrderId,
    string? ProductId,
    string RequestedProductName,
    string? ProductNameSnapshot,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string MatchStatus);

public sealed record HistoricalOrderRegistrationData(
    string Id,
    string StoreId,
    string PhoneNumber,
    string SourceMessageId,
    string? SaleType,
    string? CustomerMessage,
    string AiResponseText,
    string AiOutputJson,
    string? GeneralObservation,
    long TotalCents,
    string CreatedAtUtc,
    IReadOnlyList<HistoricalOrderItemRegistrationData> Items);

public sealed record HistoricalOrderItemRegistrationData(
    string Id,
    string OrderId,
    string? ProductId,
    string RequestedProductName,
    string? ProductNameSnapshot,
    int Quantity,
    long? UnitPriceCents,
    long? TotalPriceCents,
    string? Observation,
    string MatchStatus);

public sealed record WhatsappConversationState(string? ConversationId, string? LastResponseId);

public sealed record WhatsappMessageJob(
    string Id,
    string Message,
    string PhoneNumber,
    string StoreId,
    int Attempts,
    string? FeedbackSolicitationId);
