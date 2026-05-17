namespace AtendenteWhatssApp.Extensions;

internal static class SwaggerEndpointsExtensions
{
    public static WebApplication MapLocalSwagger(this WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/swagger"));

        app.MapGet("/swagger", () =>
        {
            const string html = """
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Swagger UI</title>
              <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
              <style>
                body { margin: 0; background: #f6f7fb; }
                #swagger-ui { max-width: 1200px; margin: 0 auto; }
              </style>
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
              <script>
                window.onload = () => {
                  SwaggerUIBundle({
                    url: '/swagger/v1/swagger.json',
                    dom_id: '#swagger-ui',
                    deepLinking: true,
                    presets: [SwaggerUIBundle.presets.apis],
                    layout: 'BaseLayout'
                  });
                };
              </script>
            </body>
            </html>
            """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/swagger/v1/swagger.json", () =>
            Results.Json(SwaggerDocumentFactory.Create()));

        return app;
    }
}
