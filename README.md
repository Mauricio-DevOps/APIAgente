# Atendente WhatsApp API

.NET API for the WhatsApp attendant flow.

## Required configuration

Configure these values outside source control:

- `SUPABASE_DB_URL`
- `Twilio__AccountSid`
- `Twilio__AuthToken`
- `InternalApi__ServiceKey`

Optional seed values:

- `SeedCompany__CompanyName`
- `SeedCompany__CompanyPhone`
- `SeedCompany__InitialPassword`

For Azure Web App, use App Settings with the same names. For local development,
copy `.env.example` to `.env.supabase.local` and fill in the real values.

## Run locally

```powershell
dotnet run --project AtendenteWhatssApp.csproj --urls http://localhost:5253
```
