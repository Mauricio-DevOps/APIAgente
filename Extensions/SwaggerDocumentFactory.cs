namespace AtendenteWhatssApp.Extensions;

internal static class SwaggerDocumentFactory
{
    public static object Create()
    {
        return new
        {
            openapi = "3.0.1",
            info = new
            {
                title = "AtendenteWhatsApp API",
                version = "v1",
                description = "WhatsApp flow backed by Supabase/PostgreSQL conversation, product, customer, and order state."
            },
            paths = new Dictionary<string, object>
            {
                ["/api/ia/chat-whatsapp"] = new
                {
                    post = new
                    {
                        summary = "Receives a WhatsApp message, resolves the store prompt, and handles text, order registration, order consultation, or human handoff based on the AI response type.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "message", "phoneNumber", "storeId" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["message"] = new { type = "string" },
                                            ["phoneNumber"] = new { type = "string" },
                                            ["storeId"] = new { type = "string" }
                                        }
                                    },
                                    example = new
                                    {
                                        message = "Quero uma pizza grande de calabresa.",
                                        phoneNumber = "5511999999999",
                                        storeId = "whatsapp:+14155238886"
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new
                            {
                                description = "Plain text response for the WhatsApp client.",
                                content = new Dictionary<string, object>
                                {
                                    ["text/plain"] = new
                                    {
                                        schema = new { type = "string" }
                                    }
                                }
                        },
                        ["400"] = new
                        {
                            description = "Validation failure or store without prompt mapping."
                        },
                        ["500"] = new
                        {
                            description = "Unexpected error."
                        }
                    }
                }
            },
                ["/api/ia/products/lookup"] = new
                {
                    get = new
                    {
                        summary = "Looks up active product details by product name for agent/tool use.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "name",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "limit",
                                @in = "query",
                                required = false,
                                schema = new { type = "integer", format = "int32", minimum = 1, maximum = 10 }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Matching active product details." },
                            ["400"] = new { description = "storeId and name are required." }
                        }
                    }
                },
                ["/api/admin/stores/prompt"] = new
                {
                    post = new
                    {
                        summary = "Creates or updates the local store to prompt mapping.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "promptId" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["promptId"] = new { type = "string" }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        promptId = "pmpt_69ea654d6dd08190870bcc1a8eef07b10e6ecc9fc5eea817"
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new
                            {
                                description = "Mapping saved."
                            },
                            ["400"] = new
                            {
                                description = "Validation failure."
                            }
                        }
                    }
                },
                ["/api/admin/stores/conversation/reset"] = new
                {
                    post = new
                    {
                        summary = "Resets homologation data, including customers, products, orders, WhatsApp conversations, messages, campaign runs, and feedback records.",
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new
                            {
                                description = "Homologation data cleared. Companies, store prompt mappings, agent persona, FAQs, and feedback settings are preserved."
                            }
                        }
                    }
                },
                ["/api/admin/stores/database/reset"] = new
                {
                    post = new
                    {
                        summary = "Alias for the homologation database reset.",
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new
                            {
                                description = "Homologation data cleared. Companies, store prompt mappings, agent persona, FAQs, and feedback settings are preserved."
                            }
                        }
                    }
                },
                ["/api/admin/whatsapp/conversations/{phoneNumber}/reset"] = new
                {
                    post = new
                    {
                        summary = "Clears WhatsApp conversation state, messages, and queued jobs for one customer phone number.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "phoneNumber",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new { description = "Conversation reset for the selected customer." },
                            ["400"] = new { description = "storeId and phoneNumber are required." }
                        }
                    }
                },
                ["/api/admin/products"] = new
                {
                    get = new
                    {
                        summary = "Lists products for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Products found for the store." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    },
                    post = new
                    {
                        summary = "Creates or updates a product for a store.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "name", "retailPrice", "wholesalePrice" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["name"] = new { type = "string" },
                                            ["description"] = new { type = "string" },
                                            ["type"] = new { type = "string" },
                                            ["brand"] = new { type = "string" },
                                            ["retailPrice"] = new { type = "number" },
                                            ["promotionalPrice"] = new { type = "number", nullable = true },
                                            ["wholesalePrice"] = new { type = "number" },
                                            ["aliases"] = new
                                            {
                                                type = "array",
                                                items = new { type = "string" }
                                            },
                                            ["isActive"] = new { type = "boolean" }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        name = "Açaí 500ml",
                                        description = "Produto para venda no varejo e atacado.",
                                        retailPrice = 18.50m,
                                        wholesalePrice = 14.00m,
                                        aliases = new[] { "Acai 500", "Açaí 500" },
                                        isActive = true
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Product saved." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/products/import"] = new
                {
                    post = new
                    {
                        summary = "Imports products in bulk after upload validation.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "rows" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["rows"] = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    properties = new Dictionary<string, object>
                                                    {
                                                        ["rowNumber"] = new { type = "integer" },
                                                        ["action"] = new { type = "string", @enum = new[] { "Create", "Update", "Skip" } },
                                                        ["productId"] = new { type = "string", nullable = true },
                                                        ["name"] = new { type = "string" },
                                                        ["description"] = new { type = "string" },
                                                        ["type"] = new { type = "string" },
                                                        ["brand"] = new { type = "string" },
                                                        ["retailPrice"] = new { type = "number" },
                                                        ["promotionalPrice"] = new { type = "number", nullable = true },
                                                        ["isActive"] = new { type = "boolean" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Import processed with per-row results." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/products/{productId}"] = new
                {
                    put = new
                    {
                        summary = "Updates one product by id for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "productId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "name", "retailPrice", "wholesalePrice" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["name"] = new { type = "string" },
                                            ["description"] = new { type = "string" },
                                            ["type"] = new { type = "string" },
                                            ["brand"] = new { type = "string" },
                                            ["retailPrice"] = new { type = "number" },
                                            ["promotionalPrice"] = new { type = "number", nullable = true },
                                            ["wholesalePrice"] = new { type = "number" },
                                            ["aliases"] = new
                                            {
                                                type = "array",
                                                items = new { type = "string" }
                                            },
                                            ["isActive"] = new { type = "boolean" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Product updated." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Product not found for the store." },
                            ["409"] = new { description = "Another product already uses the requested name." }
                        }
                    },
                    delete = new
                    {
                        summary = "Inactivates one product by id for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "productId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new { description = "Product inactivated." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Product not found for the store." }
                        }
                    }
                },
                ["/api/admin/customers"] = new
                {
                    get = new
                    {
                        summary = "Lists customers for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Customers found for the store." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    },
                    post = new
                    {
                        summary = "Creates a customer for a store.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "clienteTelefoneCelular" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["clienteNome"] = new { type = "string", nullable = true },
                                            ["cpfCnpj"] = new { type = "string", nullable = true },
                                            ["clienteEmail"] = new { type = "string", nullable = true },
                                            ["clienteTelefoneCelular"] = new { type = "string" }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        clienteNome = "Cliente Teste",
                                        cpfCnpj = "12345678900",
                                        clienteEmail = "cliente@example.com",
                                        clienteTelefoneCelular = "5511999999999"
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Customer created." },
                            ["400"] = new { description = "Validation failure." },
                            ["409"] = new { description = "Another customer already uses the phone or CPF/CNPJ." }
                        }
                    }
                },
                ["/api/admin/customers/import"] = new
                {
                    post = new
                    {
                        summary = "Imports customers in bulk after upload validation.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "rows" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["rows"] = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    properties = new Dictionary<string, object>
                                                    {
                                                        ["rowNumber"] = new { type = "integer" },
                                                        ["action"] = new { type = "string", @enum = new[] { "Create", "Update", "Skip" } },
                                                        ["customerId"] = new { type = "string", nullable = true },
                                                        ["clienteNome"] = new { type = "string", nullable = true },
                                                        ["cpfCnpj"] = new { type = "string", nullable = true },
                                                        ["clienteEmail"] = new { type = "string", nullable = true },
                                                        ["clienteTelefoneCelular"] = new { type = "string" }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Import processed with per-row results." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/customers/{customerId}"] = new
                {
                    put = new
                    {
                        summary = "Updates one customer by id for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "customerId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "clienteTelefoneCelular" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["clienteNome"] = new { type = "string", nullable = true },
                                            ["cpfCnpj"] = new { type = "string", nullable = true },
                                            ["clienteEmail"] = new { type = "string", nullable = true },
                                            ["clienteTelefoneCelular"] = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Customer updated." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Customer not found for the store." },
                            ["409"] = new { description = "Another customer already uses the phone or CPF/CNPJ." }
                        }
                    },
                    delete = new
                    {
                        summary = "Deletes one customer by id for a store.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "customerId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new { description = "Customer deleted." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Customer not found for the store." }
                        }
                    }
                },
                ["/api/admin/agent/product-campaign/preview"] = new
                {
                    get = new
                    {
                        summary = "Previews a proactive product campaign for customers who completed orders with the selected product.",
                        parameters = new[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "productId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Campaign preview with suggested message and eligible customers." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Product not found for the store." }
                        }
                    }
                },
                ["/api/admin/agent/product-campaign/send"] = new
                {
                    post = new
                    {
                        summary = "Sends a proactive product campaign to customers from the recalculated campaign preview.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "productId", "message" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["productId"] = new { type = "string" },
                                            ["message"] = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Per-customer send result." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Product not found for the store." }
                        }
                    }
                },
                ["/api/admin/agent/customers"] = new
                {
                    get = new
                    {
                        summary = "Lists customer recurrence metrics based on completed orders.",
                        parameters = new[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Customers with last order, average interval and overdue flag." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/agent/customer-reminder/send"] = new
                {
                    post = new
                    {
                        summary = "Sends a proactive recurrence reminder to one customer.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "phoneNumber", "message" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["phoneNumber"] = new { type = "string" },
                                            ["message"] = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Per-customer send result." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Customer not found for the store." }
                        }
                    }
                },
                ["/api/admin/orders/manage"] = new
                {
                    get = new
                    {
                        summary = "Lists orders grouped by customer for administrative status management.",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "storeId",
                                @in = "query",
                                required = true,
                                schema = new { type = "string" }
                            },
                            new
                            {
                                name = "status",
                                @in = "query",
                                required = false,
                                schema = new
                                {
                                    type = "string",
                                    @enum = new[] { "PendingReview", "EmProducao", "EmRotaEntrega", "Concluido" }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Orders grouped by customer." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/orders/import-history"] = new
                {
                    post = new
                    {
                        summary = "Imports historical completed orders from validated Excel rows.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "rows" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["rows"] = new
                                            {
                                                type = "array",
                                                items = new
                                                {
                                                    type = "object",
                                                    required = new[] { "rowNumber", "pedidoCodigo", "clienteTelefoneCelular", "produtoNome", "quantidade" },
                                                    properties = new Dictionary<string, object>
                                                    {
                                                        ["rowNumber"] = new { type = "integer" },
                                                        ["pedidoCodigo"] = new { type = "string" },
                                                        ["pedidoData"] = new { type = "string", nullable = true },
                                                        ["clienteNome"] = new { type = "string", nullable = true },
                                                        ["cpfCnpj"] = new { type = "string", nullable = true },
                                                        ["clienteEmail"] = new { type = "string", nullable = true },
                                                        ["clienteTelefoneCelular"] = new { type = "string" },
                                                        ["tipoVenda"] = new { type = "string", nullable = true, @enum = new[] { "varejo", "atacado" } },
                                                        ["produtoNome"] = new { type = "string" },
                                                        ["quantidade"] = new { type = "integer" },
                                                        ["precoUnitario"] = new { type = "number", nullable = true },
                                                        ["observacaoItem"] = new { type = "string", nullable = true },
                                                        ["observacaoPedido"] = new { type = "string", nullable = true }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        rows = new[]
                                        {
                                            new
                                            {
                                                rowNumber = 2,
                                                pedidoCodigo = "HIST-001",
                                                pedidoData = "2026-05-01T10:00:00-03:00",
                                                clienteNome = "Cliente Teste",
                                                cpfCnpj = "12345678900",
                                                clienteEmail = "cliente@example.com",
                                                clienteTelefoneCelular = "5511999999999",
                                                tipoVenda = "varejo",
                                                produtoNome = "Produto Teste",
                                                quantidade = 2,
                                                precoUnitario = 19.90m,
                                                observacaoItem = "Sem observacao",
                                                observacaoPedido = "Importado do historico"
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Import processed with created counts, skipped duplicate orders and per-row errors." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/api/admin/orders/{orderId}/status"] = new
                {
                    patch = new
                    {
                        summary = "Updates an order status.",
                        parameters = new[]
                        {
                            new
                            {
                                name = "orderId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string" }
                            }
                        },
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "status" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["status"] = new
                                            {
                                                type = "string",
                                                @enum = new[] { "PendingReview", "EmProducao", "EmRotaEntrega", "Concluido" }
                                            }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        status = "EmRotaEntrega"
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["204"] = new { description = "Order status updated." },
                            ["400"] = new { description = "Validation failure." },
                            ["404"] = new { description = "Order not found." }
                        }
                    }
                },
                ["/api/orders/register"] = new
                {
                    post = new
                    {
                        summary = "Registers an order from the structured AI order payload.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        required = new[] { "storeId", "phoneNumber", "sourceMessageId", "texto", "pedido" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["storeId"] = new { type = "string" },
                                            ["phoneNumber"] = new { type = "string" },
                                            ["sourceMessageId"] = new { type = "string" },
                                            ["texto"] = new { type = "string" },
                                            ["customerMessage"] = new { type = "string" },
                                            ["pedido"] = new { type = "object" }
                                        }
                                    },
                                    example = new
                                    {
                                        storeId = "whatsapp:+14155238886",
                                        phoneNumber = "whatsapp:+5511999999999",
                                        sourceMessageId = "SM123",
                                        texto = "Pedido registrado: 2 açaís de 500ml com leite em pó.",
                                        pedido = new
                                        {
                                            tipoVenda = "varejo",
                                            itens = new[]
                                            {
                                                new
                                                {
                                                    produto = "Açaí 500ml",
                                                    quantidade = 2,
                                                    observacao = "com leite em pó"
                                                }
                                            },
                                            observacaoGeral = (string?)null
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Order registered or marked as pending review." },
                            ["400"] = new { description = "Validation failure." }
                        }
                    }
                },
                ["/whatsapp"] = new
                {
                    post = new
                    {
                        summary = "Receives the Twilio WhatsApp webhook and responds with TwiML.",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/x-www-form-urlencoded"] = new
                                {
                                    schema = new
                                    {
                                        type = "object",
                                        properties = new Dictionary<string, object>
                                        {
                                            ["Body"] = new { type = "string" },
                                            ["From"] = new { type = "string" },
                                            ["To"] = new { type = "string" },
                                            ["MessageSid"] = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new
                            {
                                description = "Twilio TwiML response.",
                                content = new Dictionary<string, object>
                                {
                                    ["application/xml"] = new
                                    {
                                        schema = new { type = "string" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
