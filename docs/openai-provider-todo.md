# OpenAI provider — TODO / verification handoff

Status as of this session (branch `feature/chatgpt-provider`): **implemented, builds clean, 26/26
tests pass, app starts without crash. NOT yet verified against a live OpenAI account.**

> Note: the branch is named `chatgpt-provider`, but the feature pivoted to the **OpenAI Platform
> API** (see below). The provider's display name and all types are `OpenAI*`.

## What it does

Tracks **OpenAI Platform prepaid-credit balance** as a usage meter (`OpenAI Credits` =
`total_available / total_granted`). Authenticates with an **API key** the user pastes in
Settings ▸ Accounts (stored DPAPI-encrypted), not a browser login.

### Why not ChatGPT subscription limits (the original ask)
A live spike found the ChatGPT consumer app (chatgpt.com) exposes **no passive remaining-usage
endpoint** — its rate-limit signal only appears inline in `POST /backend-api/conversation` when
you actually send a message. So ChatGPT can't be metered passively like Claude/OpenCode. The
OpenAI Platform billing API is the only OpenAI surface with a real passive %-quota.
(Refs: `terminalcommandnewsletter/everything-chatgpt`, `Jerry-Terrasse/oai_rate_limit`.)

---

## ▶ The one thing that still needs YOU: live verification

The `credit_grants` endpoint path and JSON shape are reverse-engineered from community docs +
the CodexBar menu-bar app — **not yet confirmed against a real account.**

**Steps:**
1. `dotnet run --project src/AiMeter/AiMeter.csproj`
2. Open Settings (tray or widget gear) ▸ **Accounts** ▸ the **OpenAI** row ▸ "Log into OpenAI".
3. Paste an OpenAI **user or admin** key with **billing read access** (project `sk-proj-…` keys
   return 403 — that's expected and surfaces as "OpenAI (No Billing Access)").
4. In Settings ▸ **Metrics shown**, enable **OpenAI Credits** (new metrics aren't auto-selected
   if you already have a saved selection), Save.
5. Confirm the widget shows `OpenAI Credits` with a plausible remaining %.

**If it shows an error tile instead:**
- `OpenAI (Invalid Key)` (401) — wrong/rejected key.
- `OpenAI (No Billing Access)` (403) — project key or no billing scope; try a user/admin key.
- `OpenAI (Billing Unavailable)` (404) — the account/endpoint doesn't serve credit_grants →
  the path or shape has changed; capture the real call (browser DevTools on
  `platform.openai.com/settings/organization/billing/…`) and update:
  - `OpenAiProvider.CreditGrantsEndpoint` (the path constant)
  - `OpenAiProvider.ParseCreditGrants` (the field names)
  - `OpenAiProviderTests.CreditGrantsJson` / `CreditGrantsPath` (keep tests in sync)
- `OpenAI (No Credits)` — account has `total_granted = 0` (pay-as-you-go, no prepaid credits);
  see the monthly-budget follow-up below.

Manual test the key storage too:
- Enter a key → status flips to "Logged In", restart the app → still logged in (DPAPI persisted).
- "Log out" → key is cleared from `settings.json` (`OpenAiApiKeyProtected` becomes null).

---

## Follow-ups / known limitations (optional, not blocking)

- [ ] **Monthly-budget fallback** for pay-as-you-go accounts (no prepaid credits): add a second
      path using `GET /v1/dashboard/billing/subscription` (`hard_limit_usd` = monthly budget) +
      `GET /v1/dashboard/billing/usage?start_date=<month-start>&end_date=<today+1>`
      (`total_usage` in cents) → remaining = limit − usage. Surface as `OpenAI Budget`.
- [ ] **Reset/expiry time**: `credit_grants.grants.data[].expires_at` (unix seconds) could feed
      `UsageMetric.ResetTime` as a "credits expire in Xd" countdown. Left null for now because
      credit expiry isn't a "reset" (you get 0, not a refill) and the widget labels it "resets".
- [ ] **Key dialog polish**: `OpenAiAuthWindow` uses default WPF button chrome on the dark bg;
      could adopt the shared `SettingsStyles.xaml` button style for consistency.
- [ ] **Account-row wording**: the generic button says "Log into OpenAI" for what is really a key
      paste. Fine for now; could add optional custom button text to `AccountRowViewModel`.
- [ ] **README/design docs**: add OpenAI to the providers list once live-verified.
- [ ] Consider renaming the branch to `feature/openai-provider` to match the final scope.

---

## File map (this feature)

**New — `src/AiMeter/`:**
- `Providers/OpenAiProvider.cs` — `credit_grants` → `OpenAI Credits` metric; honest error tiles.
- `Services/IOpenAiSession.cs` / `OpenAiSession.cs` — API-key store/get; DPAPI encrypt/decrypt.
- `Services/IOpenAiApiClient.cs` / `OpenAiApiClient.cs` — plain `HttpClient` to `api.openai.com`.
- `Views/OpenAiAuthWindow.xaml(.cs)` — masked API-key entry dialog (not a browser).

**New — `tests/AiMeter.Tests/`:**
- `OpenAiProviderTests.cs` — no-key / parse+bearer / 401 / 403 / no-credits.
- `Fakes/FakeOpenAiSession.cs`, `Fakes/FakeOpenAiApiClient.cs`.

**Changed:**
- `Models/AppConfig.cs` — `OpenAiApiKeyProtected` (DPAPI blob) replaces the old session bool.
- `App.xaml.cs` — DI registration + dispose the HttpClient client on exit.
- `ViewModels/SettingsViewModel.cs` — OpenAI account row.
- `tests/…/SettingsViewModelResyncTests.cs` — inject `FakeOpenAiSession`.
- `AiMeter.csproj` — `System.Security.Cryptography.ProtectedData` (DPAPI).
