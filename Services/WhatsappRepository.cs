using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Options;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;

namespace AtendenteWhatssApp.Services;

public sealed class WhatsappRepository
{
    private const string DefaultFeedbackRequestMessage =
        "Ola! Seu pedido foi concluido. Pode nos contar como foi sua experiencia? Voce pode responder por texto ou audio.";
    private const int DefaultFeedbackDelayMinutes = 60;
    private const int DefaultFeedbackPeriodicSurveyDays = 10;
    private const int DefaultFeedbackPeriodicSurveySampleSize = 10;
    private static readonly HashSet<string> ProductSearchStopWords = new(StringComparer.Ordinal)
    {
        "sobre",
        "produto",
        "produtos",
        "quero",
        "queria",
        "gostaria",
        "saber",
        "fala",
        "falar",
        "detalhe",
        "detalhes",
        "descricao",
        "descrição",
        "preco",
        "preço",
        "valor",
        "tem",
        "tenho",
        "voces",
        "vocês",
        "qual",
        "quais",
        "pode",
        "ajuda",
        "ajudar",
        "para",
        "com",
        "uma",
        "uns",
        "das",
        "dos"
    };

    private readonly string _connectionString;
    private readonly SeedCompanyOptions _seedCompanyOptions;

    public WhatsappRepository(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<SeedCompanyOptions> seedCompanyOptions,
        IConfiguration configuration)
    {
        _seedCompanyOptions = seedCompanyOptions.Value;
        var configuredConnectionString = FirstNonEmpty(
            databaseOptions.Value.ConnectionString,
            configuration["SUPABASE_DB_URL"],
            configuration.GetConnectionString("DefaultConnection"));

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException("Configure SUPABASE_DB_URL with the Supabase/PostgreSQL connection string.");
        }

        _connectionString = BuildNpgsqlConnectionString(configuredConnectionString);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string BuildNpgsqlConnectionString(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, "postgresql", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, "postgres", StringComparison.OrdinalIgnoreCase)))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo.Length != 2)
            {
                throw new InvalidOperationException("The PostgreSQL URI must include user and password.");
            }

            return new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = string.IsNullOrWhiteSpace(uri.AbsolutePath.TrimStart('/'))
                    ? "postgres"
                    : Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = Uri.UnescapeDataString(userInfo[1]),
                SslMode = SslMode.Require,
                Timeout = 30,
                CommandTimeout = 120,
                Pooling = false
            }.ConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.SslMode == SslMode.Disable)
        {
            builder.SslMode = SslMode.Require;
        }

        if (builder.CommandTimeout == 30)
        {
            builder.CommandTimeout = 120;
        }

        builder.Pooling = false;
        return builder.ConnectionString;
    }

    private static void AddNullableTextParameter(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS StorePrompts (
                StoreId TEXT PRIMARY KEY,
                PromptId TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Companies (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                CompanyName TEXT NOT NULL,
                CompanyPhone TEXT NOT NULL UNIQUE,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Clientes (
                ID_Cliente TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                CLIENTE_NOME TEXT NULL,
                CPF_CNPJ TEXT NULL,
                CLIENTE_EMAIL TEXT NULL,
                CLIENTE_ENDERECO TEXT NULL,
                CLIENTE_TELEFONE_CELULAR TEXT NOT NULL,
                CLIENTE_DATA_CRIACAO TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS WhatsappConversations (
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                ConversationId TEXT NULL,
                LastResponseId TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (StoreId, PhoneNumber)
            );

            CREATE TABLE IF NOT EXISTS WhatsappContactSettings (
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                IsAgentEnabled INTEGER NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (StoreId, PhoneNumber)
            );

            CREATE TABLE IF NOT EXISTS WhatsappConversationMessages (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                Direction TEXT NOT NULL,
                MessageType TEXT NOT NULL,
                Body TEXT NOT NULL,
                TwilioMessageSid TEXT NULL,
                SourceJobId TEXT NULL,
                Status TEXT NOT NULL,
                Error TEXT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS WhatsappMessageJobs (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                Message TEXT NOT NULL,
                FeedbackSolicitationId TEXT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS WhatsappPendingCustomerActions (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                ActionType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentPersonaSettings (
                StoreId TEXT PRIMARY KEY,
                Tone TEXT NOT NULL,
                CustomInstructions TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentPersonaFaqs (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                Question TEXT NOT NULL,
                Answer TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StoreId) REFERENCES AgentPersonaSettings(StoreId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AgentNotificationSettings (
                StoreId TEXT PRIMARY KEY,
                StaffNotificationPhoneNumber TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ApplicationLogs (
                Id TEXT PRIMARY KEY,
                CreatedAtUtc TEXT NOT NULL,
                Text TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Products (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                Type TEXT NULL,
                Brand TEXT NULL,
                NormalizedName TEXT NOT NULL,
                RetailPriceCents INTEGER NOT NULL,
                PromotionalPriceCents INTEGER NULL,
                WholesalePriceCents INTEGER NOT NULL,
                StockQuantity INTEGER NULL,
                LowStockThreshold INTEGER NULL,
                IsActive INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                UNIQUE (StoreId, NormalizedName)
            );

            CREATE TABLE IF NOT EXISTS ProductAliases (
                Id TEXT PRIMARY KEY,
                ProductId TEXT NOT NULL,
                Alias TEXT NOT NULL,
                NormalizedAlias TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UNIQUE (ProductId, NormalizedAlias),
                FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Orders (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                SourceMessageId TEXT NOT NULL,
                PromptResponseId TEXT NULL,
                ConversationId TEXT NULL,
                SaleType TEXT NULL,
                Status TEXT NOT NULL,
                CustomerMessage TEXT NULL,
                AiResponseText TEXT NOT NULL,
                AiOutputJson TEXT NOT NULL,
                GeneralObservation TEXT NULL,
                TotalCents INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                UNIQUE (StoreId, SourceMessageId)
            );

            CREATE TABLE IF NOT EXISTS OrderItems (
                Id TEXT PRIMARY KEY,
                OrderId TEXT NOT NULL,
                ProductId TEXT NULL,
                RequestedProductName TEXT NOT NULL,
                ProductNameSnapshot TEXT NULL,
                Quantity INTEGER NOT NULL,
                UnitPriceCents INTEGER NULL,
                TotalPriceCents INTEGER NULL,
                Observation TEXT NULL,
                MatchStatus TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );

            CREATE TABLE IF NOT EXISTS AgentAutomatedCampaigns (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                Type TEXT NOT NULL,
                Name TEXT NOT NULL,
                ProductId TEXT NULL,
                Message TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DailyRunTime TEXT NOT NULL,
                CooldownDays INTEGER NOT NULL,
                InactiveDaysThreshold INTEGER NULL,
                LastRunAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );

            CREATE TABLE IF NOT EXISTS AgentAutomatedCampaignRuns (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                StoreId TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NOT NULL,
                EligibleCount INTEGER NOT NULL,
                SkippedCooldownCount INTEGER NOT NULL,
                SentCount INTEGER NOT NULL,
                FailedCount INTEGER NOT NULL,
                Error TEXT NULL,
                FOREIGN KEY (CampaignId) REFERENCES AgentAutomatedCampaigns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AgentAutomatedCampaignDeliveries (
                Id TEXT PRIMARY KEY,
                CampaignId TEXT NOT NULL,
                RunId TEXT NOT NULL,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                Sent INTEGER NOT NULL,
                Error TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (CampaignId) REFERENCES AgentAutomatedCampaigns(Id) ON DELETE CASCADE,
                FOREIGN KEY (RunId) REFERENCES AgentAutomatedCampaignRuns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AgentFeedbackSettings (
                StoreId TEXT PRIMARY KEY,
                IsPostOrderEnabled INTEGER NOT NULL,
                PostOrderDelayMinutes INTEGER NOT NULL,
                AcceptedFormat TEXT NOT NULL,
                RequestMessage TEXT NOT NULL,
                IsPeriodicSurveyEnabled INTEGER NOT NULL,
                PeriodicSurveyDays INTEGER NOT NULL,
                PeriodicSurveySampleSize INTEGER NOT NULL,
                LastPeriodicSurveyRunAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentFeedbackSolicitations (
                Id TEXT PRIMARY KEY,
                StoreId TEXT NOT NULL,
                OrderId TEXT NULL,
                PhoneNumber TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Status TEXT NOT NULL,
                Message TEXT NOT NULL,
                DueAtUtc TEXT NOT NULL,
                SentAtUtc TEXT NULL,
                RespondedAtUtc TEXT NULL,
                LastError TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                UNIQUE (StoreId, OrderId),
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AgentFeedbackResponses (
                Id TEXT PRIMARY KEY,
                SolicitationId TEXT NOT NULL UNIQUE,
                StoreId TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                ResponseType TEXT NOT NULL,
                Text TEXT NULL,
                MediaUrl TEXT NULL,
                MediaContentType TEXT NULL,
                Category TEXT NULL,
                Sentiment TEXT NULL,
                CustomerClassification TEXT NULL,
                Score INTEGER NULL,
                Summary TEXT NULL,
                AnalyzedAtUtc TEXT NULL,
                PromptResponseId TEXT NULL,
                ConversationId TEXT NULL,
                AiOutputJson TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (SolicitationId) REFERENCES AgentFeedbackSolicitations(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ProductAliases_NormalizedAlias ON ProductAliases (NormalizedAlias);
            CREATE INDEX IF NOT EXISTS IX_Orders_StoreId_PhoneNumber ON Orders (StoreId, PhoneNumber);
            CREATE INDEX IF NOT EXISTS IX_OrderItems_OrderId ON OrderItems (OrderId);
            CREATE INDEX IF NOT EXISTS IX_Companies_Username ON Companies (Username);
            CREATE INDEX IF NOT EXISTS IX_AgentPersonaFaqs_StoreId_SortOrder ON AgentPersonaFaqs (StoreId, SortOrder);
            CREATE INDEX IF NOT EXISTS IX_AgentAutomatedCampaigns_StoreId ON AgentAutomatedCampaigns (StoreId, IsDeleted, IsActive);
            CREATE INDEX IF NOT EXISTS IX_AgentAutomatedCampaignRuns_CampaignId ON AgentAutomatedCampaignRuns (CampaignId, StartedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_AgentAutomatedCampaignDeliveries_Cooldown ON AgentAutomatedCampaignDeliveries (CampaignId, PhoneNumber, Sent, CreatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_AgentFeedbackSolicitations_Due ON AgentFeedbackSolicitations (Status, DueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_AgentFeedbackSolicitations_Store ON AgentFeedbackSolicitations (StoreId, CreatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_AgentFeedbackSolicitations_ResponseLookup ON AgentFeedbackSolicitations (StoreId, PhoneNumber, Status, SentAtUtc);
            CREATE INDEX IF NOT EXISTS IX_WhatsappConversationMessages_Store_Phone_Created ON WhatsappConversationMessages (StoreId, PhoneNumber, CreatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_WhatsappConversationMessages_Store_Created ON WhatsappConversationMessages (StoreId, CreatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_WhatsappPendingCustomerActions_Lookup ON WhatsappPendingCustomerActions (StoreId, PhoneNumber, ActionType, Status, CreatedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_ApplicationLogs_CreatedAtUtc ON ApplicationLogs (CreatedAtUtc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await SeedCompanyIfNeededAsync(connection, cancellationToken);
        await EnsureClientesSchemaAsync(connection, cancellationToken);

        if (!await TableHasColumnAsync(connection, "WhatsappConversations", "ConversationId", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE WhatsappConversations ADD COLUMN ConversationId TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "Description", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN Description TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "Type", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN Type TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "Brand", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN Brand TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "PromotionalPriceCents", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN PromotionalPriceCents INTEGER NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "StockQuantity", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN StockQuantity INTEGER NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Products", "LowStockThreshold", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Products ADD COLUMN LowStockThreshold INTEGER NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "WhatsappMessageJobs", "FeedbackSolicitationId", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE WhatsappMessageJobs ADD COLUMN FeedbackSolicitationId TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var column in new[]
        {
            ("Category", "TEXT NULL"),
            ("Sentiment", "TEXT NULL"),
            ("CustomerClassification", "TEXT NULL"),
            ("Score", "INTEGER NULL"),
            ("Summary", "TEXT NULL"),
            ("AnalyzedAtUtc", "TEXT NULL"),
            ("PromptResponseId", "TEXT NULL"),
            ("ConversationId", "TEXT NULL"),
            ("AiOutputJson", "TEXT NULL")
        })
        {
            if (!await TableHasColumnAsync(connection, "AgentFeedbackResponses", column.Item1, cancellationToken))
            {
                var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = $"ALTER TABLE AgentFeedbackResponses ADD COLUMN {column.Item1} {column.Item2};";
                await alterCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var migrateRegisteredOrdersCommand = connection.CreateCommand();
        migrateRegisteredOrdersCommand.CommandText =
            """
            UPDATE Orders
            SET Status = @newStatus,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Status = 'Registered';
            """;
        migrateRegisteredOrdersCommand.Parameters.AddWithValue("@newStatus", OrderStatuses.EmProducao);
        migrateRegisteredOrdersCommand.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));
        await migrateRegisteredOrdersCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureClientesSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableHasColumnAsync(connection, "Clientes", "StoreId", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Clientes ADD COLUMN StoreId TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await TableHasColumnAsync(connection, "Clientes", "CLIENTE_ENDERECO", cancellationToken))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE Clientes ADD COLUMN CLIENTE_ENDERECO TEXT NULL;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var commandText in new[]
        {
            "DROP INDEX IF EXISTS IX_Clientes_TelefoneCelular;",
            "DROP INDEX IF EXISTS IX_Clientes_CpfCnpj;",
            "DROP INDEX IF EXISTS IX_Clientes_Email;",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Clientes_StoreId_TelefoneCelular ON Clientes (StoreId, CLIENTE_TELEFONE_CELULAR);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Clientes_StoreId_CpfCnpj ON Clientes (StoreId, CPF_CNPJ) WHERE CPF_CNPJ IS NOT NULL AND TRIM(CPF_CNPJ) <> '';",
            "CREATE INDEX IF NOT EXISTS IX_Clientes_StoreId_Email ON Clientes (StoreId, CLIENTE_EMAIL);"
        })
        {
            var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = commandText;
            await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<CompanyLoginResponse?> AuthenticateCompanyAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Username, PasswordHash, CompanyName, CompanyPhone
            FROM Companies
            WHERE Username = @username
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@username", username.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var passwordHash = reader.GetString(2);
        if (!VerifyPassword(password, passwordHash))
        {
            return null;
        }

        return new CompanyLoginResponse(
            reader.GetString(0),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(1));
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListCustomersAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ID_Cliente,
                   StoreId,
                   CLIENTE_NOME,
                   CPF_CNPJ,
                   CLIENTE_EMAIL,
                   CLIENTE_ENDERECO,
                   CLIENTE_TELEFONE_CELULAR,
                   CLIENTE_DATA_CRIACAO
            FROM Clientes
            WHERE StoreId = @storeId
            ORDER BY COALESCE(NULLIF(CLIENTE_NOME, ''), CLIENTE_TELEFONE_CELULAR), CLIENTE_TELEFONE_CELULAR;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);

        var customers = new List<CustomerResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(ReadCustomer(reader));
        }

        return customers;
    }

    public async Task<CustomerSaveResult> CreateCustomerAsync(
        CustomerUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var phone = PhoneNumberNormalizer.ToBrazilNationalPhone(request.ClienteTelefoneCelular);
        var name = NormalizeOptionalText(request.ClienteNome);
        var cpfCnpj = NormalizeOptionalText(request.CpfCnpj);
        var email = NormalizeOptionalText(request.ClienteEmail);
        var address = NormalizeOptionalText(request.ClienteEndereco);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var conflict = await FindCustomerConflictAsync(
            connection,
            transaction,
            storeId,
            phone,
            cpfCnpj,
            excludeCustomerId: null,
            cancellationToken);
        if (conflict is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CustomerSaveResult(CustomerSaveStatus.Conflict, Customer: null, conflict);
        }

        var customerId = Guid.NewGuid().ToString("N");
        var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO Clientes
                (ID_Cliente, StoreId, CLIENTE_NOME, CPF_CNPJ, CLIENTE_EMAIL, CLIENTE_ENDERECO, CLIENTE_TELEFONE_CELULAR, CLIENTE_DATA_CRIACAO)
            VALUES
                (@id, @storeId, @name, @cpfCnpj, @email, @address, @phone, @createdAtUtc);
            """;
        AddCustomerParameters(insertCommand, customerId, storeId, name, cpfCnpj, email, address, phone, now);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CustomerSaveResult(
            CustomerSaveStatus.Saved,
            new CustomerResponse(customerId, storeId, name, cpfCnpj, email, address, phone, now));
    }

    public async Task<CustomerSaveResult> UpdateCustomerAsync(
        string storeId,
        string customerId,
        CustomerUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        var normalizedCustomerId = customerId.Trim();
        var phone = PhoneNumberNormalizer.ToBrazilNationalPhone(request.ClienteTelefoneCelular);
        var name = NormalizeOptionalText(request.ClienteNome);
        var cpfCnpj = NormalizeOptionalText(request.CpfCnpj);
        var email = NormalizeOptionalText(request.ClienteEmail);
        var address = NormalizeOptionalText(request.ClienteEndereco);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var conflict = await FindCustomerConflictAsync(
            connection,
            transaction,
            normalizedStoreId,
            phone,
            cpfCnpj,
            normalizedCustomerId,
            cancellationToken);
        if (conflict is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CustomerSaveResult(CustomerSaveStatus.Conflict, Customer: null, conflict);
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE Clientes
            SET CLIENTE_NOME = @name,
                CPF_CNPJ = @cpfCnpj,
                CLIENTE_EMAIL = @email,
                CLIENTE_ENDERECO = @address,
                CLIENTE_TELEFONE_CELULAR = @phone
            WHERE ID_Cliente = @id
              AND StoreId = @storeId;
            """;
        updateCommand.Parameters.AddWithValue("@id", normalizedCustomerId);
        updateCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        updateCommand.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@cpfCnpj", (object?)cpfCnpj ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@address", (object?)address ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@phone", phone);

        var rowsUpdated = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (rowsUpdated == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CustomerSaveResult(CustomerSaveStatus.NotFound, Customer: null);
        }

        await transaction.CommitAsync(cancellationToken);

        var customer = await GetCustomerAsync(normalizedStoreId, normalizedCustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Updated customer was not found.");
        return new CustomerSaveResult(CustomerSaveStatus.Saved, customer);
    }

    public async Task<bool> DeleteCustomerAsync(
        string storeId,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM Clientes
            WHERE ID_Cliente = @id
              AND StoreId = @storeId;
            """;
        command.Parameters.AddWithValue("@id", customerId.Trim());
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<CustomerResponse?> GetCustomerAsync(
        string storeId,
        string customerId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ID_Cliente,
                   StoreId,
                   CLIENTE_NOME,
                   CPF_CNPJ,
                   CLIENTE_EMAIL,
                   CLIENTE_ENDERECO,
                   CLIENTE_TELEFONE_CELULAR,
                   CLIENTE_DATA_CRIACAO
            FROM Clientes
            WHERE StoreId = @storeId
              AND ID_Cliente = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@id", customerId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCustomer(reader) : null;
    }

    public async Task<CustomerResponse?> FindCustomerByPhoneAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var customers = await ListCustomersAsync(storeId, cancellationToken);
        var lookupKeys = PhoneNumberNormalizer.GetLookupKeys(phoneNumber);
        return customers.FirstOrDefault(customer =>
            PhoneNumberNormalizer.GetLookupKeys(customer.ClienteTelefoneCelular).Any(lookupKeys.Contains));
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var normalizedStoreId = storeId.Trim();
        var (totalOrders, totalSoldCents, averageTicketCents, pendingReviewOrders) = await GetDashboardTotalsAsync(
            connection,
            normalizedStoreId,
            cancellationToken);
        var topProducts = await GetDashboardTopProductsAsync(connection, normalizedStoreId, cancellationToken);
        var topProduct = topProducts.FirstOrDefault();
        var lateOrders = await CountLateOrdersAsync(connection, normalizedStoreId, cancellationToken);
        var statusCounts = await GetDashboardStatusCountsAsync(connection, normalizedStoreId, cancellationToken);
        var recentOrders = await GetDashboardRecentOrdersAsync(connection, normalizedStoreId, cancellationToken);

        return new DashboardResponse(
            totalOrders,
            totalSoldCents,
            averageTicketCents,
            topProduct,
            pendingReviewOrders,
            lateOrders,
            statusCounts,
            topProducts,
            recentOrders);
    }

    public async Task<ProductResponse> UpsertProductAsync(
        ProductUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var name = request.Name.Trim();
        var description = NormalizeOptionalText(request.Description);
        var type = NormalizeOptionalText(request.Type);
        var brand = NormalizeOptionalText(request.Brand);
        var normalizedName = TextNormalizer.NormalizeForLookup(name);
        var retailPriceCents = ToCents(request.RetailPrice);
        long? promotionalPriceCents = request.PromotionalPrice is null ? null : ToCents(request.PromotionalPrice.Value);
        var wholesalePriceCents = ToCents(request.WholesalePrice);
        var stockQuantity = NormalizeOptionalQuantity(request.StockQuantity);
        var lowStockThreshold = NormalizeOptionalQuantity(request.LowStockThreshold);
        var aliases = NormalizeAliases(request.Aliases, normalizedName);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Id
            FROM Products
            WHERE StoreId = @storeId
              AND NormalizedName = @normalizedName
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("@storeId", storeId);
        selectCommand.Parameters.AddWithValue("@normalizedName", normalizedName);

        var existingId = await selectCommand.ExecuteScalarAsync(cancellationToken) as string;
        var productId = existingId ?? Guid.NewGuid().ToString("N");

        var upsertCommand = connection.CreateCommand();
        upsertCommand.Transaction = transaction;
        upsertCommand.CommandText = existingId is null
            ? """
              INSERT INTO Products
                  (Id, StoreId, Name, Description, Type, Brand, NormalizedName, RetailPriceCents, PromotionalPriceCents, WholesalePriceCents,
                   StockQuantity, LowStockThreshold, IsActive, CreatedAtUtc, UpdatedAtUtc)
              VALUES
                  (@id, @storeId, @name, @description, @type, @brand, @normalizedName, @retailPriceCents, @promotionalPriceCents, @wholesalePriceCents,
                   @stockQuantity, @lowStockThreshold, @isActive, @updatedAtUtc, @updatedAtUtc);
              """
            : """
              UPDATE Products
              SET Name = @name,
                  Description = @description,
                  Type = @type,
                  Brand = @brand,
                  RetailPriceCents = @retailPriceCents,
                  PromotionalPriceCents = @promotionalPriceCents,
                  WholesalePriceCents = @wholesalePriceCents,
                  StockQuantity = @stockQuantity,
                  LowStockThreshold = @lowStockThreshold,
                  IsActive = @isActive,
                  UpdatedAtUtc = @updatedAtUtc
              WHERE Id = @id;
              """;
        upsertCommand.Parameters.AddWithValue("@id", productId);
        upsertCommand.Parameters.AddWithValue("@storeId", storeId);
        upsertCommand.Parameters.AddWithValue("@name", name);
        upsertCommand.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@brand", (object?)brand ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
        upsertCommand.Parameters.AddWithValue("@retailPriceCents", retailPriceCents);
        upsertCommand.Parameters.AddWithValue("@promotionalPriceCents", (object?)promotionalPriceCents ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@wholesalePriceCents", wholesalePriceCents);
        upsertCommand.Parameters.AddWithValue("@stockQuantity", (object?)stockQuantity ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@lowStockThreshold", (object?)lowStockThreshold ?? DBNull.Value);
        upsertCommand.Parameters.AddWithValue("@isActive", request.IsActive ? 1 : 0);
        upsertCommand.Parameters.AddWithValue("@updatedAtUtc", now);
        await upsertCommand.ExecuteNonQueryAsync(cancellationToken);

        var deleteAliasesCommand = connection.CreateCommand();
        deleteAliasesCommand.Transaction = transaction;
        deleteAliasesCommand.CommandText = "DELETE FROM ProductAliases WHERE ProductId = @productId;";
        deleteAliasesCommand.Parameters.AddWithValue("@productId", productId);
        await deleteAliasesCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var alias in aliases)
        {
            var insertAliasCommand = connection.CreateCommand();
            insertAliasCommand.Transaction = transaction;
            insertAliasCommand.CommandText =
                """
                INSERT INTO ProductAliases (Id, ProductId, Alias, NormalizedAlias, CreatedAtUtc)
                VALUES (@id, @productId, @alias, @normalizedAlias, @createdAtUtc);
                """;
            insertAliasCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            insertAliasCommand.Parameters.AddWithValue("@productId", productId);
            insertAliasCommand.Parameters.AddWithValue("@alias", alias.Alias);
            insertAliasCommand.Parameters.AddWithValue("@normalizedAlias", alias.NormalizedAlias);
            insertAliasCommand.Parameters.AddWithValue("@createdAtUtc", now);
            await insertAliasCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new ProductResponse(
            productId,
            storeId,
            name,
            description,
            type,
            brand,
            FromCents(retailPriceCents),
            promotionalPriceCents is null ? null : FromCents(promotionalPriceCents.Value),
            FromCents(wholesalePriceCents),
            aliases.Select(alias => alias.Alias).ToArray(),
            stockQuantity,
            lowStockThreshold,
            request.IsActive);
    }

    public async Task<ProductSaveResult> SyncProductFromMenuAsync(
        ProductSyncFromMenuRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var requestedProductId = string.IsNullOrWhiteSpace(request.ProductId) ? null : request.ProductId.Trim();
        var name = request.Name.Trim();
        var description = NormalizeOptionalText(request.Description);
        var normalizedName = TextNormalizer.NormalizeForLookup(name);
        var retailPriceCents = ToCents(request.RetailPrice);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        string? productId = null;
        if (!string.IsNullOrWhiteSpace(requestedProductId))
        {
            var selectByIdCommand = connection.CreateCommand();
            selectByIdCommand.Transaction = transaction;
            selectByIdCommand.CommandText =
                """
                SELECT Id
                FROM Products
                WHERE StoreId = @storeId
                  AND Id = @productId
                LIMIT 1;
                """;
            selectByIdCommand.Parameters.AddWithValue("@storeId", storeId);
            selectByIdCommand.Parameters.AddWithValue("@productId", requestedProductId);
            productId = await selectByIdCommand.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (productId is null)
        {
            var selectByNameCommand = connection.CreateCommand();
            selectByNameCommand.Transaction = transaction;
            selectByNameCommand.CommandText =
                """
                SELECT Id
                FROM Products
                WHERE StoreId = @storeId
                  AND NormalizedName = @normalizedName
                LIMIT 1;
                """;
            selectByNameCommand.Parameters.AddWithValue("@storeId", storeId);
            selectByNameCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
            productId = await selectByNameCommand.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (productId is not null)
        {
            var conflictCommand = connection.CreateCommand();
            conflictCommand.Transaction = transaction;
            conflictCommand.CommandText =
                """
                SELECT Id
                FROM Products
                WHERE StoreId = @storeId
                  AND NormalizedName = @normalizedName
                  AND Id <> @productId
                LIMIT 1;
                """;
            conflictCommand.Parameters.AddWithValue("@storeId", storeId);
            conflictCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
            conflictCommand.Parameters.AddWithValue("@productId", productId);

            var conflictingId = await conflictCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (conflictingId is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ProductSaveResult(ProductSaveStatus.Conflict, Product: null);
            }

            var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                """
                UPDATE Products
                SET Name = @name,
                    Description = @description,
                    NormalizedName = @normalizedName,
                    RetailPriceCents = @retailPriceCents,
                    IsActive = @isActive,
                    UpdatedAtUtc = @updatedAtUtc
                WHERE Id = @productId
                  AND StoreId = @storeId;
                """;
            updateCommand.Parameters.AddWithValue("@productId", productId);
            updateCommand.Parameters.AddWithValue("@storeId", storeId);
            updateCommand.Parameters.AddWithValue("@name", name);
            updateCommand.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
            updateCommand.Parameters.AddWithValue("@retailPriceCents", retailPriceCents);
            updateCommand.Parameters.AddWithValue("@isActive", request.IsActive ? 1 : 0);
            updateCommand.Parameters.AddWithValue("@updatedAtUtc", now);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            productId = Guid.NewGuid().ToString("N");
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO Products
                    (Id, StoreId, Name, Description, NormalizedName, RetailPriceCents, WholesalePriceCents, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@id, @storeId, @name, @description, @normalizedName, @retailPriceCents, 0, @isActive, @updatedAtUtc, @updatedAtUtc);
                """;
            insertCommand.Parameters.AddWithValue("@id", productId);
            insertCommand.Parameters.AddWithValue("@storeId", storeId);
            insertCommand.Parameters.AddWithValue("@name", name);
            insertCommand.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
            insertCommand.Parameters.AddWithValue("@retailPriceCents", retailPriceCents);
            insertCommand.Parameters.AddWithValue("@isActive", request.IsActive ? 1 : 0);
            insertCommand.Parameters.AddWithValue("@updatedAtUtc", now);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var product = await GetProductAsync(storeId, productId, cancellationToken)
            ?? throw new InvalidOperationException("Synchronized product was not found.");
        return new ProductSaveResult(ProductSaveStatus.Saved, product);
    }

    public async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.Id,
                   p.StoreId,
                   p.Name,
                   p.Description,
                   p.Type,
                   p.Brand,
                   p.RetailPriceCents,
                   p.PromotionalPriceCents,
                   p.WholesalePriceCents,
                   p.IsActive,
                   p.StockQuantity,
                   p.LowStockThreshold,
                   a.Alias
            FROM Products p
            LEFT JOIN ProductAliases a ON a.ProductId = p.Id
            WHERE p.StoreId = @storeId
            ORDER BY p.Name, a.Alias;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var products = new Dictionary<string, ProductListBuilder>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var productId = reader.GetString(0);
            if (!products.TryGetValue(productId, out var product))
            {
                product = new ProductListBuilder(
                    productId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt32(9) == 1,
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11));
                products.Add(productId, product);
            }

            if (!reader.IsDBNull(12))
            {
                product.Aliases.Add(reader.GetString(12));
            }
        }

        return products.Values
            .Select(product => new ProductResponse(
                product.Id,
                product.StoreId,
                product.Name,
                product.Description,
                product.Type,
                product.Brand,
                FromCents(product.RetailPriceCents),
                product.PromotionalPriceCents is null ? null : FromCents(product.PromotionalPriceCents.Value),
                FromCents(product.WholesalePriceCents),
                product.Aliases,
                product.StockQuantity,
                product.LowStockThreshold,
                product.IsActive))
            .ToArray();
    }

    public async Task<ProductSaveResult> UpdateProductAsync(
        string storeId,
        string productId,
        ProductUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        var normalizedProductId = productId.Trim();
        var name = request.Name.Trim();
        var description = NormalizeOptionalText(request.Description);
        var type = NormalizeOptionalText(request.Type);
        var brand = NormalizeOptionalText(request.Brand);
        var normalizedName = TextNormalizer.NormalizeForLookup(name);
        var retailPriceCents = ToCents(request.RetailPrice);
        long? promotionalPriceCents = request.PromotionalPrice is null ? null : ToCents(request.PromotionalPrice.Value);
        var wholesalePriceCents = ToCents(request.WholesalePrice);
        var stockQuantity = NormalizeOptionalQuantity(request.StockQuantity);
        var lowStockThreshold = NormalizeOptionalQuantity(request.LowStockThreshold);
        var aliases = NormalizeAliases(request.Aliases, normalizedName);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var conflictCommand = connection.CreateCommand();
        conflictCommand.Transaction = transaction;
        conflictCommand.CommandText =
            """
            SELECT Id
            FROM Products
            WHERE StoreId = @storeId
              AND NormalizedName = @normalizedName
              AND Id <> @productId
            LIMIT 1;
            """;
        conflictCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        conflictCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
        conflictCommand.Parameters.AddWithValue("@productId", normalizedProductId);

        var conflictingId = await conflictCommand.ExecuteScalarAsync(cancellationToken) as string;
        if (conflictingId is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProductSaveResult(ProductSaveStatus.Conflict, Product: null);
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE Products
            SET Name = @name,
                Description = @description,
                Type = @type,
                Brand = @brand,
                NormalizedName = @normalizedName,
                RetailPriceCents = @retailPriceCents,
                PromotionalPriceCents = @promotionalPriceCents,
                WholesalePriceCents = @wholesalePriceCents,
                StockQuantity = @stockQuantity,
                LowStockThreshold = @lowStockThreshold,
                IsActive = @isActive,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @productId
              AND StoreId = @storeId;
            """;
        updateCommand.Parameters.AddWithValue("@productId", normalizedProductId);
        updateCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        updateCommand.Parameters.AddWithValue("@name", name);
        updateCommand.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@brand", (object?)brand ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@normalizedName", normalizedName);
        updateCommand.Parameters.AddWithValue("@retailPriceCents", retailPriceCents);
        updateCommand.Parameters.AddWithValue("@promotionalPriceCents", (object?)promotionalPriceCents ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@wholesalePriceCents", wholesalePriceCents);
        updateCommand.Parameters.AddWithValue("@stockQuantity", (object?)stockQuantity ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@lowStockThreshold", (object?)lowStockThreshold ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@isActive", request.IsActive ? 1 : 0);
        updateCommand.Parameters.AddWithValue("@updatedAtUtc", now);

        var rowsUpdated = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (rowsUpdated == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ProductSaveResult(ProductSaveStatus.NotFound, Product: null);
        }

        var deleteAliasesCommand = connection.CreateCommand();
        deleteAliasesCommand.Transaction = transaction;
        deleteAliasesCommand.CommandText = "DELETE FROM ProductAliases WHERE ProductId = @productId;";
        deleteAliasesCommand.Parameters.AddWithValue("@productId", normalizedProductId);
        await deleteAliasesCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var alias in aliases)
        {
            var insertAliasCommand = connection.CreateCommand();
            insertAliasCommand.Transaction = transaction;
            insertAliasCommand.CommandText =
                """
                INSERT INTO ProductAliases (Id, ProductId, Alias, NormalizedAlias, CreatedAtUtc)
                VALUES (@id, @productId, @alias, @normalizedAlias, @createdAtUtc);
                """;
            insertAliasCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            insertAliasCommand.Parameters.AddWithValue("@productId", normalizedProductId);
            insertAliasCommand.Parameters.AddWithValue("@alias", alias.Alias);
            insertAliasCommand.Parameters.AddWithValue("@normalizedAlias", alias.NormalizedAlias);
            insertAliasCommand.Parameters.AddWithValue("@createdAtUtc", now);
            await insertAliasCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var product = new ProductResponse(
            normalizedProductId,
            normalizedStoreId,
            name,
            description,
            type,
            brand,
            FromCents(retailPriceCents),
            promotionalPriceCents is null ? null : FromCents(promotionalPriceCents.Value),
            FromCents(wholesalePriceCents),
            aliases.Select(alias => alias.Alias).ToArray(),
            stockQuantity,
            lowStockThreshold,
            request.IsActive);

        return new ProductSaveResult(ProductSaveStatus.Saved, product);
    }

    public async Task<bool> InactivateProductAsync(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Products
            SET IsActive = 0,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @productId
              AND StoreId = @storeId;
            """;
        command.Parameters.AddWithValue("@productId", productId);
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<AgentPersonaSettingsResponse> GetAgentPersonaAsync(
        string storeId,
        CancellationToken cancellationToken,
        bool activeFaqsOnly = false)
    {
        var normalizedStoreId = storeId.Trim();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Tone, CustomInstructions
            FROM AgentPersonaSettings
            WHERE StoreId = @storeId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AgentPersonaSettingsResponse(
                normalizedStoreId,
                AgentPersonaTones.Amigavel,
                string.Empty,
                Array.Empty<AgentPersonaFaqResponse>());
        }

        var tone = AgentPersonaTones.Normalize(reader.GetString(0));
        var customInstructions = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        await reader.DisposeAsync();

        var faqs = await ReadAgentPersonaFaqsAsync(
            connection,
            normalizedStoreId,
            activeFaqsOnly,
            cancellationToken);

        return new AgentPersonaSettingsResponse(
            normalizedStoreId,
            tone,
            customInstructions,
            faqs);
    }

    public async Task<AgentPersonaSettingsResponse> UpsertAgentPersonaAsync(
        AgentPersonaSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var tone = AgentPersonaTones.Normalize(request.Tone);
        var customInstructions = NormalizeOptionalText(request.CustomInstructions) ?? string.Empty;
        var now = DateTime.UtcNow.ToString("O");
        var faqs = NormalizePersonaFaqs(request.Faqs);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var upsertSettingsCommand = connection.CreateCommand();
        upsertSettingsCommand.Transaction = transaction;
        upsertSettingsCommand.CommandText =
            """
            INSERT INTO AgentPersonaSettings (StoreId, Tone, CustomInstructions, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@storeId, @tone, @customInstructions, @updatedAtUtc, @updatedAtUtc)
            ON CONFLICT(StoreId) DO UPDATE SET
                Tone = excluded.Tone,
                CustomInstructions = excluded.CustomInstructions,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        upsertSettingsCommand.Parameters.AddWithValue("@storeId", storeId);
        upsertSettingsCommand.Parameters.AddWithValue("@tone", tone);
        upsertSettingsCommand.Parameters.AddWithValue("@customInstructions", customInstructions);
        upsertSettingsCommand.Parameters.AddWithValue("@updatedAtUtc", now);
        await upsertSettingsCommand.ExecuteNonQueryAsync(cancellationToken);

        var deleteFaqsCommand = connection.CreateCommand();
        deleteFaqsCommand.Transaction = transaction;
        deleteFaqsCommand.CommandText = "DELETE FROM AgentPersonaFaqs WHERE StoreId = @storeId;";
        deleteFaqsCommand.Parameters.AddWithValue("@storeId", storeId);
        await deleteFaqsCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var faq in faqs)
        {
            var insertFaqCommand = connection.CreateCommand();
            insertFaqCommand.Transaction = transaction;
            insertFaqCommand.CommandText =
                """
                INSERT INTO AgentPersonaFaqs
                    (Id, StoreId, Question, Answer, IsActive, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@id, @storeId, @question, @answer, @isActive, @sortOrder, @updatedAtUtc, @updatedAtUtc);
                """;
            insertFaqCommand.Parameters.AddWithValue("@id", faq.Id);
            insertFaqCommand.Parameters.AddWithValue("@storeId", storeId);
            insertFaqCommand.Parameters.AddWithValue("@question", faq.Question);
            insertFaqCommand.Parameters.AddWithValue("@answer", faq.Answer);
            insertFaqCommand.Parameters.AddWithValue("@isActive", faq.IsActive ? 1 : 0);
            insertFaqCommand.Parameters.AddWithValue("@sortOrder", faq.SortOrder);
            insertFaqCommand.Parameters.AddWithValue("@updatedAtUtc", now);
            await insertFaqCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAgentPersonaAsync(storeId, cancellationToken);
    }

    public async Task<AgentNotificationSettingsResponse> GetAgentNotificationSettingsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT StaffNotificationPhoneNumber, UpdatedAtUtc
            FROM AgentNotificationSettings
            WHERE StoreId = @storeId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AgentNotificationSettingsResponse(
                normalizedStoreId,
                StaffNotificationPhoneNumber: null,
                UpdatedAtUtc: string.Empty);
        }

        return new AgentNotificationSettingsResponse(
            normalizedStoreId,
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetString(1));
    }

    public async Task<AgentNotificationSettingsResponse> UpsertAgentNotificationSettingsAsync(
        AgentNotificationSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var phoneNumber = NormalizeStaffNotificationPhoneNumber(request.StaffNotificationPhoneNumber);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AgentNotificationSettings
                (StoreId, StaffNotificationPhoneNumber, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@storeId, @phoneNumber, @updatedAtUtc, @updatedAtUtc)
            ON CONFLICT(StoreId) DO UPDATE SET
                StaffNotificationPhoneNumber = excluded.StaffNotificationPhoneNumber,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@phoneNumber", (object?)phoneNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new AgentNotificationSettingsResponse(storeId, phoneNumber, now);
    }

    public async Task RecordApplicationLogAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var normalizedText = text.Trim();
        if (normalizedText.Length == 0)
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ApplicationLogs
                (Id, CreatedAtUtc, Text)
            VALUES
                (@id, @createdAtUtc, @text);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("@createdAtUtc", now);
        command.Parameters.AddWithValue("@text", normalizedText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentAutomatedCampaignResponse>> GetAutomatedCampaignsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateAutomatedCampaignSelectCommand(connection);
        command.CommandText +=
            """
            WHERE c.StoreId = @storeId
              AND c.IsDeleted = 0
            ORDER BY c.CreatedAtUtc DESC, c.Name;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());

        return await ReadAutomatedCampaignResponsesAsync(connection, command, cancellationToken);
    }

    public async Task<AgentAutomatedCampaignResponse?> GetAutomatedCampaignAsync(
        string storeId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateAutomatedCampaignSelectCommand(connection);
        command.CommandText +=
            """
            WHERE c.StoreId = @storeId
              AND c.Id = @campaignId
              AND c.IsDeleted = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@campaignId", campaignId.Trim());

        var campaigns = await ReadAutomatedCampaignResponsesAsync(connection, command, cancellationToken);
        return campaigns.FirstOrDefault();
    }

    public async Task<IReadOnlyList<AgentAutomatedCampaignResponse>> GetDueAutomatedCampaignsAsync(
        DateTimeOffset localNow,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateAutomatedCampaignSelectCommand(connection);
        command.CommandText +=
            """
            WHERE c.IsActive = 1
              AND c.IsDeleted = 0;
            """;

        var campaigns = await ReadAutomatedCampaignResponsesAsync(connection, command, cancellationToken);
        return campaigns
            .Where(campaign => IsAutomatedCampaignDue(campaign, localNow))
            .ToArray();
    }

    public async Task<AgentAutomatedCampaignResponse?> UpsertAutomatedCampaignAsync(
        AgentAutomatedCampaignUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var campaignId = string.IsNullOrWhiteSpace(request.Id)
            ? Guid.NewGuid().ToString("N")
            : request.Id.Trim();
        var type = AgentAutomatedCampaignTypes.Normalize(request.Type);
        var name = request.Name.Trim();
        var productId = string.IsNullOrWhiteSpace(request.ProductId) ? null : request.ProductId.Trim();
        var message = request.Message.Trim();
        var dailyRunTime = NormalizeDailyRunTime(request.DailyRunTime);
        var cooldownDays = Math.Max(1, request.CooldownDays ?? 7);
        var inactiveDaysThreshold = type == AgentAutomatedCampaignTypes.InactiveCustomers
            ? (int?)Math.Max(1, request.InactiveDaysThreshold ?? 30)
            : null;
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (productId is not null)
        {
            var productCommand = connection.CreateCommand();
            productCommand.CommandText =
                """
                SELECT 1
                FROM Products
                WHERE StoreId = @storeId
                  AND Id = @productId
                LIMIT 1;
                """;
            productCommand.Parameters.AddWithValue("@storeId", storeId);
            productCommand.Parameters.AddWithValue("@productId", productId);
            if (await productCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            """
            SELECT 1
            FROM AgentAutomatedCampaigns
            WHERE StoreId = @storeId
              AND Id = @campaignId
              AND IsDeleted = 0
            LIMIT 1;
            """;
        existsCommand.Parameters.AddWithValue("@storeId", storeId);
        existsCommand.Parameters.AddWithValue("@campaignId", campaignId);
        var exists = await existsCommand.ExecuteScalarAsync(cancellationToken) is not null;

        var command = connection.CreateCommand();
        command.CommandText = exists
            ? """
              UPDATE AgentAutomatedCampaigns
              SET Type = @type,
                  Name = @name,
                  ProductId = @productId,
                  Message = @message,
                  IsActive = @isActive,
                  DailyRunTime = @dailyRunTime,
                  CooldownDays = @cooldownDays,
                  InactiveDaysThreshold = @inactiveDaysThreshold,
                  UpdatedAtUtc = @updatedAtUtc
              WHERE StoreId = @storeId
                AND Id = @campaignId
                AND IsDeleted = 0;
              """
            : """
              INSERT INTO AgentAutomatedCampaigns
                  (Id, StoreId, Type, Name, ProductId, Message, IsActive, IsDeleted,
                   DailyRunTime, CooldownDays, InactiveDaysThreshold, CreatedAtUtc, UpdatedAtUtc)
              VALUES
                  (@campaignId, @storeId, @type, @name, @productId, @message, @isActive, 0,
                   @dailyRunTime, @cooldownDays, @inactiveDaysThreshold, @updatedAtUtc, @updatedAtUtc);
              """;
        command.Parameters.AddWithValue("@campaignId", campaignId);
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@productId", (object?)productId ?? DBNull.Value);
        command.Parameters.AddWithValue("@message", message);
        command.Parameters.AddWithValue("@isActive", request.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@dailyRunTime", dailyRunTime);
        command.Parameters.AddWithValue("@cooldownDays", cooldownDays);
        command.Parameters.AddWithValue("@inactiveDaysThreshold", (object?)inactiveDaysThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAutomatedCampaignAsync(storeId, campaignId, cancellationToken);
    }

    public async Task<bool> DeleteAutomatedCampaignAsync(
        string storeId,
        string campaignId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AgentAutomatedCampaigns
            SET IsDeleted = 1,
                IsActive = 0,
                UpdatedAtUtc = @updatedAtUtc
            WHERE StoreId = @storeId
              AND Id = @campaignId
              AND IsDeleted = 0;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@campaignId", campaignId.Trim());
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlySet<string>> GetRecentSuccessfulCampaignRecipientsAsync(
        string campaignId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT PhoneNumber
            FROM AgentAutomatedCampaignDeliveries
            WHERE CampaignId = @campaignId
              AND Sent = 1
              AND CreatedAtUtc >= @sinceUtc;
            """;
        command.Parameters.AddWithValue("@campaignId", campaignId.Trim());
        command.Parameters.AddWithValue("@sinceUtc", sinceUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        var recipients = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(reader.GetString(0));
            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                recipients.Add(phoneNumber);
            }
        }

        return recipients;
    }

    public async Task<AgentAutomatedCampaignRunResponse> RecordAutomatedCampaignRunAsync(
        AgentAutomatedCampaignResponse campaign,
        string startedAtUtc,
        string completedAtUtc,
        int eligibleCount,
        int skippedCooldownCount,
        IReadOnlyList<AgentSendResultItemResponse> deliveries,
        string? error,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var runCommand = connection.CreateCommand();
        runCommand.Transaction = transaction;
        runCommand.CommandText =
            """
            INSERT INTO AgentAutomatedCampaignRuns
                (Id, CampaignId, StoreId, StartedAtUtc, CompletedAtUtc, EligibleCount,
                 SkippedCooldownCount, SentCount, FailedCount, Error)
            VALUES
                (@id, @campaignId, @storeId, @startedAtUtc, @completedAtUtc, @eligibleCount,
                 @skippedCooldownCount, @sentCount, @failedCount, @error);
            """;
        runCommand.Parameters.AddWithValue("@id", runId);
        runCommand.Parameters.AddWithValue("@campaignId", campaign.Id);
        runCommand.Parameters.AddWithValue("@storeId", campaign.StoreId);
        runCommand.Parameters.AddWithValue("@startedAtUtc", startedAtUtc);
        runCommand.Parameters.AddWithValue("@completedAtUtc", completedAtUtc);
        runCommand.Parameters.AddWithValue("@eligibleCount", eligibleCount);
        runCommand.Parameters.AddWithValue("@skippedCooldownCount", skippedCooldownCount);
        runCommand.Parameters.AddWithValue("@sentCount", deliveries.Count(delivery => delivery.Sent));
        runCommand.Parameters.AddWithValue("@failedCount", deliveries.Count(delivery => !delivery.Sent));
        runCommand.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        await runCommand.ExecuteNonQueryAsync(cancellationToken);

        var deliveryResponses = new List<AgentAutomatedCampaignDeliveryResponse>();
        foreach (var delivery in deliveries)
        {
            var deliveryId = Guid.NewGuid().ToString("N");
            var deliveryCommand = connection.CreateCommand();
            deliveryCommand.Transaction = transaction;
            deliveryCommand.CommandText =
                """
                INSERT INTO AgentAutomatedCampaignDeliveries
                    (Id, CampaignId, RunId, StoreId, PhoneNumber, Sent, Error, CreatedAtUtc)
                VALUES
                    (@id, @campaignId, @runId, @storeId, @phoneNumber, @sent, @error, @createdAtUtc);
                """;
            deliveryCommand.Parameters.AddWithValue("@id", deliveryId);
            deliveryCommand.Parameters.AddWithValue("@campaignId", campaign.Id);
            deliveryCommand.Parameters.AddWithValue("@runId", runId);
            deliveryCommand.Parameters.AddWithValue("@storeId", campaign.StoreId);
            deliveryCommand.Parameters.AddWithValue("@phoneNumber", delivery.PhoneNumber);
            deliveryCommand.Parameters.AddWithValue("@sent", delivery.Sent ? 1 : 0);
            deliveryCommand.Parameters.AddWithValue("@error", (object?)delivery.Error ?? DBNull.Value);
            deliveryCommand.Parameters.AddWithValue("@createdAtUtc", completedAtUtc);
            await deliveryCommand.ExecuteNonQueryAsync(cancellationToken);

            deliveryResponses.Add(new AgentAutomatedCampaignDeliveryResponse(
                deliveryId,
                campaign.Id,
                runId,
                delivery.PhoneNumber,
                delivery.Sent,
                delivery.Error,
                completedAtUtc));
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE AgentAutomatedCampaigns
            SET LastRunAtUtc = @completedAtUtc,
                UpdatedAtUtc = @completedAtUtc
            WHERE Id = @campaignId
              AND StoreId = @storeId;
            """;
        updateCommand.Parameters.AddWithValue("@completedAtUtc", completedAtUtc);
        updateCommand.Parameters.AddWithValue("@campaignId", campaign.Id);
        updateCommand.Parameters.AddWithValue("@storeId", campaign.StoreId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new AgentAutomatedCampaignRunResponse(
            runId,
            campaign.Id,
            campaign.StoreId,
            startedAtUtc,
            completedAtUtc,
            eligibleCount,
            skippedCooldownCount,
            deliveryResponses.Count(delivery => delivery.Sent),
            deliveryResponses.Count(delivery => !delivery.Sent),
            error,
            deliveryResponses);
    }

    public async Task<AgentFeedbackSettingsResponse> GetAgentFeedbackSettingsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT StoreId,
                   IsPostOrderEnabled,
                   PostOrderDelayMinutes,
                   AcceptedFormat,
                   RequestMessage,
                   IsPeriodicSurveyEnabled,
                   PeriodicSurveyDays,
                   PeriodicSurveySampleSize,
                   LastPeriodicSurveyRunAtUtc,
                   UpdatedAtUtc
            FROM AgentFeedbackSettings
            WHERE StoreId = @storeId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return CreateDefaultFeedbackSettings(normalizedStoreId);
        }

        return ReadFeedbackSettings(reader);
    }

    public async Task<AgentFeedbackSettingsResponse> UpsertAgentFeedbackSettingsAsync(
        AgentFeedbackSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeId = request.StoreId.Trim();
        var postOrderDelayMinutes = Math.Max(1, request.PostOrderDelayMinutes ?? DefaultFeedbackDelayMinutes);
        var acceptedFormat = AgentFeedbackFormats.Normalize(request.AcceptedFormat);
        var requestMessage = NormalizeOptionalText(request.RequestMessage) ?? DefaultFeedbackRequestMessage;
        var periodicSurveyDays = Math.Max(1, request.PeriodicSurveyDays ?? DefaultFeedbackPeriodicSurveyDays);
        var periodicSurveySampleSize = Math.Max(1, request.PeriodicSurveySampleSize ?? DefaultFeedbackPeriodicSurveySampleSize);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AgentFeedbackSettings
                (StoreId, IsPostOrderEnabled, PostOrderDelayMinutes, AcceptedFormat, RequestMessage,
                 IsPeriodicSurveyEnabled, PeriodicSurveyDays, PeriodicSurveySampleSize, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@storeId, @isPostOrderEnabled, @postOrderDelayMinutes, @acceptedFormat, @requestMessage,
                 @isPeriodicSurveyEnabled, @periodicSurveyDays, @periodicSurveySampleSize, @updatedAtUtc, @updatedAtUtc)
            ON CONFLICT(StoreId) DO UPDATE SET
                IsPostOrderEnabled = excluded.IsPostOrderEnabled,
                PostOrderDelayMinutes = excluded.PostOrderDelayMinutes,
                AcceptedFormat = excluded.AcceptedFormat,
                RequestMessage = excluded.RequestMessage,
                IsPeriodicSurveyEnabled = excluded.IsPeriodicSurveyEnabled,
                PeriodicSurveyDays = excluded.PeriodicSurveyDays,
                PeriodicSurveySampleSize = excluded.PeriodicSurveySampleSize,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@isPostOrderEnabled", request.IsPostOrderEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@postOrderDelayMinutes", postOrderDelayMinutes);
        command.Parameters.AddWithValue("@acceptedFormat", acceptedFormat);
        command.Parameters.AddWithValue("@requestMessage", requestMessage);
        command.Parameters.AddWithValue("@isPeriodicSurveyEnabled", request.IsPeriodicSurveyEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@periodicSurveyDays", periodicSurveyDays);
        command.Parameters.AddWithValue("@periodicSurveySampleSize", periodicSurveySampleSize);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetAgentFeedbackSettingsAsync(storeId, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentFeedbackSolicitationResponse>> GetAgentFeedbackSolicitationsAsync(
        string storeId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateFeedbackSolicitationSelectCommand(connection);
        command.CommandText +=
            """
            WHERE s.StoreId = @storeId
            ORDER BY s.CreatedAtUtc DESC, s.Id DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        return await ReadFeedbackSolicitationsAsync(command, cancellationToken);
    }

    public async Task<AgentFeedbackSolicitationResponse?> GetAgentFeedbackSolicitationAsync(
        string storeId,
        string solicitationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateFeedbackSolicitationSelectCommand(connection);
        command.CommandText +=
            """
            WHERE s.StoreId = @storeId
              AND s.Id = @solicitationId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@solicitationId", solicitationId.Trim());

        return (await ReadFeedbackSolicitationsAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<AgentFeedbackSolicitationResponse>> GetDueAgentFeedbackSolicitationsAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = CreateFeedbackSolicitationSelectCommand(connection);
        command.CommandText +=
            """
            WHERE s.Status = @status
              AND s.DueAtUtc <= @nowUtc
            ORDER BY s.DueAtUtc, s.CreatedAtUtc
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Pending);
        command.Parameters.AddWithValue("@nowUtc", nowUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        return await ReadFeedbackSolicitationsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentFeedbackSettingsResponse>> GetDuePeriodicFeedbackSettingsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT StoreId,
                   IsPostOrderEnabled,
                   PostOrderDelayMinutes,
                   AcceptedFormat,
                   RequestMessage,
                   IsPeriodicSurveyEnabled,
                   PeriodicSurveyDays,
                   PeriodicSurveySampleSize,
                   LastPeriodicSurveyRunAtUtc,
                   UpdatedAtUtc
            FROM AgentFeedbackSettings
            WHERE IsPeriodicSurveyEnabled = 1;
            """;

        var settings = new List<AgentFeedbackSettingsResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var setting = ReadFeedbackSettings(reader);
            if (IsPeriodicFeedbackDue(setting, nowUtc))
            {
                settings.Add(setting);
            }
        }

        return settings;
    }

    public async Task<IReadOnlyList<string>> GetPeriodicFeedbackCandidatesAsync(
        string storeId,
        int sampleSize,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PhoneNumber
            FROM Orders
            WHERE StoreId = @storeId
              AND Status = @status
            GROUP BY PhoneNumber
            ORDER BY random()
            LIMIT @sampleSize;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);
        command.Parameters.AddWithValue("@sampleSize", Math.Max(1, sampleSize));

        var candidates = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(PhoneNumberNormalizer.ToBrazilNationalPhone(reader.GetString(0)));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentFeedbackSolicitationResponse>> CreatePeriodicFeedbackSolicitationsAsync(
        AgentFeedbackSettingsResponse settings,
        IReadOnlyList<string> phoneNumbers,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (phoneNumbers.Count == 0)
        {
            return Array.Empty<AgentFeedbackSolicitationResponse>();
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var createdIds = new List<string>();
        var timestamp = nowUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        foreach (var phoneNumber in phoneNumbers
                     .Select(PhoneNumberNormalizer.ToBrazilNationalPhone)
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.Ordinal))
        {
            var id = Guid.NewGuid().ToString("N");
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO AgentFeedbackSolicitations
                    (Id, StoreId, OrderId, PhoneNumber, Kind, Status, Message, DueAtUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    (@id, @storeId, NULL, @phoneNumber, @kind, @status, @message, @dueAtUtc, @updatedAtUtc, @updatedAtUtc);
                """;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@storeId", settings.StoreId);
            command.Parameters.AddWithValue("@phoneNumber", phoneNumber);
            command.Parameters.AddWithValue("@kind", AgentFeedbackSolicitationKinds.Periodic);
            command.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Pending);
            command.Parameters.AddWithValue("@message", settings.RequestMessage);
            command.Parameters.AddWithValue("@dueAtUtc", timestamp);
            command.Parameters.AddWithValue("@updatedAtUtc", timestamp);
            await command.ExecuteNonQueryAsync(cancellationToken);
            createdIds.Add(id);
        }

        await transaction.CommitAsync(cancellationToken);
        await UpdateFeedbackPeriodicSurveyRunAsync(settings.StoreId, nowUtc, cancellationToken);

        var created = new List<AgentFeedbackSolicitationResponse>();
        foreach (var id in createdIds)
        {
            var solicitation = await GetAgentFeedbackSolicitationAsync(settings.StoreId, id, cancellationToken);
            if (solicitation is not null)
            {
                created.Add(solicitation);
            }
        }

        return created;
    }

    public async Task ScheduleOrderFeedbackSolicitationAsync(
        string storeId,
        string orderId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await ScheduleOrderFeedbackSolicitationAsync(connection, storeId.Trim(), orderId.Trim(), completedAtUtc, cancellationToken);
    }

    public async Task MarkAgentFeedbackSolicitationSentAsync(
        string solicitationId,
        CancellationToken cancellationToken)
    {
        await UpdateAgentFeedbackSolicitationStatusAsync(
            solicitationId,
            AgentFeedbackSolicitationStatuses.Sent,
            lastError: null,
            setSentAt: true,
            setRespondedAt: false,
            cancellationToken);
    }

    public async Task MarkAgentFeedbackSolicitationFailedAsync(
        string solicitationId,
        string error,
        CancellationToken cancellationToken)
    {
        await UpdateAgentFeedbackSolicitationStatusAsync(
            solicitationId,
            AgentFeedbackSolicitationStatuses.Failed,
            error,
            setSentAt: false,
            setRespondedAt: false,
            cancellationToken);
    }

    public async Task<AgentFeedbackResponseTarget?> GetOpenAgentFeedbackResponseTargetAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "s.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT s.Id, COALESCE(settings.AcceptedFormat, @defaultFormat)
            FROM AgentFeedbackSolicitations s
            LEFT JOIN AgentFeedbackSettings settings ON settings.StoreId = s.StoreId
            WHERE s.StoreId = @storeId
              AND {phoneFilter}
              AND s.Status = @status
            ORDER BY s.SentAtUtc DESC, s.CreatedAtUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);
        command.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Sent);
        command.Parameters.AddWithValue("@defaultFormat", AgentFeedbackFormats.Both);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AgentFeedbackResponseTarget(reader.GetString(0), AgentFeedbackFormats.Normalize(reader.GetString(1)))
            : null;
    }

    public async Task RecordDetectedAgentFeedbackAsync(
        AgentFeedbackRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = command.StoreId.Trim();
        var normalizedPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(command.PhoneNumber);
        var customerMessage = command.CustomerMessage.Trim();
        var solicitationId = Guid.NewGuid().ToString("N");
        var responseId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var solicitationCommand = connection.CreateCommand();
        solicitationCommand.Transaction = transaction;
        solicitationCommand.CommandText =
            """
            INSERT INTO AgentFeedbackSolicitations
                (Id, StoreId, OrderId, PhoneNumber, Kind, Status, Message, DueAtUtc, SentAtUtc, RespondedAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, NULL, @phoneNumber, @kind, @status, @message, @now, NULL, @now, @now, @now);
            """;
        solicitationCommand.Parameters.AddWithValue("@id", solicitationId);
        solicitationCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        solicitationCommand.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        solicitationCommand.Parameters.AddWithValue("@kind", AgentFeedbackSolicitationKinds.AgentDetected);
        solicitationCommand.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Responded);
        solicitationCommand.Parameters.AddWithValue("@message", "Feedback detectado automaticamente pelo agente.");
        solicitationCommand.Parameters.AddWithValue("@now", now);
        await solicitationCommand.ExecuteNonQueryAsync(cancellationToken);

        var responseCommand = connection.CreateCommand();
        responseCommand.Transaction = transaction;
        responseCommand.CommandText =
            """
            INSERT INTO AgentFeedbackResponses
                (Id, SolicitationId, StoreId, PhoneNumber, ResponseType, Text, MediaUrl, MediaContentType,
                 Category, Sentiment, CustomerClassification, Score, Summary, AnalyzedAtUtc,
                 PromptResponseId, ConversationId, AiOutputJson, CreatedAtUtc)
            VALUES
                (@id, @solicitationId, @storeId, @phoneNumber, @responseType, @text, NULL, NULL,
                 @category, @sentiment, @customerClassification, @score, @summary, @analyzedAtUtc,
                 @promptResponseId, @conversationId, @aiOutputJson, @createdAtUtc);
            """;
        responseCommand.Parameters.AddWithValue("@id", responseId);
        responseCommand.Parameters.AddWithValue("@solicitationId", solicitationId);
        responseCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        responseCommand.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        responseCommand.Parameters.AddWithValue("@responseType", AgentFeedbackResponseTypes.Text);
        responseCommand.Parameters.AddWithValue("@text", customerMessage);
        AddFeedbackAnalysisParameters(responseCommand, command.Analysis, now);
        responseCommand.Parameters.AddWithValue("@promptResponseId", (object?)NormalizeOptionalText(command.PromptResponseId) ?? DBNull.Value);
        responseCommand.Parameters.AddWithValue("@conversationId", (object?)NormalizeOptionalText(command.ConversationId) ?? DBNull.Value);
        responseCommand.Parameters.AddWithValue("@aiOutputJson", command.AiOutputJson);
        responseCommand.Parameters.AddWithValue("@createdAtUtc", now);
        await responseCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> RecordSolicitedAgentFeedbackTextResponseAsync(
        string solicitationId,
        AgentFeedbackRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedSolicitationId = solicitationId.Trim();
        var normalizedStoreId = command.StoreId.Trim();
        var normalizedPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(command.PhoneNumber);
        var customerMessage = command.CustomerMessage.Trim();
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var responseCommand = connection.CreateCommand();
        responseCommand.Transaction = transaction;
        responseCommand.CommandText =
            """
            INSERT INTO AgentFeedbackResponses
                (Id, SolicitationId, StoreId, PhoneNumber, ResponseType, Text, MediaUrl, MediaContentType,
                 Category, Sentiment, CustomerClassification, Score, Summary, AnalyzedAtUtc,
                 PromptResponseId, ConversationId, AiOutputJson, CreatedAtUtc)
            VALUES
                (@id, @solicitationId, @storeId, @phoneNumber, @responseType, @text, NULL, NULL,
                 @category, @sentiment, @customerClassification, @score, @summary, @analyzedAtUtc,
                 @promptResponseId, @conversationId, @aiOutputJson, @createdAtUtc)
            ON CONFLICT DO NOTHING;
            """;
        responseCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        responseCommand.Parameters.AddWithValue("@solicitationId", normalizedSolicitationId);
        responseCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        responseCommand.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        responseCommand.Parameters.AddWithValue("@responseType", AgentFeedbackResponseTypes.Text);
        responseCommand.Parameters.AddWithValue("@text", customerMessage);
        AddFeedbackAnalysisParameters(responseCommand, command.Analysis, now);
        responseCommand.Parameters.AddWithValue("@promptResponseId", (object?)NormalizeOptionalText(command.PromptResponseId) ?? DBNull.Value);
        responseCommand.Parameters.AddWithValue("@conversationId", (object?)NormalizeOptionalText(command.ConversationId) ?? DBNull.Value);
        responseCommand.Parameters.AddWithValue("@aiOutputJson", command.AiOutputJson);
        responseCommand.Parameters.AddWithValue("@createdAtUtc", now);
        var inserted = await responseCommand.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var solicitationCommand = connection.CreateCommand();
        solicitationCommand.Transaction = transaction;
        solicitationCommand.CommandText =
            """
            UPDATE AgentFeedbackSolicitations
            SET Status = @status,
                RespondedAtUtc = @respondedAtUtc,
                LastError = NULL,
                UpdatedAtUtc = @respondedAtUtc
            WHERE Id = @solicitationId
              AND StoreId = @storeId;
            """;
        solicitationCommand.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Responded);
        solicitationCommand.Parameters.AddWithValue("@respondedAtUtc", now);
        solicitationCommand.Parameters.AddWithValue("@solicitationId", normalizedSolicitationId);
        solicitationCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        var updated = await solicitationCommand.ExecuteNonQueryAsync(cancellationToken) > 0;

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> TryRecordAgentFeedbackResponseAsync(
        string storeId,
        string phoneNumber,
        string? text,
        string? mediaUrl,
        string? mediaContentType,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        var normalizedPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(phoneNumber);
        var normalizedText = NormalizeOptionalText(text);
        var normalizedMediaUrl = NormalizeOptionalText(mediaUrl);
        var normalizedMediaContentType = NormalizeOptionalText(mediaContentType);
        var isAudio = normalizedMediaUrl is not null &&
            normalizedMediaContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        var phoneFilter = AddPhoneStorageCandidateParameters(selectCommand, "s.PhoneNumber", "phone", phoneNumber);
        selectCommand.CommandText =
            $"""
            SELECT s.Id, COALESCE(settings.AcceptedFormat, @defaultFormat), s.PhoneNumber
            FROM AgentFeedbackSolicitations s
            LEFT JOIN AgentFeedbackSettings settings ON settings.StoreId = s.StoreId
            WHERE s.StoreId = @storeId
              AND {phoneFilter}
              AND s.Status = @status
            ORDER BY s.SentAtUtc DESC, s.CreatedAtUtc DESC
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        selectCommand.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Sent);
        selectCommand.Parameters.AddWithValue("@defaultFormat", AgentFeedbackFormats.Both);

        var foundSolicitation = false;
        var solicitationId = string.Empty;
        var acceptedFormat = AgentFeedbackFormats.Both;
        var storedPhoneNumber = normalizedPhoneNumber;
        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                foundSolicitation = true;
                solicitationId = reader.GetString(0);
                acceptedFormat = AgentFeedbackFormats.Normalize(reader.GetString(1));
                var storedNormalizedPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(reader.GetString(2));
                if (!string.IsNullOrWhiteSpace(storedNormalizedPhoneNumber))
                {
                    storedPhoneNumber = storedNormalizedPhoneNumber;
                }
            }
        }

        if (!foundSolicitation)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        string responseType;
        if (isAudio && AgentFeedbackFormats.AcceptsAudio(acceptedFormat))
        {
            responseType = AgentFeedbackResponseTypes.Audio;
        }
        else if (normalizedText is not null && AgentFeedbackFormats.AcceptsText(acceptedFormat))
        {
            responseType = AgentFeedbackResponseTypes.Text;
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var responseCommand = connection.CreateCommand();
        responseCommand.Transaction = transaction;
        responseCommand.CommandText =
            """
            INSERT INTO AgentFeedbackResponses
                (Id, SolicitationId, StoreId, PhoneNumber, ResponseType, Text, MediaUrl, MediaContentType, CreatedAtUtc)
            VALUES
                (@id, @solicitationId, @storeId, @phoneNumber, @responseType, @text, @mediaUrl, @mediaContentType, @createdAtUtc)
            ON CONFLICT DO NOTHING;
            """;
        responseCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        responseCommand.Parameters.AddWithValue("@solicitationId", solicitationId);
        responseCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        responseCommand.Parameters.AddWithValue("@phoneNumber", storedPhoneNumber);
        responseCommand.Parameters.AddWithValue("@responseType", responseType);
        responseCommand.Parameters.AddWithValue("@text", responseType == AgentFeedbackResponseTypes.Text ? (object?)normalizedText ?? DBNull.Value : DBNull.Value);
        responseCommand.Parameters.AddWithValue("@mediaUrl", responseType == AgentFeedbackResponseTypes.Audio ? (object?)normalizedMediaUrl ?? DBNull.Value : DBNull.Value);
        responseCommand.Parameters.AddWithValue("@mediaContentType", responseType == AgentFeedbackResponseTypes.Audio ? (object?)normalizedMediaContentType ?? DBNull.Value : DBNull.Value);
        responseCommand.Parameters.AddWithValue("@createdAtUtc", now);
        var inserted = await responseCommand.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var solicitationCommand = connection.CreateCommand();
        solicitationCommand.Transaction = transaction;
        solicitationCommand.CommandText =
            """
            UPDATE AgentFeedbackSolicitations
            SET Status = @status,
                RespondedAtUtc = @respondedAtUtc,
                LastError = NULL,
                UpdatedAtUtc = @respondedAtUtc
            WHERE Id = @solicitationId;
            """;
        solicitationCommand.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Responded);
        solicitationCommand.Parameters.AddWithValue("@respondedAtUtc", now);
        solicitationCommand.Parameters.AddWithValue("@solicitationId", solicitationId);
        await solicitationCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task UpdateFeedbackPeriodicSurveyRunAsync(
        string storeId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var now = nowUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AgentFeedbackSettings
            SET LastPeriodicSurveyRunAtUtc = @lastRun,
                UpdatedAtUtc = @lastRun
            WHERE StoreId = @storeId;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@lastRun", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProductResponse?> GetProductAsync(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.Id,
                   p.StoreId,
                   p.Name,
                   p.Description,
                   p.Type,
                   p.Brand,
                   p.RetailPriceCents,
                   p.PromotionalPriceCents,
                   p.WholesalePriceCents,
                   p.IsActive,
                   p.StockQuantity,
                   p.LowStockThreshold,
                   a.Alias
            FROM Products p
            LEFT JOIN ProductAliases a ON a.ProductId = p.Id
            WHERE p.StoreId = @storeId
              AND p.Id = @productId
            ORDER BY a.Alias;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@productId", productId);

        ProductListBuilder? product = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            product ??= new ProductListBuilder(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt32(9) == 1,
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11));

            if (!reader.IsDBNull(12))
            {
                product.Aliases.Add(reader.GetString(12));
            }
        }

        return product is null
            ? null
            : new ProductResponse(
                product.Id,
                product.StoreId,
                product.Name,
                product.Description,
                product.Type,
                product.Brand,
                FromCents(product.RetailPriceCents),
                product.PromotionalPriceCents is null ? null : FromCents(product.PromotionalPriceCents.Value),
                FromCents(product.WholesalePriceCents),
                product.Aliases,
                product.StockQuantity,
                product.LowStockThreshold,
                product.IsActive);
    }

    public async Task<IReadOnlyList<ProductResponse>> SearchActiveProductDetailsByNameAsync(
        string storeId,
        string productName,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        var normalizedQuery = TextNormalizer.NormalizeForLookup(productName);
        var normalizedLimit = Math.Clamp(limit, 1, 10);

        if (normalizedQuery.Length < 4)
        {
            return Array.Empty<ProductResponse>();
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var candidatesCommand = connection.CreateCommand();
        candidatesCommand.CommandText =
            """
            SELECT p.Id,
                   p.Name,
                   p.NormalizedName,
                   a.NormalizedAlias
            FROM Products p
            LEFT JOIN ProductAliases a ON a.ProductId = p.Id
            WHERE p.StoreId = @storeId
              AND p.IsActive = 1
            ORDER BY p.Name, a.Alias;
            """;
        candidatesCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);

        var scores = new Dictionary<string, ProductSearchMatch>(StringComparer.Ordinal);
        await using (var reader = await candidatesCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var candidate = new ProductSearchCandidate(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3));

                var score = ScoreProductSearchMatch(normalizedQuery, candidate.NormalizedName, candidate.NormalizedAlias);
                if (score is null)
                {
                    continue;
                }

                if (!scores.TryGetValue(candidate.Id, out var existing) || score.Value < existing.Score)
                {
                    scores[candidate.Id] = new ProductSearchMatch(candidate.Id, candidate.Name, score.Value);
                }
            }
        }

        var matches = scores.Values
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .Take(normalizedLimit)
            .ToArray();

        if (matches.Length == 0)
        {
            return Array.Empty<ProductResponse>();
        }

        var idParameters = matches
            .Select((_, index) => $"@id{index}")
            .ToArray();

        var detailsCommand = connection.CreateCommand();
        detailsCommand.CommandText =
            $"""
            SELECT p.Id,
                   p.StoreId,
                   p.Name,
                   p.Description,
                   p.Type,
                   p.Brand,
                   p.RetailPriceCents,
                   p.PromotionalPriceCents,
                   p.WholesalePriceCents,
                   p.IsActive,
                   p.StockQuantity,
                   p.LowStockThreshold,
                   a.Alias
            FROM Products p
            LEFT JOIN ProductAliases a ON a.ProductId = p.Id
            WHERE p.StoreId = @storeId
              AND p.IsActive = 1
              AND p.Id IN ({string.Join(", ", idParameters)})
            ORDER BY p.Name, a.Alias;
            """;
        detailsCommand.Parameters.AddWithValue("@storeId", normalizedStoreId);
        for (var index = 0; index < matches.Length; index++)
        {
            detailsCommand.Parameters.AddWithValue(idParameters[index], matches[index].Id);
        }

        var products = new Dictionary<string, ProductListBuilder>(StringComparer.Ordinal);
        await using (var reader = await detailsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = reader.GetString(0);
                if (!products.TryGetValue(productId, out var product))
                {
                    product = new ProductListBuilder(
                        productId,
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetInt64(6),
                        reader.IsDBNull(7) ? null : reader.GetInt64(7),
                        reader.GetInt64(8),
                        reader.GetInt32(9) == 1,
                        reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        reader.IsDBNull(11) ? null : reader.GetInt32(11));
                    products.Add(productId, product);
                }

                if (!reader.IsDBNull(12))
                {
                    product.Aliases.Add(reader.GetString(12));
                }
            }
        }

        var rankById = matches
            .Select((match, index) => new { match.Id, Index = index })
            .ToDictionary(match => match.Id, match => match.Index, StringComparer.Ordinal);

        return products.Values
            .Select(CreateProductResponse)
            .OrderBy(product => rankById.GetValueOrDefault(product.Id, int.MaxValue))
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentCampaignCustomerResponse>> GetProductCampaignCustomersAsync(
        string storeId,
        string productId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT o.PhoneNumber,
                   MAX(o.CreatedAtUtc) AS LastOrderAtUtc,
                   COUNT(DISTINCT o.Id) AS TotalOrders
            FROM Orders o
            INNER JOIN OrderItems oi ON oi.OrderId = o.Id
            WHERE o.StoreId = @storeId
              AND o.Status = @status
              AND oi.ProductId = @productId
            GROUP BY o.PhoneNumber
            ORDER BY LastOrderAtUtc DESC, o.PhoneNumber;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);
        command.Parameters.AddWithValue("@productId", productId);

        var customers = new List<AgentCampaignCustomerResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new AgentCampaignCustomerResponse(
                PhoneNumberNormalizer.ToBrazilNationalPhone(reader.GetString(0)),
                reader.GetString(1),
                Convert.ToInt32(reader.GetInt64(2))));
        }

        return customers
            .Where(customer => !string.IsNullOrWhiteSpace(customer.PhoneNumber))
            .GroupBy(customer => customer.PhoneNumber, StringComparer.Ordinal)
            .Select(group => new AgentCampaignCustomerResponse(
                group.Key,
                group.Max(customer => customer.LastOrderAtUtc) ?? string.Empty,
                group.Sum(customer => customer.TotalOrders)))
            .OrderByDescending(customer => customer.LastOrderAtUtc, StringComparer.Ordinal)
            .ThenBy(customer => customer.PhoneNumber, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<AgentCustomerRecurrenceResponse>> GetAgentCustomerRecurrencesAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PhoneNumber, CreatedAtUtc
            FROM Orders
            WHERE StoreId = @storeId
              AND Status = @status
            ORDER BY PhoneNumber, CreatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);

        var ordersByCustomer = new Dictionary<string, List<DateTimeOffset>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(reader.GetString(0));
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                continue;
            }

            var createdAtUtc = reader.GetString(1);
            if (!DateTimeOffset.TryParse(
                    createdAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedDate))
            {
                continue;
            }

            if (!ordersByCustomer.TryGetValue(phoneNumber, out var orders))
            {
                orders = new List<DateTimeOffset>();
                ordersByCustomer.Add(phoneNumber, orders);
            }

            orders.Add(parsedDate.ToUniversalTime());
        }

        var now = DateTimeOffset.UtcNow;
        return ordersByCustomer
            .Select(item => CreateCustomerRecurrence(item.Key, item.Value, now))
            .OrderByDescending(customer => customer.IsOverdue)
            .ThenByDescending(customer => customer.DaysSinceLastOrder)
            .ThenBy(customer => customer.PhoneNumber, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProductCatalogItem>> GetActiveProductCatalogAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.Id,
                   p.StoreId,
                   p.Name,
                   NULL AS Description,
                   p.NormalizedName,
                   p.Type,
                   p.Brand,
                   p.RetailPriceCents,
                   p.PromotionalPriceCents,
                   p.WholesalePriceCents,
                   p.IsActive,
                   a.Alias,
                   a.NormalizedAlias
            FROM Products p
            LEFT JOIN ProductAliases a ON a.ProductId = p.Id
            WHERE p.StoreId = @storeId
              AND p.IsActive = 1
            ORDER BY p.Name, a.Alias;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var products = new Dictionary<string, ProductCatalogBuilder>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var productId = reader.GetString(0);
            if (!products.TryGetValue(productId, out var product))
            {
                product = new ProductCatalogBuilder(
                    productId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetInt32(10) == 1);
                products.Add(productId, product);
            }

            if (!reader.IsDBNull(11) && !reader.IsDBNull(12))
            {
                product.Aliases.Add(new ProductAliasCatalogItem(reader.GetString(11), reader.GetString(12)));
            }
        }

        return products.Values
            .Select(product => new ProductCatalogItem(
                product.Id,
                product.StoreId,
                product.Name,
                product.Description,
                product.NormalizedName,
                product.Type,
                product.Brand,
                product.RetailPriceCents,
                product.PromotionalPriceCents,
                product.WholesalePriceCents,
                product.Aliases,
                product.IsActive))
            .ToArray();
    }

    public async Task<IReadOnlySet<string>> GetExistingOrderSourceMessageIdsAsync(
        string storeId,
        IReadOnlyList<string> sourceMessageIds,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        if (sourceMessageIds.Count == 0)
        {
            return existing;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var sourceMessageId in sourceMessageIds.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT SourceMessageId
                FROM Orders
                WHERE StoreId = @storeId
                  AND SourceMessageId = @sourceMessageId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("@storeId", storeId.Trim());
            command.Parameters.AddWithValue("@sourceMessageId", sourceMessageId.Trim());

            var result = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (result is not null)
            {
                existing.Add(result);
            }
        }

        return existing;
    }

    public async Task<OrderRegistrationResult> SaveHistoricalOrderAsync(
        HistoricalOrderRegistrationData order,
        CancellationToken cancellationToken)
    {
        var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(order.PhoneNumber);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Id, Status, TotalCents
            FROM Orders
            WHERE StoreId = @storeId
              AND SourceMessageId = @sourceMessageId
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("@storeId", order.StoreId);
        selectCommand.Parameters.AddWithValue("@sourceMessageId", order.SourceMessageId);

        OrderRegistrationResult? existingResult = null;
        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existingResult = new OrderRegistrationResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    AlreadyExisted: true);
            }
        }

        if (existingResult is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingResult;
        }

        var insertOrderCommand = connection.CreateCommand();
        insertOrderCommand.Transaction = transaction;
        insertOrderCommand.CommandText =
            """
            INSERT INTO Orders
                (Id, StoreId, PhoneNumber, SourceMessageId, PromptResponseId, ConversationId, SaleType, Status,
                 CustomerMessage, AiResponseText, AiOutputJson, GeneralObservation, TotalCents, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, @phoneNumber, @sourceMessageId, NULL, NULL, @saleType, @status,
                 @customerMessage, @aiResponseText, @aiOutputJson, @generalObservation, @totalCents, @createdAtUtc, @createdAtUtc);
            """;
        insertOrderCommand.Parameters.AddWithValue("@id", order.Id);
        insertOrderCommand.Parameters.AddWithValue("@storeId", order.StoreId);
        insertOrderCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);
        insertOrderCommand.Parameters.AddWithValue("@sourceMessageId", order.SourceMessageId);
        insertOrderCommand.Parameters.AddWithValue("@saleType", (object?)order.SaleType ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@status", OrderStatuses.Concluido);
        insertOrderCommand.Parameters.AddWithValue("@customerMessage", (object?)order.CustomerMessage ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@aiResponseText", order.AiResponseText);
        insertOrderCommand.Parameters.AddWithValue("@aiOutputJson", order.AiOutputJson);
        insertOrderCommand.Parameters.AddWithValue("@generalObservation", (object?)order.GeneralObservation ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@totalCents", order.TotalCents);
        insertOrderCommand.Parameters.AddWithValue("@createdAtUtc", order.CreatedAtUtc);
        await insertOrderCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var insertItemCommand = connection.CreateCommand();
            insertItemCommand.Transaction = transaction;
            insertItemCommand.CommandText =
                """
                INSERT INTO OrderItems
                    (Id, OrderId, ProductId, RequestedProductName, ProductNameSnapshot, Quantity,
                     UnitPriceCents, TotalPriceCents, Observation, MatchStatus, CreatedAtUtc)
                VALUES
                    (@id, @orderId, @productId, @requestedProductName, @productNameSnapshot, @quantity,
                     @unitPriceCents, @totalPriceCents, @observation, @matchStatus, @createdAtUtc);
                """;
            insertItemCommand.Parameters.AddWithValue("@id", item.Id);
            insertItemCommand.Parameters.AddWithValue("@orderId", item.OrderId);
            insertItemCommand.Parameters.AddWithValue("@productId", (object?)item.ProductId ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@requestedProductName", item.RequestedProductName);
            insertItemCommand.Parameters.AddWithValue("@productNameSnapshot", (object?)item.ProductNameSnapshot ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@quantity", item.Quantity);
            insertItemCommand.Parameters.AddWithValue("@unitPriceCents", (object?)item.UnitPriceCents ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@totalPriceCents", (object?)item.TotalPriceCents ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@observation", (object?)item.Observation ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@matchStatus", item.MatchStatus);
            insertItemCommand.Parameters.AddWithValue("@createdAtUtc", order.CreatedAtUtc);
            await insertItemCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new OrderRegistrationResult(order.Id, OrderStatuses.Concluido, order.TotalCents, AlreadyExisted: false);
    }

    public async Task<OrderRegistrationResult> SaveOrderAsync(
        OrderRegistrationData order,
        CancellationToken cancellationToken)
    {
        var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(order.PhoneNumber);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Id, Status, TotalCents
            FROM Orders
            WHERE StoreId = @storeId
              AND SourceMessageId = @sourceMessageId
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("@storeId", order.StoreId);
        selectCommand.Parameters.AddWithValue("@sourceMessageId", order.SourceMessageId);

        OrderRegistrationResult? existingResult = null;
        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existingResult = new OrderRegistrationResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    AlreadyExisted: true);
            }
        }

        if (existingResult is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingResult;
        }

        var now = DateTime.UtcNow.ToString("O");
        var insertOrderCommand = connection.CreateCommand();
        insertOrderCommand.Transaction = transaction;
        insertOrderCommand.CommandText =
            """
            INSERT INTO Orders
                (Id, StoreId, PhoneNumber, SourceMessageId, PromptResponseId, ConversationId, SaleType, Status,
                 CustomerMessage, AiResponseText, AiOutputJson, GeneralObservation, TotalCents, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, @phoneNumber, @sourceMessageId, @promptResponseId, @conversationId, @saleType, @status,
                 @customerMessage, @aiResponseText, @aiOutputJson, @generalObservation, @totalCents, @updatedAtUtc, @updatedAtUtc);
            """;
        insertOrderCommand.Parameters.AddWithValue("@id", order.Id);
        insertOrderCommand.Parameters.AddWithValue("@storeId", order.StoreId);
        insertOrderCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);
        insertOrderCommand.Parameters.AddWithValue("@sourceMessageId", order.SourceMessageId);
        insertOrderCommand.Parameters.AddWithValue("@promptResponseId", (object?)order.PromptResponseId ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@conversationId", (object?)order.ConversationId ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@saleType", (object?)order.SaleType ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@status", order.Status);
        insertOrderCommand.Parameters.AddWithValue("@customerMessage", (object?)order.CustomerMessage ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@aiResponseText", order.AiResponseText);
        insertOrderCommand.Parameters.AddWithValue("@aiOutputJson", order.AiOutputJson);
        insertOrderCommand.Parameters.AddWithValue("@generalObservation", (object?)order.GeneralObservation ?? DBNull.Value);
        insertOrderCommand.Parameters.AddWithValue("@totalCents", order.TotalCents);
        insertOrderCommand.Parameters.AddWithValue("@updatedAtUtc", now);
        await insertOrderCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var insertItemCommand = connection.CreateCommand();
            insertItemCommand.Transaction = transaction;
            insertItemCommand.CommandText =
                """
                INSERT INTO OrderItems
                    (Id, OrderId, ProductId, RequestedProductName, ProductNameSnapshot, Quantity,
                     UnitPriceCents, TotalPriceCents, Observation, MatchStatus, CreatedAtUtc)
                VALUES
                    (@id, @orderId, @productId, @requestedProductName, @productNameSnapshot, @quantity,
                     @unitPriceCents, @totalPriceCents, @observation, @matchStatus, @createdAtUtc);
                """;
            insertItemCommand.Parameters.AddWithValue("@id", item.Id);
            insertItemCommand.Parameters.AddWithValue("@orderId", item.OrderId);
            insertItemCommand.Parameters.AddWithValue("@productId", (object?)item.ProductId ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@requestedProductName", item.RequestedProductName);
            insertItemCommand.Parameters.AddWithValue("@productNameSnapshot", (object?)item.ProductNameSnapshot ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@quantity", item.Quantity);
            insertItemCommand.Parameters.AddWithValue("@unitPriceCents", (object?)item.UnitPriceCents ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@totalPriceCents", (object?)item.TotalPriceCents ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@observation", (object?)item.Observation ?? DBNull.Value);
            insertItemCommand.Parameters.AddWithValue("@matchStatus", item.MatchStatus);
            insertItemCommand.Parameters.AddWithValue("@createdAtUtc", now);
            await insertItemCommand.ExecuteNonQueryAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(item.ProductId) && item.Quantity > 0)
            {
                var stockCommand = connection.CreateCommand();
                stockCommand.Transaction = transaction;
                stockCommand.CommandText =
                    """
                    UPDATE Products
                    SET StockQuantity = CASE
                            WHEN StockQuantity IS NULL THEN NULL
                            WHEN StockQuantity <= @quantity THEN 0
                            ELSE StockQuantity - @quantity
                        END,
                        UpdatedAtUtc = @updatedAtUtc
                    WHERE StoreId = @storeId
                      AND Id = @productId
                      AND StockQuantity IS NOT NULL;
                    """;
                stockCommand.Parameters.AddWithValue("@storeId", order.StoreId);
                stockCommand.Parameters.AddWithValue("@productId", item.ProductId);
                stockCommand.Parameters.AddWithValue("@quantity", item.Quantity);
                stockCommand.Parameters.AddWithValue("@updatedAtUtc", now);
                await stockCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new OrderRegistrationResult(order.Id, order.Status, order.TotalCents, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<ActiveOrderData>> GetActiveOrdersAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "o.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT o.Id,
                   o.Status,
                   o.SaleType,
                   o.TotalCents,
                   o.CreatedAtUtc,
                   o.UpdatedAtUtc,
                   oi.RequestedProductName,
                   oi.ProductNameSnapshot,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   oi.MatchStatus
            FROM Orders o
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            WHERE o.StoreId = @storeId
              AND {phoneFilter}
              AND o.Status IN (@emProducao, @emRotaEntrega)
            ORDER BY o.CreatedAtUtc DESC, oi.CreatedAtUtc, oi.Id;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@emProducao", OrderStatuses.EmProducao);
        command.Parameters.AddWithValue("@emRotaEntrega", OrderStatuses.EmRotaEntrega);

        return await ReadOrderDataAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveOrderData>> GetRecentOrdersAsync(
        string storeId,
        string phoneNumber,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "o.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            WITH RecentOrders AS (
                SELECT o.Id
                FROM Orders o
                WHERE o.StoreId = @storeId
                  AND {phoneFilter}
                ORDER BY o.CreatedAtUtc DESC, o.Id DESC
                LIMIT @limit
            )
            SELECT o.Id,
                   o.Status,
                   o.SaleType,
                   o.TotalCents,
                   o.CreatedAtUtc,
                   o.UpdatedAtUtc,
                   oi.RequestedProductName,
                   oi.ProductNameSnapshot,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   oi.MatchStatus
            FROM Orders o
            INNER JOIN RecentOrders ro ON ro.Id = o.Id
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            ORDER BY o.CreatedAtUtc DESC, o.Id DESC, oi.CreatedAtUtc, oi.Id;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        return await ReadOrderDataAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerPurchasedItemData>> GetCustomerPurchasedItemsAsync(
        string storeId,
        string phoneNumber,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "o.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT ProductName,
                   MIN(ProductId) AS ProductId,
                   COALESCE(SUM(Quantity), 0) AS TotalQuantity,
                   COUNT(DISTINCT OrderId) AS OrderCount,
                   COALESCE(SUM(COALESCE(TotalPriceCents, 0)), 0) AS TotalSpentCents,
                   MAX(UnitPriceCents) AS MaxUnitPriceCents,
                   MAX(CreatedAtUtc) AS LastPurchasedAtUtc
            FROM (
                SELECT o.Id AS OrderId,
                       o.CreatedAtUtc,
                       oi.ProductId,
                       COALESCE(NULLIF(oi.ProductNameSnapshot, ''), oi.RequestedProductName) AS ProductName,
                       oi.Quantity,
                       oi.UnitPriceCents,
                       oi.TotalPriceCents
                FROM Orders o
                INNER JOIN OrderItems oi ON oi.OrderId = o.Id
                WHERE o.StoreId = @storeId
                  AND {phoneFilter}
                  AND o.Status = @status
            ) purchased
            WHERE ProductName IS NOT NULL
              AND TRIM(ProductName) <> ''
            GROUP BY ProductName
            ORDER BY TotalQuantity DESC, TotalSpentCents DESC, LastPurchasedAtUtc DESC, ProductName
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 200));

        var items = new List<CustomerPurchasedItemData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CustomerPurchasedItemData(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                Convert.ToInt32(reader.GetInt64(2)),
                Convert.ToInt32(reader.GetInt64(3)),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<CustomerHistoricalOrderItemData?> GetCustomerMostExpensivePurchasedItemAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "o.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT COALESCE(NULLIF(oi.ProductNameSnapshot, ''), oi.RequestedProductName) AS ProductName,
                   oi.ProductId,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   o.CreatedAtUtc
            FROM Orders o
            INNER JOIN OrderItems oi ON oi.OrderId = o.Id
            WHERE o.StoreId = @storeId
              AND {phoneFilter}
              AND o.Status = @status
              AND TRIM(COALESCE(NULLIF(oi.ProductNameSnapshot, ''), oi.RequestedProductName)) <> ''
            ORDER BY COALESCE(
                         oi.UnitPriceCents,
                         CASE
                             WHEN oi.Quantity > 0 AND oi.TotalPriceCents IS NOT NULL THEN oi.TotalPriceCents / oi.Quantity
                             ELSE NULL
                         END,
                         0
                     ) DESC,
                     COALESCE(oi.TotalPriceCents, 0) DESC,
                     o.CreatedAtUtc DESC,
                     oi.Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CustomerHistoricalOrderItemData(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6));
    }

    public async Task<ActiveOrderData?> GetLastCompletedOrderAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "o.PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            WITH RecentOrders AS (
                SELECT o.Id
                FROM Orders o
                WHERE o.StoreId = @storeId
                  AND {phoneFilter}
                  AND o.Status = @status
                ORDER BY o.CreatedAtUtc DESC, o.Id DESC
                LIMIT 1
            )
            SELECT o.Id,
                   o.Status,
                   o.SaleType,
                   o.TotalCents,
                   o.CreatedAtUtc,
                   o.UpdatedAtUtc,
                   oi.RequestedProductName,
                   oi.ProductNameSnapshot,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   oi.MatchStatus
            FROM Orders o
            INNER JOIN RecentOrders ro ON ro.Id = o.Id
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            ORDER BY o.CreatedAtUtc DESC, o.Id DESC, oi.CreatedAtUtc, oi.Id;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);

        var orders = await ReadOrderDataAsync(command, cancellationToken);
        return orders.FirstOrDefault();
    }

    public async Task<int> CountCustomerCompletedOrdersAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT COUNT(1)
            FROM Orders
            WHERE StoreId = @storeId
              AND {phoneFilter}
              AND Status = @status;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@status", OrderStatuses.Concluido);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task SavePendingCustomerActionAsync(
        PendingCustomerAction action,
        CancellationToken cancellationToken)
    {
        var normalizedPhoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(action.PhoneNumber);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var cancelCommand = connection.CreateCommand();
        cancelCommand.Transaction = transaction;
        var phoneFilter = AddPhoneStorageCandidateParameters(cancelCommand, "PhoneNumber", "phone", normalizedPhoneNumber);
        cancelCommand.CommandText =
            $"""
            UPDATE WhatsappPendingCustomerActions
            SET Status = @cancelledStatus,
                UpdatedAtUtc = @updatedAtUtc
            WHERE StoreId = @storeId
              AND {phoneFilter}
              AND ActionType = @actionType
              AND Status = @activeStatus;
            """;
        cancelCommand.Parameters.AddWithValue("@storeId", action.StoreId.Trim());
        cancelCommand.Parameters.AddWithValue("@actionType", action.ActionType);
        cancelCommand.Parameters.AddWithValue("@activeStatus", PendingCustomerActionStatuses.Active);
        cancelCommand.Parameters.AddWithValue("@cancelledStatus", PendingCustomerActionStatuses.Cancelled);
        cancelCommand.Parameters.AddWithValue("@updatedAtUtc", action.CreatedAtUtc);
        await cancelCommand.ExecuteNonQueryAsync(cancellationToken);

        var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO WhatsappPendingCustomerActions
                (Id, StoreId, PhoneNumber, ActionType, PayloadJson, Status, CreatedAtUtc, ExpiresAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, @phoneNumber, @actionType, @payloadJson, @status, @createdAtUtc, @expiresAtUtc, @createdAtUtc);
            """;
        insertCommand.Parameters.AddWithValue("@id", action.Id);
        insertCommand.Parameters.AddWithValue("@storeId", action.StoreId.Trim());
        insertCommand.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        insertCommand.Parameters.AddWithValue("@actionType", action.ActionType);
        insertCommand.Parameters.AddWithValue("@payloadJson", action.PayloadJson);
        insertCommand.Parameters.AddWithValue("@status", action.Status);
        insertCommand.Parameters.AddWithValue("@createdAtUtc", action.CreatedAtUtc);
        insertCommand.Parameters.AddWithValue("@expiresAtUtc", action.ExpiresAtUtc);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PendingCustomerAction?> GetOpenPendingCustomerActionAsync(
        string storeId,
        string phoneNumber,
        string actionType,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var phoneFilter = AddPhoneStorageCandidateParameters(command, "PhoneNumber", "phone", phoneNumber);
        command.CommandText =
            $"""
            SELECT Id,
                   StoreId,
                   PhoneNumber,
                   ActionType,
                   PayloadJson,
                   Status,
                   CreatedAtUtc,
                   ExpiresAtUtc
            FROM WhatsappPendingCustomerActions
            WHERE StoreId = @storeId
              AND {phoneFilter}
              AND ActionType = @actionType
              AND Status = @status
            ORDER BY CreatedAtUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@actionType", actionType);
        command.Parameters.AddWithValue("@status", PendingCustomerActionStatuses.Active);

        PendingCustomerAction? action = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                action = new PendingCustomerAction(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7));
            }
        }

        if (action is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                action.ExpiresAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt) &&
            expiresAt <= DateTimeOffset.UtcNow)
        {
            await UpdatePendingCustomerActionStatusAsync(
                connection,
                action.Id,
                PendingCustomerActionStatuses.Expired,
                cancellationToken);
            return null;
        }

        return action;
    }

    public async Task CompletePendingCustomerActionAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await UpdatePendingCustomerActionStatusAsync(
            connection,
            id,
            PendingCustomerActionStatuses.Completed,
            cancellationToken);
    }

    public async Task CancelPendingCustomerActionAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await UpdatePendingCustomerActionStatusAsync(
            connection,
            id,
            PendingCustomerActionStatuses.Cancelled,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<ActiveOrderData>> ReadOrderDataAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var orders = new Dictionary<string, ActiveOrderBuilder>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = reader.GetString(0);
            if (!orders.TryGetValue(orderId, out var order))
            {
                order = new ActiveOrderBuilder(
                    orderId,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetString(5));
                orders.Add(orderId, order);
            }

            if (!reader.IsDBNull(6))
            {
                order.Items.Add(new ActiveOrderItemData(
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetString(12)));
            }
        }

        return orders.Values
            .Select(order => new ActiveOrderData(
                order.Id,
                order.Status,
                order.SaleType,
                order.TotalCents,
                order.CreatedAtUtc,
                order.UpdatedAtUtc,
                order.Items))
            .ToArray();
    }

    public async Task<IReadOnlyList<OrderManagementCustomerResponse>> GetManagedOrdersAsync(
        string storeId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT o.Id,
                   o.PhoneNumber,
                   o.Status,
                   o.SaleType,
                   o.TotalCents,
                   o.GeneralObservation,
                   o.CreatedAtUtc,
                   o.UpdatedAtUtc,
                   oi.RequestedProductName,
                   oi.ProductNameSnapshot,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   oi.MatchStatus
            FROM Orders o
            LEFT JOIN OrderItems oi ON oi.OrderId = o.Id
            WHERE o.StoreId = @storeId
              AND (@status IS NULL OR o.Status = @status)
            ORDER BY o.PhoneNumber, o.CreatedAtUtc DESC, oi.CreatedAtUtc, oi.Id;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        AddNullableTextParameter(command, "@status", status);

        var customers = new Dictionary<string, OrderManagementCustomerBuilder>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rawPhoneNumber = reader.GetString(1);
            var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(rawPhoneNumber);
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                phoneNumber = rawPhoneNumber.Trim();
            }
            if (!customers.TryGetValue(phoneNumber, out var customer))
            {
                customer = new OrderManagementCustomerBuilder(phoneNumber);
                customers.Add(phoneNumber, customer);
            }

            var orderId = reader.GetString(0);
            if (!customer.Orders.TryGetValue(orderId, out var order))
            {
                order = new OrderManagementOrderBuilder(
                    orderId,
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7));

                customer.Orders.Add(orderId, order);
            }

            if (!reader.IsDBNull(8))
            {
                order.Items.Add(new OrderManagementOrderItemResponse(
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    reader.IsDBNull(12) ? null : reader.GetInt64(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetString(14)));
            }
        }

        return customers.Values
            .Select(customer =>
            {
                var orders = customer.Orders.Values
                    .OrderByDescending(order => order.CreatedAtUtc, StringComparer.Ordinal)
                    .Select(order => new OrderManagementOrderResponse(
                        order.Id,
                        order.Status,
                        order.SaleType,
                        order.TotalCents,
                        order.GeneralObservation,
                        order.CreatedAtUtc,
                        order.UpdatedAtUtc,
                        order.Items))
                    .ToArray();

                return new OrderManagementCustomerResponse(
                    customer.PhoneNumber,
                    orders.Length,
                    orders.Count(order => order.Status != OrderStatuses.Concluido),
                    orders.FirstOrDefault()?.CreatedAtUtc ?? string.Empty,
                    orders);
            })
            .OrderByDescending(customer => customer.LastOrderAtUtc, StringComparer.Ordinal)
            .ThenBy(customer => customer.PhoneNumber, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<bool> UpdateOrderStatusAsync(
        string storeId,
        string orderId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var updatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Orders
            SET Status = @status,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @id
              AND StoreId = @storeId;
            """;
        command.Parameters.AddWithValue("@id", orderId);
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@updatedAtUtc", updatedAtUtc);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (updated && status == OrderStatuses.Concluido)
        {
            await ScheduleOrderFeedbackSolicitationAsync(
                connection,
                storeId,
                orderId,
                DateTimeOffset.Parse(updatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                cancellationToken);
        }

        return updated;
    }

    public async Task<string?> GetPromptIdAsync(string storeId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PromptId
            FROM StorePrompts
            WHERE StoreId = @storeId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task<WhatsappConversationState?> GetConversationAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ConversationId, LastResponseId
            FROM WhatsappConversations
            WHERE StoreId = @storeId
              AND PhoneNumber = @phoneNumber
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var conversationId = reader.IsDBNull(0) ? null : reader.GetString(0);
        var lastResponseId = reader.IsDBNull(1) ? null : reader.GetString(1);
        return new WhatsappConversationState(conversationId, lastResponseId);
    }

    public async Task UpsertStorePromptAsync(string storeId, string promptId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO StorePrompts (StoreId, PromptId, UpdatedAtUtc)
            VALUES (@storeId, @promptId, @updatedAtUtc)
            ON CONFLICT(StoreId) DO UPDATE SET
                PromptId = excluded.PromptId,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@promptId", promptId);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertConversationAsync(
        string storeId,
        string phoneNumber,
        string? conversationId,
        string? lastResponseId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WhatsappConversations (StoreId, PhoneNumber, ConversationId, LastResponseId, UpdatedAtUtc)
            VALUES (@storeId, @phoneNumber, @conversationId, @lastResponseId, @updatedAtUtc)
            ON CONFLICT(StoreId, PhoneNumber) DO UPDATE SET
                ConversationId = excluded.ConversationId,
                LastResponseId = excluded.LastResponseId,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber);
        command.Parameters.AddWithValue("@conversationId", (object?)conversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastResponseId", (object?)lastResponseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsappConversationSummaryResponse>> ListWhatsappConversationsAsync(
        string storeId,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH Phones AS (
                SELECT PhoneNumber, COUNT(*) AS MessageCount
                FROM WhatsappConversationMessages
                WHERE StoreId = @storeId
                GROUP BY PhoneNumber
            )
            SELECT p.PhoneNumber,
                   COALESCE(s.IsAgentEnabled, 1) AS IsAgentEnabled,
                   m.Body,
                   m.Direction,
                   m.MessageType,
                   m.Status,
                   m.CreatedAtUtc,
                   p.MessageCount
            FROM Phones p
            INNER JOIN WhatsappConversationMessages m ON m.Id = (
                SELECT innerMessage.Id
                FROM WhatsappConversationMessages innerMessage
                WHERE innerMessage.StoreId = @storeId
                  AND innerMessage.PhoneNumber = p.PhoneNumber
                ORDER BY innerMessage.CreatedAtUtc DESC, innerMessage.Id DESC
                LIMIT 1
            )
            LEFT JOIN WhatsappContactSettings s ON s.StoreId = @storeId
                AND s.PhoneNumber = p.PhoneNumber
            ORDER BY m.CreatedAtUtc DESC, m.Id DESC;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);

        var conversationRows = new List<WhatsappConversationSummaryBuilder>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                conversationRows.Add(new WhatsappConversationSummaryBuilder(
                    reader.GetString(0),
                    reader.GetInt32(1) == 1,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    Convert.ToInt32(reader.GetInt64(7))));
            }
        }

        var customers = await ReadWhatsappConversationCustomerLookupsAsync(
            connection,
            normalizedStoreId,
            cancellationToken);

        return conversationRows
            .Select(row =>
            {
                var customer = FindWhatsappConversationCustomer(customers, row.PhoneNumber);
                return new WhatsappConversationSummaryResponse(
                    row.PhoneNumber,
                    customer?.Id,
                    customer?.Name,
                    row.IsAgentEnabled,
                    row.LastMessage,
                    row.LastMessageDirection,
                    row.LastMessageType,
                    row.LastMessageStatus,
                    row.LastMessageAtUtc,
                    row.MessageCount);
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<WhatsappConversationMessageResponse>> GetWhatsappConversationMessagesAsync(
        string storeId,
        string phoneNumber,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedStoreId = storeId.Trim();
        var normalizedPhoneNumber = phoneNumber.Trim();
        var normalizedLimit = Math.Clamp(limit, 1, 500);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id,
                   PhoneNumber,
                   Direction,
                   MessageType,
                   Body,
                   TwilioMessageSid,
                   SourceJobId,
                   Status,
                   Error,
                   CreatedAtUtc
            FROM (
                SELECT Id,
                       PhoneNumber,
                       Direction,
                       MessageType,
                       Body,
                       TwilioMessageSid,
                       SourceJobId,
                       Status,
                       Error,
                       CreatedAtUtc
                FROM WhatsappConversationMessages
                WHERE StoreId = @storeId
                  AND PhoneNumber = @phoneNumber
                ORDER BY CreatedAtUtc DESC, Id DESC
                LIMIT @limit
            )
            ORDER BY CreatedAtUtc ASC, Id ASC;
            """;
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);
        command.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        command.Parameters.AddWithValue("@limit", normalizedLimit);

        var messages = new List<WhatsappConversationMessageResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadWhatsappConversationMessage(reader));
        }

        return messages;
    }

    public async Task<bool> IsWhatsappAgentEnabledAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT IsAgentEnabled
            FROM WhatsappContactSettings
            WHERE StoreId = @storeId
              AND PhoneNumber = @phoneNumber
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber.Trim());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    public async Task SetWhatsappAgentEnabledAsync(
        string storeId,
        string phoneNumber,
        bool isAgentEnabled,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WhatsappContactSettings (StoreId, PhoneNumber, IsAgentEnabled, UpdatedAtUtc)
            VALUES (@storeId, @phoneNumber, @isAgentEnabled, @updatedAtUtc)
            ON CONFLICT(StoreId, PhoneNumber) DO UPDATE SET
                IsAgentEnabled = excluded.IsAgentEnabled,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId.Trim());
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber.Trim());
        command.Parameters.AddWithValue("@isAgentEnabled", isAgentEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WhatsappConversationMessageResponse> RecordWhatsappConversationMessageAsync(
        string storeId,
        string phoneNumber,
        string direction,
        string messageType,
        string body,
        string? twilioMessageSid,
        string? sourceJobId,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!WhatsappConversationMessageDirections.IsKnown(direction))
        {
            throw new ArgumentException("Unknown WhatsApp message direction.", nameof(direction));
        }

        if (!WhatsappConversationMessageTypes.IsKnown(messageType))
        {
            throw new ArgumentException("Unknown WhatsApp message type.", nameof(messageType));
        }

        var normalizedStoreId = storeId.Trim();
        var normalizedPhoneNumber = phoneNumber.Trim();
        var normalizedBody = body.Trim();
        var now = DateTime.UtcNow.ToString("O");
        var id = Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WhatsappConversationMessages
                (Id, StoreId, PhoneNumber, Direction, MessageType, Body, TwilioMessageSid, SourceJobId, Status, Error, CreatedAtUtc)
            VALUES
                (@id, @storeId, @phoneNumber, @direction, @messageType, @body, @twilioMessageSid, @sourceJobId, @status, @error, @createdAtUtc);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@storeId", normalizedStoreId);
        command.Parameters.AddWithValue("@phoneNumber", normalizedPhoneNumber);
        command.Parameters.AddWithValue("@direction", direction.Trim());
        command.Parameters.AddWithValue("@messageType", messageType.Trim());
        command.Parameters.AddWithValue("@body", normalizedBody);
        command.Parameters.AddWithValue("@twilioMessageSid", (object?)NormalizeOptionalText(twilioMessageSid) ?? DBNull.Value);
        command.Parameters.AddWithValue("@sourceJobId", (object?)NormalizeOptionalText(sourceJobId) ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", status.Trim());
        command.Parameters.AddWithValue("@error", (object?)NormalizeOptionalText(error) ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAtUtc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new WhatsappConversationMessageResponse(
            id,
            normalizedPhoneNumber,
            direction.Trim(),
            messageType.Trim(),
            normalizedBody,
            NormalizeOptionalText(twilioMessageSid),
            NormalizeOptionalText(sourceJobId),
            status.Trim(),
            NormalizeOptionalText(error),
            now);
    }

    public async Task ClearAllConversationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM WhatsappConversations;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetHomologationDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var commandText in new[]
        {
            "DELETE FROM AgentFeedbackResponses;",
            "DELETE FROM AgentFeedbackSolicitations;",
            "DELETE FROM AgentAutomatedCampaignDeliveries;",
            "DELETE FROM AgentAutomatedCampaignRuns;",
            "DELETE FROM AgentAutomatedCampaigns;",
            "DELETE FROM WhatsappMessageJobs;",
            "DELETE FROM WhatsappConversationMessages;",
            "DELETE FROM WhatsappContactSettings;",
            "DELETE FROM WhatsappConversations;",
            "DELETE FROM OrderItems;",
            "DELETE FROM Orders;",
            "DELETE FROM ProductAliases;",
            "DELETE FROM Products;",
            "DELETE FROM Clientes;"
        })
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearConversationAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM WhatsappConversations
            WHERE StoreId = @storeId
              AND PhoneNumber = @phoneNumber;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearWhatsappConversationHistoryAsync(
        string storeId,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();

        var commands = new[]
        {
            "DELETE FROM WhatsappMessageJobs WHERE StoreId = @storeId AND PhoneNumber = @phoneNumber;",
            "DELETE FROM WhatsappConversationMessages WHERE StoreId = @storeId AND PhoneNumber = @phoneNumber;",
            "DELETE FROM WhatsappConversations WHERE StoreId = @storeId AND PhoneNumber = @phoneNumber;"
        };

        foreach (var commandText in commands)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.Parameters.AddWithValue("@storeId", storeId.Trim());
            command.Parameters.AddWithValue("@phoneNumber", phoneNumber.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> EnqueueWhatsappMessageJobAsync(
        string id,
        string storeId,
        string phoneNumber,
        string message,
        string? feedbackSolicitationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WhatsappMessageJobs
                (Id, StoreId, PhoneNumber, Message, FeedbackSolicitationId, Status, Attempts, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, @phoneNumber, @message, @feedbackSolicitationId, 'Pending', 0, @updatedAtUtc, @updatedAtUtc)
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@phoneNumber", phoneNumber);
        command.Parameters.AddWithValue("@message", message);
        command.Parameters.AddWithValue("@feedbackSolicitationId", (object?)feedbackSolicitationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task ResetStaleWhatsappMessageJobsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WhatsappMessageJobs
            SET Status = 'Pending',
                UpdatedAtUtc = @updatedAtUtc
            WHERE Status = 'Processing';
            """;
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WhatsappMessageJob?> TryClaimNextWhatsappMessageJobAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Id, Message, PhoneNumber, StoreId, Attempts, FeedbackSolicitationId
            FROM WhatsappMessageJobs
            WHERE Status = 'Pending'
            ORDER BY CreatedAtUtc
            LIMIT 1;
            """;

        var foundJob = false;
        var id = string.Empty;
        var message = string.Empty;
        var phoneNumber = string.Empty;
        var storeId = string.Empty;
        var attempts = 0;
        string? feedbackSolicitationId = null;

        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                foundJob = true;
                id = reader.GetString(0);
                message = reader.GetString(1);
                phoneNumber = reader.GetString(2);
                storeId = reader.GetString(3);
                attempts = reader.GetInt32(4) + 1;
                feedbackSolicitationId = reader.IsDBNull(5) ? null : reader.GetString(5);
            }
        }

        if (!foundJob)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE WhatsappMessageJobs
            SET Status = 'Processing',
                Attempts = @attempts,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @id
              AND Status = 'Pending';
            """;
        updateCommand.Parameters.AddWithValue("@id", id);
        updateCommand.Parameters.AddWithValue("@attempts", attempts);
        updateCommand.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        var rowsUpdated = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return rowsUpdated == 0
            ? null
            : new WhatsappMessageJob(id, message, phoneNumber, storeId, attempts, feedbackSolicitationId);
    }

    public async Task CompleteWhatsappMessageJobAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WhatsappMessageJobs
            SET Status = 'Completed',
                LastError = NULL,
                UpdatedAtUtc = @updatedAtUtc,
                CompletedAtUtc = @updatedAtUtc
            WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@updatedAtUtc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailWhatsappMessageJobAsync(
        string id,
        string error,
        bool shouldRetry,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WhatsappMessageJobs
            SET Status = @status,
                LastError = @lastError,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", shouldRetry ? "Pending" : "Failed");
        command.Parameters.AddWithValue("@lastError", error);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CompanyLoginResponse> SyncCompanyAsync(
        string companyName,
        string companyPhone,
        string? previousCompanyPhone,
        CancellationToken cancellationToken)
    {
        var normalizedCompanyName = companyName.Trim();
        var normalizedCompanyPhone = companyPhone.Trim();
        var normalizedPreviousPhone = string.IsNullOrWhiteSpace(previousCompanyPhone)
            ? null
            : previousCompanyPhone.Trim();

        if (string.IsNullOrWhiteSpace(normalizedCompanyName) ||
            string.IsNullOrWhiteSpace(normalizedCompanyPhone))
        {
            throw new InvalidOperationException("Company name and phone are required.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var currentCompanyId = await FindCompanyIdByPhoneAsync(
            connection,
            transaction,
            normalizedCompanyPhone,
            cancellationToken);
        var previousCompanyId = normalizedPreviousPhone is null ||
            string.Equals(normalizedPreviousPhone, normalizedCompanyPhone, StringComparison.Ordinal)
                ? null
                : await FindCompanyIdByPhoneAsync(connection, transaction, normalizedPreviousPhone, cancellationToken);

        if (currentCompanyId is not null &&
            previousCompanyId is not null &&
            !string.Equals(currentCompanyId, previousCompanyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The requested WhatsApp phone is already linked to another company.");
        }

        var companyId = currentCompanyId ?? previousCompanyId ?? Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("O");

        if (currentCompanyId is null && previousCompanyId is null)
        {
            await InsertCompanyAsync(
                connection,
                transaction,
                companyId,
                normalizedCompanyName,
                normalizedCompanyPhone,
                CreateHiddenPassword(),
                now,
                cancellationToken);
        }
        else
        {
            await UpdateCompanyAsync(
                connection,
                transaction,
                companyId,
                normalizedCompanyName,
                normalizedCompanyPhone,
                now,
                cancellationToken);
        }

        if (normalizedPreviousPhone is not null &&
            !string.Equals(normalizedPreviousPhone, normalizedCompanyPhone, StringComparison.Ordinal))
        {
            await MigrateStoreIdAsync(
                connection,
                transaction,
                normalizedPreviousPhone,
                normalizedCompanyPhone,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CompanyLoginResponse(
            companyId,
            normalizedCompanyName,
            normalizedCompanyPhone,
            normalizedCompanyPhone);
    }

    private async Task SeedCompanyIfNeededAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var company in GetConfiguredSeedCompanies())
        {
            await InsertCompanyIfMissingAsync(connection, company, cancellationToken);
        }
    }

    private IEnumerable<SeedCompanyEntry> GetConfiguredSeedCompanies()
    {
        yield return new SeedCompanyEntry
        {
            CompanyName = _seedCompanyOptions.CompanyName,
            CompanyPhone = _seedCompanyOptions.CompanyPhone,
            InitialPassword = _seedCompanyOptions.InitialPassword
        };

        foreach (var company in _seedCompanyOptions.AdditionalCompanies)
        {
            yield return company;
        }
    }

    private static async Task InsertCompanyIfMissingAsync(
        NpgsqlConnection connection,
        SeedCompanyEntry company,
        CancellationToken cancellationToken)
    {
        var companyPhone = company.CompanyPhone.Trim();
        var companyName = company.CompanyName.Trim();
        var initialPassword = company.InitialPassword;
        if (string.IsNullOrWhiteSpace(companyPhone) ||
            string.IsNullOrWhiteSpace(companyName) ||
            string.IsNullOrWhiteSpace(initialPassword))
        {
            return;
        }

        var countCommand = connection.CreateCommand();
        countCommand.CommandText =
            """
            SELECT COUNT(1)
            FROM Companies
            WHERE Username = @username
               OR CompanyPhone = @companyPhone;
            """;
        countCommand.Parameters.AddWithValue("@username", companyPhone);
        countCommand.Parameters.AddWithValue("@companyPhone", companyPhone);
        var count = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        if (count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O");
        var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO Companies
                (Id, Username, PasswordHash, CompanyName, CompanyPhone, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @username, @passwordHash, @companyName, @companyPhone, @createdAtUtc, @updatedAtUtc);
            """;
        insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        insertCommand.Parameters.AddWithValue("@username", companyPhone);
        insertCommand.Parameters.AddWithValue("@passwordHash", HashPassword(initialPassword));
        insertCommand.Parameters.AddWithValue("@companyName", companyName);
        insertCommand.Parameters.AddWithValue("@companyPhone", companyPhone);
        insertCommand.Parameters.AddWithValue("@createdAtUtc", now);
        insertCommand.Parameters.AddWithValue("@updatedAtUtc", now);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> FindCompanyIdByPhoneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string phone,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id
            FROM Companies
            WHERE Username = @phone
               OR CompanyPhone = @phone
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@phone", phone);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task InsertCompanyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string companyId,
        string companyName,
        string companyPhone,
        string hiddenPassword,
        string now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Companies
                (Id, Username, PasswordHash, CompanyName, CompanyPhone, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @username, @passwordHash, @companyName, @companyPhone, @createdAtUtc, @updatedAtUtc);
            """;
        command.Parameters.AddWithValue("@id", companyId);
        command.Parameters.AddWithValue("@username", companyPhone);
        command.Parameters.AddWithValue("@passwordHash", HashPassword(hiddenPassword));
        command.Parameters.AddWithValue("@companyName", companyName);
        command.Parameters.AddWithValue("@companyPhone", companyPhone);
        command.Parameters.AddWithValue("@createdAtUtc", now);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateCompanyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string companyId,
        string companyName,
        string companyPhone,
        string now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Companies
            SET Username = @username,
                CompanyName = @companyName,
                CompanyPhone = @companyPhone,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", companyId);
        command.Parameters.AddWithValue("@username", companyPhone);
        command.Parameters.AddWithValue("@companyName", companyName);
        command.Parameters.AddWithValue("@companyPhone", companyPhone);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateStoreIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string oldStoreId,
        string newStoreId,
        CancellationToken cancellationToken)
    {
        foreach (var tableName in new[] { "StorePrompts", "Products", "Orders", "WhatsappConversations", "WhatsappMessageJobs", "AgentPersonaSettings", "AgentPersonaFaqs", "AgentAutomatedCampaigns", "AgentAutomatedCampaignRuns", "AgentAutomatedCampaignDeliveries", "AgentFeedbackSettings", "AgentFeedbackSolicitations", "AgentFeedbackResponses" })
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {tableName} SET StoreId = @newStoreId WHERE StoreId = @oldStoreId;";
            command.Parameters.AddWithValue("@newStoreId", newStoreId);
            command.Parameters.AddWithValue("@oldStoreId", oldStoreId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string CreateHiddenPassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static async Task<(int TotalOrders, long TotalSoldCents, long AverageTicketCents, int PendingReviewOrders)> GetDashboardTotalsAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1),
                   COALESCE(SUM(TotalCents), 0),
                   SUM(CASE WHEN Status = @pendingReview THEN 1 ELSE 0 END)
            FROM Orders
            WHERE StoreId = @storeId;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@pendingReview", OrderStatuses.PendingReview);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0, 0, 0);
        }

        var totalOrders = Convert.ToInt32(reader.GetInt64(0));
        var totalSoldCents = reader.GetInt64(1);
        var averageTicketCents = totalOrders == 0 ? 0 : totalSoldCents / totalOrders;
        var pendingReviewOrders = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetInt64(2));

        return (totalOrders, totalSoldCents, averageTicketCents, pendingReviewOrders);
    }

    private static async Task<IReadOnlyList<DashboardTopProductResponse>> GetDashboardTopProductsAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(NULLIF(oi.ProductNameSnapshot, ''), oi.RequestedProductName) AS ProductName,
                   COALESCE(SUM(oi.Quantity), 0) AS Quantity,
                   COALESCE(SUM(oi.TotalPriceCents), 0) AS TotalCents
            FROM OrderItems oi
            INNER JOIN Orders o ON o.Id = oi.OrderId
            WHERE o.StoreId = @storeId
            GROUP BY ProductName
            ORDER BY Quantity DESC, TotalCents DESC, ProductName
            LIMIT 5;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var products = new List<DashboardTopProductResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new DashboardTopProductResponse(
                reader.GetString(0),
                Convert.ToInt32(reader.GetInt64(1)),
                reader.GetInt64(2)));
        }

        return products;
    }

    private static async Task<int> CountLateOrdersAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM Orders
            WHERE StoreId = @storeId
              AND Status IN (@emProducao, @emRotaEntrega)
              AND UpdatedAtUtc < @cutoffUtc;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@emProducao", OrderStatuses.EmProducao);
        command.Parameters.AddWithValue("@emRotaEntrega", OrderStatuses.EmRotaEntrega);
        command.Parameters.AddWithValue("@cutoffUtc", DateTime.UtcNow.AddMinutes(-30).ToString("O"));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<IReadOnlyList<DashboardStatusCountResponse>> GetDashboardStatusCountsAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Status, COUNT(1)
            FROM Orders
            WHERE StoreId = @storeId
            GROUP BY Status;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var countsByStatus = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [OrderStatuses.PendingReview] = 0,
            [OrderStatuses.EmProducao] = 0,
            [OrderStatuses.EmRotaEntrega] = 0,
            [OrderStatuses.Concluido] = 0
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            countsByStatus[reader.GetString(0)] = Convert.ToInt32(reader.GetInt64(1));
        }

        return countsByStatus
            .Select(item => new DashboardStatusCountResponse(item.Key, item.Value))
            .ToArray();
    }

    private static async Task<IReadOnlyList<DashboardRecentOrderResponse>> GetDashboardRecentOrdersAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH RecentOrders AS (
                SELECT Id,
                       PhoneNumber,
                       Status,
                       SaleType,
                       TotalCents,
                       GeneralObservation,
                       CreatedAtUtc,
                       UpdatedAtUtc
                FROM Orders
                WHERE StoreId = @storeId
                ORDER BY CreatedAtUtc DESC
                LIMIT 10
            )
            SELECT ro.Id,
                   ro.PhoneNumber,
                   ro.Status,
                   ro.SaleType,
                   ro.TotalCents,
                   ro.GeneralObservation,
                   ro.CreatedAtUtc,
                   ro.UpdatedAtUtc,
                   CASE
                       WHEN ro.Status IN (@emProducao, @emRotaEntrega)
                        AND ro.UpdatedAtUtc < @cutoffUtc
                       THEN 1
                       ELSE 0
                   END AS IsLate,
                   oi.RequestedProductName,
                   oi.ProductNameSnapshot,
                   oi.Quantity,
                   oi.UnitPriceCents,
                   oi.TotalPriceCents,
                   oi.Observation,
                   oi.MatchStatus
            FROM RecentOrders ro
            LEFT JOIN OrderItems oi ON oi.OrderId = ro.Id
            ORDER BY ro.CreatedAtUtc DESC, oi.CreatedAtUtc, oi.Id;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@emProducao", OrderStatuses.EmProducao);
        command.Parameters.AddWithValue("@emRotaEntrega", OrderStatuses.EmRotaEntrega);
        command.Parameters.AddWithValue("@cutoffUtc", DateTime.UtcNow.AddMinutes(-30).ToString("O"));

        var orders = new Dictionary<string, DashboardRecentOrderBuilder>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = reader.GetString(0);
            if (!orders.TryGetValue(orderId, out var order))
            {
                order = new DashboardRecentOrderBuilder(
                    orderId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetInt32(8) == 1);
                orders.Add(orderId, order);
            }

            if (!reader.IsDBNull(9))
            {
                order.Items.Add(new DashboardRecentOrderItemResponse(
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetInt64(12),
                    reader.IsDBNull(13) ? null : reader.GetInt64(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.GetString(15)));
            }
        }

        return orders.Values
            .Select(order => new DashboardRecentOrderResponse(
                order.Id,
                order.PhoneNumber,
                order.Status,
                order.SaleType,
                order.TotalCents,
                order.GeneralObservation,
                order.CreatedAtUtc,
                order.UpdatedAtUtc,
                order.IsLate,
                order.Items))
            .ToArray();
    }

    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static long ToCents(decimal value)
    {
        return decimal.ToInt64(decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal FromCents(long value)
    {
        return value / 100m;
    }

    private static ProductResponse CreateProductResponse(ProductListBuilder product)
    {
        return new ProductResponse(
            product.Id,
            product.StoreId,
            product.Name,
            product.Description,
            product.Type,
            product.Brand,
            FromCents(product.RetailPriceCents),
            product.PromotionalPriceCents is null ? null : FromCents(product.PromotionalPriceCents.Value),
            FromCents(product.WholesalePriceCents),
            product.Aliases,
            product.StockQuantity,
            product.LowStockThreshold,
            product.IsActive);
    }

    private static int? ScoreProductSearchMatch(string normalizedQuery, string normalizedName, string? normalizedAlias)
    {
        var nameScore = ScoreNormalizedProductText(normalizedQuery, normalizedName);
        var aliasScore = string.IsNullOrWhiteSpace(normalizedAlias)
            ? null
            : ScoreNormalizedProductText(normalizedQuery, normalizedAlias) + 1;

        return nameScore is null
            ? aliasScore
            : aliasScore is null
                ? nameScore
                : Math.Min(nameScore.Value, aliasScore.Value);
    }

    private static int? ScoreNormalizedProductText(string normalizedQuery, string normalizedProductText)
    {
        if (string.Equals(normalizedQuery, normalizedProductText, StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalizedProductText.Length >= 4 &&
            normalizedQuery.Contains(normalizedProductText, StringComparison.Ordinal))
        {
            return 2;
        }

        if (normalizedQuery.Length >= 5 &&
            normalizedProductText.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 3;
        }

        var queryTerms = GetSearchTerms(normalizedQuery);
        if (queryTerms.Count == 0)
        {
            return null;
        }

        var productTerms = GetSearchTerms(normalizedProductText);
        var matchingTerms = queryTerms.Count(term => productTerms.Contains(term));
        if (matchingTerms >= 2)
        {
            return 4 + Math.Max(0, productTerms.Count - matchingTerms);
        }

        return matchingTerms == 1 && queryTerms.Any(term => term.Length >= 6 && productTerms.Contains(term))
            ? 8
            : null;
    }

    private static HashSet<string> GetSearchTerms(string normalizedText)
    {
        return normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 3 && !ProductSearchStopWords.Contains(term))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static CustomerResponse ReadCustomer(NpgsqlDataReader reader)
    {
        return new CustomerResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7));
    }

    private static string AddPhoneStorageCandidateParameters(
        NpgsqlCommand command,
        string columnName,
        string parameterPrefix,
        string phoneNumber)
    {
        var candidates = PhoneNumberNormalizer.GetStorageCandidates(phoneNumber);
        if (candidates.Count == 0)
        {
            candidates = new[] { string.Empty };
        }

        var parameterNames = new List<string>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var parameterName = $"@{parameterPrefix}{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, candidates[index]);
        }

        return $"{columnName} IN ({string.Join(", ", parameterNames)})";
    }

    private static async Task UpdatePendingCustomerActionStatusAsync(
        NpgsqlConnection connection,
        string id,
        string status,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WhatsappPendingCustomerActions
            SET Status = @status,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@updatedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<WhatsappConversationCustomerLookup>> ReadWhatsappConversationCustomerLookupsAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ID_Cliente,
                   CLIENTE_NOME,
                   CLIENTE_TELEFONE_CELULAR
            FROM Clientes
            WHERE StoreId = @storeId
            ORDER BY COALESCE(NULLIF(CLIENTE_NOME, ''), CLIENTE_TELEFONE_CELULAR), CLIENTE_TELEFONE_CELULAR;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var customers = new List<WhatsappConversationCustomerLookup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phoneNumber = reader.GetString(2);
            customers.Add(new WhatsappConversationCustomerLookup(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                phoneNumber,
                PhoneNumberNormalizer.GetLookupKeys(phoneNumber)));
        }

        return customers;
    }

    private static WhatsappConversationCustomerLookup? FindWhatsappConversationCustomer(
        IReadOnlyList<WhatsappConversationCustomerLookup> customers,
        string phoneNumber)
    {
        var lookupKeys = PhoneNumberNormalizer.GetLookupKeys(phoneNumber);
        return customers.FirstOrDefault(customer =>
            customer.LookupKeys.Any(lookupKeys.Contains));
    }

    private static WhatsappConversationMessageResponse ReadWhatsappConversationMessage(NpgsqlDataReader reader)
    {
        return new WhatsappConversationMessageResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9));
    }

    private static void AddCustomerParameters(
        NpgsqlCommand command,
        string customerId,
        string storeId,
        string? name,
        string? cpfCnpj,
        string? email,
        string? address,
        string phone,
        string createdAtUtc)
    {
        command.Parameters.AddWithValue("@id", customerId);
        command.Parameters.AddWithValue("@storeId", storeId);
        command.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
        command.Parameters.AddWithValue("@cpfCnpj", (object?)cpfCnpj ?? DBNull.Value);
        command.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
        command.Parameters.AddWithValue("@address", (object?)address ?? DBNull.Value);
        command.Parameters.AddWithValue("@phone", phone);
        command.Parameters.AddWithValue("@createdAtUtc", createdAtUtc);
    }

    private static async Task<string?> FindCustomerConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string storeId,
        string phone,
        string? cpfCnpj,
        string? excludeCustomerId,
        CancellationToken cancellationToken)
    {
        var phoneLookupKeys = PhoneNumberNormalizer.GetLookupKeys(phone);
        var phoneCommand = connection.CreateCommand();
        phoneCommand.Transaction = transaction;
        phoneCommand.CommandText =
            """
            SELECT CLIENTE_TELEFONE_CELULAR
            FROM Clientes
            WHERE StoreId = @storeId
              AND (@excludeCustomerId IS NULL OR ID_Cliente <> @excludeCustomerId)
            """;
        phoneCommand.Parameters.AddWithValue("@storeId", storeId);
        AddNullableTextParameter(phoneCommand, "@excludeCustomerId", excludeCustomerId);

        await using (var reader = await phoneCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (PhoneNumberNormalizer.GetLookupKeys(reader.GetString(0)).Any(phoneLookupKeys.Contains))
                {
                    return "telefone";
                }
            }
        }

        if (string.IsNullOrWhiteSpace(cpfCnpj))
        {
            return null;
        }

        var cpfCommand = connection.CreateCommand();
        cpfCommand.Transaction = transaction;
        cpfCommand.CommandText =
            """
            SELECT ID_Cliente
            FROM Clientes
            WHERE StoreId = @storeId
              AND CPF_CNPJ = @cpfCnpj
              AND (@excludeCustomerId IS NULL OR ID_Cliente <> @excludeCustomerId)
            LIMIT 1;
            """;
        cpfCommand.Parameters.AddWithValue("@storeId", storeId);
        cpfCommand.Parameters.AddWithValue("@cpfCnpj", cpfCnpj);
        AddNullableTextParameter(cpfCommand, "@excludeCustomerId", excludeCustomerId);
        return await cpfCommand.ExecuteScalarAsync(cancellationToken) is null ? null : "cpfCnpj";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeStaffNotificationPhoneNumber(string? value)
    {
        var digits = PhoneNumberNormalizer.NormalizeDigits(value);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        if (digits.StartsWith("55", StringComparison.Ordinal))
        {
            return $"+{digits}";
        }

        var nationalPhone = PhoneNumberNormalizer.ToBrazilNationalPhone(value);
        return string.IsNullOrWhiteSpace(nationalPhone) ? null : $"+55{nationalPhone}";
    }

    private static void AddFeedbackAnalysisParameters(
        NpgsqlCommand command,
        AgentFeedbackAnalysisData analysis,
        string analyzedAtUtc)
    {
        command.Parameters.AddWithValue("@category", analysis.Category);
        command.Parameters.AddWithValue("@sentiment", analysis.Sentiment);
        command.Parameters.AddWithValue("@customerClassification", analysis.CustomerClassification);
        command.Parameters.AddWithValue("@score", (object?)analysis.Score ?? DBNull.Value);
        command.Parameters.AddWithValue("@summary", (object?)NormalizeOptionalText(analysis.Summary) ?? DBNull.Value);
        command.Parameters.AddWithValue("@analyzedAtUtc", analyzedAtUtc);
    }

    private static AgentFeedbackSettingsResponse CreateDefaultFeedbackSettings(string storeId)
    {
        return new AgentFeedbackSettingsResponse(
            storeId,
            IsPostOrderEnabled: false,
            DefaultFeedbackDelayMinutes,
            AgentFeedbackFormats.Both,
            DefaultFeedbackRequestMessage,
            IsPeriodicSurveyEnabled: false,
            DefaultFeedbackPeriodicSurveyDays,
            DefaultFeedbackPeriodicSurveySampleSize,
            LastPeriodicSurveyRunAtUtc: null,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static AgentFeedbackSettingsResponse ReadFeedbackSettings(NpgsqlDataReader reader)
    {
        return new AgentFeedbackSettingsResponse(
            reader.GetString(0),
            reader.GetInt32(1) == 1,
            reader.GetInt32(2),
            AgentFeedbackFormats.Normalize(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5) == 1,
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9));
    }

    private static async Task<AgentFeedbackSettingsResponse> ReadFeedbackSettingsAsync(
        NpgsqlConnection connection,
        string storeId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT StoreId,
                   IsPostOrderEnabled,
                   PostOrderDelayMinutes,
                   AcceptedFormat,
                   RequestMessage,
                   IsPeriodicSurveyEnabled,
                   PeriodicSurveyDays,
                   PeriodicSurveySampleSize,
                   LastPeriodicSurveyRunAtUtc,
                   UpdatedAtUtc
            FROM AgentFeedbackSettings
            WHERE StoreId = @storeId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadFeedbackSettings(reader)
            : CreateDefaultFeedbackSettings(storeId);
    }

    private static bool IsPeriodicFeedbackDue(
        AgentFeedbackSettingsResponse settings,
        DateTimeOffset nowUtc)
    {
        if (!settings.IsPeriodicSurveyEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.LastPeriodicSurveyRunAtUtc) ||
            !DateTimeOffset.TryParse(
                settings.LastPeriodicSurveyRunAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastRun))
        {
            return true;
        }

        return lastRun.ToUniversalTime().AddDays(settings.PeriodicSurveyDays) <= nowUtc.ToUniversalTime();
    }

    private static NpgsqlCommand CreateFeedbackSolicitationSelectCommand(NpgsqlConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.Id,
                   s.StoreId,
                   s.OrderId,
                   s.PhoneNumber,
                   s.Kind,
                   s.Status,
                   s.Message,
                   s.DueAtUtc,
                   s.SentAtUtc,
                   s.RespondedAtUtc,
                   s.LastError,
                   s.CreatedAtUtc,
                   s.UpdatedAtUtc,
                   r.Id AS ResponseId,
                   r.ResponseType,
                   r.Text,
                   r.MediaUrl,
                   r.MediaContentType,
                   r.Category,
                   r.Sentiment,
                   r.CustomerClassification,
                   r.Score,
                   r.Summary,
                   r.AnalyzedAtUtc,
                   r.PromptResponseId,
                   r.ConversationId,
                   r.AiOutputJson,
                   r.CreatedAtUtc AS ResponseCreatedAtUtc
            FROM AgentFeedbackSolicitations s
            LEFT JOIN AgentFeedbackResponses r ON r.SolicitationId = s.Id
            """;
        command.CommandText += Environment.NewLine;
        return command;
    }

    private static async Task<IReadOnlyList<AgentFeedbackSolicitationResponse>> ReadFeedbackSolicitationsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var solicitations = new List<AgentFeedbackSolicitationResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AgentFeedbackResponseResponse? response = null;
            if (!reader.IsDBNull(13))
            {
                response = new AgentFeedbackResponseResponse(
                    reader.GetString(13),
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(3),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetString(17),
                    reader.IsDBNull(18) ? null : reader.GetString(18),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.IsDBNull(20) ? null : reader.GetString(20),
                    reader.IsDBNull(21) ? null : reader.GetInt32(21),
                    reader.IsDBNull(22) ? null : reader.GetString(22),
                    reader.IsDBNull(23) ? null : reader.GetString(23),
                    reader.IsDBNull(24) ? null : reader.GetString(24),
                    reader.IsDBNull(25) ? null : reader.GetString(25),
                    reader.IsDBNull(26) ? null : reader.GetString(26),
                    reader.GetString(27));
            }

            solicitations.Add(new AgentFeedbackSolicitationResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                response));
        }

        return solicitations;
    }

    private async Task UpdateAgentFeedbackSolicitationStatusAsync(
        string solicitationId,
        string status,
        string? lastError,
        bool setSentAt,
        bool setRespondedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AgentFeedbackSolicitations
            SET Status = @status,
                SentAtUtc = CASE WHEN @setSentAt = 1 THEN @updatedAtUtc ELSE SentAtUtc END,
                RespondedAtUtc = CASE WHEN @setRespondedAt = 1 THEN @updatedAtUtc ELSE RespondedAtUtc END,
                LastError = @lastError,
                UpdatedAtUtc = @updatedAtUtc
            WHERE Id = @solicitationId;
            """;
        command.Parameters.AddWithValue("@solicitationId", solicitationId.Trim());
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@setSentAt", setSentAt ? 1 : 0);
        command.Parameters.AddWithValue("@setRespondedAt", setRespondedAt ? 1 : 0);
        command.Parameters.AddWithValue("@lastError", (object?)lastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ScheduleOrderFeedbackSolicitationAsync(
        NpgsqlConnection connection,
        string storeId,
        string orderId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var settings = await ReadFeedbackSettingsAsync(connection, storeId, cancellationToken);
        if (!settings.IsPostOrderEnabled)
        {
            return;
        }

        var orderCommand = connection.CreateCommand();
        orderCommand.CommandText =
            """
            SELECT PhoneNumber
            FROM Orders
            WHERE StoreId = @storeId
              AND Id = @orderId
            LIMIT 1;
            """;
        orderCommand.Parameters.AddWithValue("@storeId", storeId);
        orderCommand.Parameters.AddWithValue("@orderId", orderId);

        var phoneNumber = PhoneNumberNormalizer.ToBrazilNationalPhone(
            await orderCommand.ExecuteScalarAsync(cancellationToken) as string);
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var dueAtUtc = completedAtUtc
            .ToUniversalTime()
            .AddMinutes(settings.PostOrderDelayMinutes)
            .UtcDateTime
            .ToString("O", CultureInfo.InvariantCulture);

        var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            """
            INSERT INTO AgentFeedbackSolicitations
                (Id, StoreId, OrderId, PhoneNumber, Kind, Status, Message, DueAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@id, @storeId, @orderId, @phoneNumber, @kind, @status, @message, @dueAtUtc, @updatedAtUtc, @updatedAtUtc)
            ON CONFLICT DO NOTHING;
            """;
        insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        insertCommand.Parameters.AddWithValue("@storeId", storeId);
        insertCommand.Parameters.AddWithValue("@orderId", orderId);
        insertCommand.Parameters.AddWithValue("@phoneNumber", phoneNumber);
        insertCommand.Parameters.AddWithValue("@kind", AgentFeedbackSolicitationKinds.Order);
        insertCommand.Parameters.AddWithValue("@status", AgentFeedbackSolicitationStatuses.Pending);
        insertCommand.Parameters.AddWithValue("@message", settings.RequestMessage);
        insertCommand.Parameters.AddWithValue("@dueAtUtc", dueAtUtc);
        insertCommand.Parameters.AddWithValue("@updatedAtUtc", now);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int? NormalizeOptionalQuantity(int? value)
    {
        return value is null ? null : Math.Max(0, value.Value);
    }

    private static string NormalizeDailyRunTime(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            TimeOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return "09:00";
    }

    private static bool IsAutomatedCampaignDue(
        AgentAutomatedCampaignResponse campaign,
        DateTimeOffset localNow)
    {
        if (!TimeOnly.TryParseExact(
                campaign.DailyRunTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var runTime))
        {
            runTime = new TimeOnly(9, 0);
        }

        var currentTime = new TimeOnly(localNow.Hour, localNow.Minute);
        if (currentTime < runTime)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(campaign.LastRunAtUtc) ||
            !DateTimeOffset.TryParse(
                campaign.LastRunAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var lastRun))
        {
            return true;
        }

        return lastRun.ToLocalTime().Date < localNow.Date;
    }

    private static NpgsqlCommand CreateAutomatedCampaignSelectCommand(NpgsqlConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.Id,
                   c.StoreId,
                   c.Type,
                   c.Name,
                   c.ProductId,
                   p.Name AS ProductName,
                   c.Message,
                   c.IsActive,
                   c.DailyRunTime,
                   c.CooldownDays,
                   c.InactiveDaysThreshold,
                   c.LastRunAtUtc,
                   c.CreatedAtUtc,
                   c.UpdatedAtUtc,
                   r.Id AS RunId,
                   r.StartedAtUtc,
                   r.CompletedAtUtc,
                   r.EligibleCount,
                   r.SkippedCooldownCount,
                   r.SentCount,
                   r.FailedCount,
                   r.Error
            FROM AgentAutomatedCampaigns c
            LEFT JOIN Products p ON p.Id = c.ProductId AND p.StoreId = c.StoreId
            LEFT JOIN AgentAutomatedCampaignRuns r ON r.Id = (
                SELECT rr.Id
                FROM AgentAutomatedCampaignRuns rr
                WHERE rr.CampaignId = c.Id
                ORDER BY rr.StartedAtUtc DESC
                LIMIT 1
            )
            """;
        return command;
    }

    private static async Task<IReadOnlyList<AgentAutomatedCampaignResponse>> ReadAutomatedCampaignResponsesAsync(
        NpgsqlConnection connection,
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var campaigns = new List<AgentAutomatedCampaignResponse>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                AgentAutomatedCampaignRunResponse? lastRun = null;
                if (!reader.IsDBNull(14))
                {
                    lastRun = new AgentAutomatedCampaignRunResponse(
                        reader.GetString(14),
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(15),
                        reader.GetString(16),
                        Convert.ToInt32(reader.GetInt64(17)),
                        Convert.ToInt32(reader.GetInt64(18)),
                        Convert.ToInt32(reader.GetInt64(19)),
                        Convert.ToInt32(reader.GetInt64(20)),
                        reader.IsDBNull(21) ? null : reader.GetString(21),
                        Array.Empty<AgentAutomatedCampaignDeliveryResponse>());
                }

                campaigns.Add(new AgentAutomatedCampaignResponse(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7) == 1,
                    reader.GetString(8),
                    reader.GetInt32(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    lastRun,
                    Array.Empty<AgentAutomatedCampaignDeliveryResponse>()));
            }
        }

        var populatedCampaigns = new List<AgentAutomatedCampaignResponse>();
        foreach (var campaign in campaigns)
        {
            var recentDeliveries = await ReadAutomatedCampaignDeliveriesAsync(
                connection,
                campaign.Id,
                runId: null,
                limit: 10,
                cancellationToken);

            var lastRun = campaign.LastRun;
            if (lastRun is not null)
            {
                var runDeliveries = await ReadAutomatedCampaignDeliveriesAsync(
                    connection,
                    campaign.Id,
                    lastRun.Id,
                    limit: 100,
                    cancellationToken);
                lastRun = lastRun with { Deliveries = runDeliveries };
            }

            populatedCampaigns.Add(campaign with
            {
                LastRun = lastRun,
                RecentDeliveries = recentDeliveries
            });
        }

        return populatedCampaigns;
    }

    private static async Task<IReadOnlyList<AgentAutomatedCampaignDeliveryResponse>> ReadAutomatedCampaignDeliveriesAsync(
        NpgsqlConnection connection,
        string campaignId,
        string? runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT Id, CampaignId, RunId, PhoneNumber, Sent, Error, CreatedAtUtc
            FROM AgentAutomatedCampaignDeliveries
            WHERE CampaignId = @campaignId
              {(runId is null ? string.Empty : "AND RunId = @runId")}
            ORDER BY CreatedAtUtc DESC, Id DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@campaignId", campaignId);
        if (runId is not null)
        {
            command.Parameters.AddWithValue("@runId", runId);
        }

        command.Parameters.AddWithValue("@limit", limit);

        var deliveries = new List<AgentAutomatedCampaignDeliveryResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new AgentAutomatedCampaignDeliveryResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6)));
        }

        return deliveries;
    }

    private static IReadOnlyList<AgentPersonaFaqResponse> NormalizePersonaFaqs(
        IReadOnlyList<AgentPersonaFaqUpsert>? faqs)
    {
        if (faqs is null || faqs.Count == 0)
        {
            return Array.Empty<AgentPersonaFaqResponse>();
        }

        var normalizedFaqs = new List<AgentPersonaFaqResponse>();
        var sortOrder = 1;
        foreach (var faq in faqs.OrderBy(faq => faq.SortOrder))
        {
            var question = NormalizeOptionalText(faq.Question);
            var answer = NormalizeOptionalText(faq.Answer);
            if (question is null || answer is null)
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(faq.Id)
                ? Guid.NewGuid().ToString("N")
                : faq.Id.Trim();

            normalizedFaqs.Add(new AgentPersonaFaqResponse(
                id,
                question,
                answer,
                faq.IsActive,
                sortOrder++));
        }

        return normalizedFaqs;
    }

    private static async Task<IReadOnlyList<AgentPersonaFaqResponse>> ReadAgentPersonaFaqsAsync(
        NpgsqlConnection connection,
        string storeId,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT Id, Question, Answer, IsActive, SortOrder
            FROM AgentPersonaFaqs
            WHERE StoreId = @storeId
              {(activeOnly ? "AND IsActive = 1" : string.Empty)}
            ORDER BY SortOrder, Question;
            """;
        command.Parameters.AddWithValue("@storeId", storeId);

        var faqs = new List<AgentPersonaFaqResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            faqs.Add(new AgentPersonaFaqResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4)));
        }

        return faqs;
    }

    private static AgentCustomerRecurrenceResponse CreateCustomerRecurrence(
        string phoneNumber,
        IReadOnlyList<DateTimeOffset> orderDates,
        DateTimeOffset now)
    {
        var orderedDates = orderDates.OrderBy(date => date).ToArray();
        var lastOrderDate = orderedDates[^1];

        decimal? averageDaysBetweenOrders = null;
        if (orderedDates.Length >= 2)
        {
            var totalDays = 0d;
            for (var index = 1; index < orderedDates.Length; index++)
            {
                totalDays += (orderedDates[index] - orderedDates[index - 1]).TotalDays;
            }

            averageDaysBetweenOrders = RoundDays((decimal)(totalDays / (orderedDates.Length - 1)));
        }

        var daysSinceLastOrder = RoundDays((decimal)Math.Max(0d, (now - lastOrderDate).TotalDays));
        var isOverdue = averageDaysBetweenOrders is not null &&
            daysSinceLastOrder > averageDaysBetweenOrders.Value;

        return new AgentCustomerRecurrenceResponse(
            phoneNumber,
            lastOrderDate.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            orderedDates.Length,
            averageDaysBetweenOrders,
            daysSinceLastOrder,
            isOverdue);
    }

    private static decimal RoundDays(decimal value)
    {
        return decimal.Round(value, 1, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<(string Alias, string NormalizedAlias)> NormalizeAliases(
        IReadOnlyList<string>? aliases,
        string normalizedName)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return Array.Empty<(string Alias, string NormalizedAlias)>();
        }

        var normalizedAliases = new List<(string Alias, string NormalizedAlias)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var trimmedAlias = alias.Trim();
            var normalizedAlias = TextNormalizer.NormalizeForLookup(trimmedAlias);
            if (string.IsNullOrWhiteSpace(normalizedAlias) ||
                string.Equals(normalizedAlias, normalizedName, StringComparison.Ordinal) ||
                !seen.Add(normalizedAlias))
            {
                continue;
            }

            normalizedAliases.Add((trimmedAlias, normalizedAlias));
        }

        return normalizedAliases;
    }

    private sealed class ProductListBuilder
    {
        public ProductListBuilder(
            string id,
            string storeId,
            string name,
            string? description,
            string? type,
            string? brand,
            long retailPriceCents,
            long? promotionalPriceCents,
            long wholesalePriceCents,
            bool isActive,
            int? stockQuantity,
            int? lowStockThreshold)
        {
            Id = id;
            StoreId = storeId;
            Name = name;
            Description = description;
            Type = type;
            Brand = brand;
            RetailPriceCents = retailPriceCents;
            PromotionalPriceCents = promotionalPriceCents;
            WholesalePriceCents = wholesalePriceCents;
            IsActive = isActive;
            StockQuantity = stockQuantity;
            LowStockThreshold = lowStockThreshold;
        }

        public string Id { get; }

        public string StoreId { get; }

        public string Name { get; }

        public string? Description { get; }

        public string? Type { get; }

        public string? Brand { get; }

        public long RetailPriceCents { get; }

        public long? PromotionalPriceCents { get; }

        public long WholesalePriceCents { get; }

        public bool IsActive { get; }

        public int? StockQuantity { get; }

        public int? LowStockThreshold { get; }

        public List<string> Aliases { get; } = new();
    }

    private sealed record WhatsappConversationSummaryBuilder(
        string PhoneNumber,
        bool IsAgentEnabled,
        string LastMessage,
        string LastMessageDirection,
        string LastMessageType,
        string LastMessageStatus,
        string LastMessageAtUtc,
        int MessageCount);

    private sealed record WhatsappConversationCustomerLookup(
        string Id,
        string? Name,
        string PhoneNumber,
        IReadOnlySet<string> LookupKeys);

    private sealed record ProductSearchCandidate(
        string Id,
        string Name,
        string NormalizedName,
        string? NormalizedAlias);

    private sealed record ProductSearchMatch(
        string Id,
        string Name,
        int Score);

    private sealed class ProductCatalogBuilder
    {
        public ProductCatalogBuilder(
            string id,
            string storeId,
            string name,
            string? description,
            string normalizedName,
            string? type,
            string? brand,
            long retailPriceCents,
            long? promotionalPriceCents,
            long wholesalePriceCents,
            bool isActive)
        {
            Id = id;
            StoreId = storeId;
            Name = name;
            Description = description;
            NormalizedName = normalizedName;
            Type = type;
            Brand = brand;
            RetailPriceCents = retailPriceCents;
            PromotionalPriceCents = promotionalPriceCents;
            WholesalePriceCents = wholesalePriceCents;
            IsActive = isActive;
        }

        public string Id { get; }

        public string StoreId { get; }

        public string Name { get; }

        public string? Description { get; }

        public string NormalizedName { get; }

        public string? Type { get; }

        public string? Brand { get; }

        public long RetailPriceCents { get; }

        public long? PromotionalPriceCents { get; }

        public long WholesalePriceCents { get; }

        public bool IsActive { get; }

        public List<ProductAliasCatalogItem> Aliases { get; } = new();
    }

    private sealed class ActiveOrderBuilder
    {
        public ActiveOrderBuilder(
            string id,
            string status,
            string? saleType,
            long totalCents,
            string createdAtUtc,
            string updatedAtUtc)
        {
            Id = id;
            Status = status;
            SaleType = saleType;
            TotalCents = totalCents;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string Id { get; }

        public string Status { get; }

        public string? SaleType { get; }

        public long TotalCents { get; }

        public string CreatedAtUtc { get; }

        public string UpdatedAtUtc { get; }

        public List<ActiveOrderItemData> Items { get; } = new();
    }

    private sealed class OrderManagementCustomerBuilder
    {
        public OrderManagementCustomerBuilder(string phoneNumber)
        {
            PhoneNumber = phoneNumber;
        }

        public string PhoneNumber { get; }

        public Dictionary<string, OrderManagementOrderBuilder> Orders { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed class OrderManagementOrderBuilder
    {
        public OrderManagementOrderBuilder(
            string id,
            string status,
            string? saleType,
            long totalCents,
            string? generalObservation,
            string createdAtUtc,
            string updatedAtUtc)
        {
            Id = id;
            Status = status;
            SaleType = saleType;
            TotalCents = totalCents;
            GeneralObservation = generalObservation;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string Id { get; }

        public string Status { get; }

        public string? SaleType { get; }

        public long TotalCents { get; }

        public string? GeneralObservation { get; }

        public string CreatedAtUtc { get; }

        public string UpdatedAtUtc { get; }

        public List<OrderManagementOrderItemResponse> Items { get; } = new();
    }

    private sealed class DashboardRecentOrderBuilder
    {
        public DashboardRecentOrderBuilder(
            string id,
            string phoneNumber,
            string status,
            string? saleType,
            long totalCents,
            string? generalObservation,
            string createdAtUtc,
            string updatedAtUtc,
            bool isLate)
        {
            Id = id;
            PhoneNumber = phoneNumber;
            Status = status;
            SaleType = saleType;
            TotalCents = totalCents;
            GeneralObservation = generalObservation;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            IsLate = isLate;
        }

        public string Id { get; }

        public string PhoneNumber { get; }

        public string Status { get; }

        public string? SaleType { get; }

        public long TotalCents { get; }

        public string? GeneralObservation { get; }

        public string CreatedAtUtc { get; }

        public string UpdatedAtUtc { get; }

        public bool IsLate { get; }

        public List<DashboardRecentOrderItemResponse> Items { get; } = new();
    }

    private static async Task<bool> TableHasColumnAsync(
        NpgsqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @tableName
              AND column_name = @columnName;
            """;
        command.Parameters.AddWithValue("@tableName", tableName.ToLowerInvariant());
        command.Parameters.AddWithValue("@columnName", columnName.ToLowerInvariant());

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture) > 0;
    }
}
