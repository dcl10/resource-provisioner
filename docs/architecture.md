# Architecture

> Skeleton. The request state machine and spend caps are filled in by #14, and the agent loop by #17.

## Components

```
Browser ──HTTPS──▶ Caddy ─┬─ app.<domain>  → web
                          └─ auth.<domain> → keycloak   (/admin blocked publicly)

web (Next.js + Auth.js) ──Bearer JWT (via /api/proxy)──▶ api (ASP.NET Core 10)
        │                                                  │
        └──── OIDC sign-in ────▶ keycloak ◀── admin API ───┤  (invites)
                                    │                      ├──▶ Anthropic API (Claude, tool use)
                                    ▼                      ├──▶ Postgres (app DB, EF Core)
                          Postgres (keycloak DB)           └──▶ terraform CLI ──▶ DigitalOcean
                          SMTP (MailHog locally)                 (one job at a time, per-request
                                                                   workspace + state)
```

| Component | Role |
| --- | --- |
| `web/` | Chat page, requests list and detail, admin Users page. A `/api/proxy/[...path]` route attaches the session's access token (and passes SSE through), so the browser never holds the API token. |
| `api/` | Auth and policies, invitations, catalog, the Claude agent, provisioning requests and the Terraform runner. |
| `catalog/<type>/` | `main.tf`, `variables.tf`, `outputs.tf` and a `resource.json` manifest for each supported resource type. |
| `keycloak/` | `realm-export.json` for the `resource-provisioner` realm, imported on first start. |
| `infra/` | Terraform for the droplet that hosts the POC, with its own state, separate from the catalog. |
| `workspaces/<requestId>/` | A per-request copy of the module, its tfvars and its state. Runtime only, gitignored. |

## API layout

```
api/
├── ResourceProvisioner.slnx
├── Directory.Build.props        # net10.0, nullable, implicit usings for every project
├── Directory.Packages.props     # Central Package Management: every NuGet version lives here
├── .config/dotnet-tools.json    # dotnet-ef, pinned to the EF Core version
├── src/ResourceProvisioner.Api/
│   ├── Program.cs               # host setup: options, auth, DbContext, catalog, controllers, /health
│   ├── Auth/                    # roles, policies, realm roles mapping, ICurrentUser
│   ├── Config/                  # typed options, one class per config section
│   ├── Controllers/             # API controllers, one per resource (MeController: GET /api/me,
│   │                            # CatalogController: GET /api/catalog)
│   ├── Data/                    # AppDbContext, design-time factory, Migrations/
│   ├── Extensions/              # service registration helpers
│   ├── Models/                  # request and response types
│   └── ResourceCatalog/         # manifest types, loader and checks, ICatalogService (see catalog.md)
└── tests/ResourceProvisioner.Api.Tests/
    └── Fixtures/                # ApiFactory (WebApplicationFactory on in-memory SQLite), TestAuthHandler,
                                 # TestAdminController (test-only admin endpoint), TempCatalog,
                                 # catalog/ (fixture resource types, not real modules)
```

Run it from `api/`:

```bash
dotnet tool restore                                   # installs dotnet-ef
dotnet run --project src/ResourceProvisioner.Api      # http://localhost:5000/health
dotnet test
dotnet ef database update --project src/ResourceProvisioner.Api
```

Migrations are **not** applied at startup; run `dotnet ef database update` after pulling a new migration. Add one
with `dotnet ef migrations add <Name> --project src/ResourceProvisioner.Api -o Data/Migrations`.

### Configuration

Options are bound from configuration and validated when the app starts, so an invalid value stops startup; so does an
invalid `resource.json`, since the catalog loads at startup too. Secrets may be empty at startup; the features that
use them check for them.

| Key | Default | Secret |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | none | yes |
| `Keycloak:AuthServerUrl` | `http://localhost:8080` | no |
| `Keycloak:Realm` | `resource-provisioner` | no |
| `Keycloak:Resource` | `rp-api` (the JWT audience) | no |
| `Keycloak:Secret` | none (the `rp-api` client secret, used for invites) | yes |
| `Invitations:AppUrl` | `http://localhost:3000` (where invited users land; a valid `rp-web` redirect URI) | no |
| `Invitations:ClientId` | `rp-web` (the client the invite link belongs to) | no |
| `Invitations:ExpiryDays` | `7` (how long the invite link works, 1 to 30) | no |
| `Anthropic:ApiKey` | none | yes |
| `Anthropic:Model` | `claude-sonnet-5-5` | no |
| `Anthropic:Effort` | `low` (`low`, `medium`, `high`, `xhigh` or `max`) | no |
| `DigitalOcean:Token` | none | yes |
| `Spaces:AccessId` / `Spaces:SecretKey` | none | yes |
| `Catalog:Root` | `catalog` (looked for in the content root, then each folder above it; see [catalog.md](catalog.md#where-the-catalog-is)) | no |
| `Workspaces:Root` | `workspaces` (relative to the working directory) | no |
| `Limits:MaxActiveRequestsPerUser` | `3` | no |
| `Limits:MaxActiveRequestsTotal` | `10` | no |
| `Limits:ChatMessagesPerMinute` | `10` | no |

Secrets never go in `appsettings*.json`. Locally they live in user-secrets, from `api/`:

```bash
dotnet user-secrets --project src/ResourceProvisioner.Api set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=resource_provisioner;Username=rp;Password=dev_password"
dotnet user-secrets --project src/ResourceProvisioner.Api set "Keycloak:Secret" "<rp-api client secret>"
dotnet user-secrets --project src/ResourceProvisioner.Api set "Anthropic:ApiKey" "<key>"
dotnet user-secrets --project src/ResourceProvisioner.Api set "DigitalOcean:Token" "<token>"
```

When deployed they come from environment variables (`Anthropic__ApiKey` and so on).

## Key design rules

1. **The LLM never writes HCL and never triggers provisioning.** It picks a type from the vetted catalog and fills in
   its variables. The `propose_request` tool validates them on the server and creates a request in
   `AwaitingConfirmation`. Provisioning starts only when the user clicks Confirm in the UI, which calls
   `POST /api/requests/{id}/confirm`.
2. **Each `resource.json` manifest is the single source of truth.** Tool schemas, server-side validation and the
   confirmation summary are all generated from it.
3. **The agent has four tools:** `list_resource_types`, `get_resource_spec`, `list_my_resources` and
   `propose_request`.
4. **Every endpoint requires sign-in.** Sign-in is invite-only through Keycloak. Users see only their own
   conversations and requests; `rp-admin` sees everything and manages invitations.
5. **Spend is capped:** a per-user rate limit on chat messages, a per-user cap on active resources, a global cap,
   cheap sizes allow-listed in the manifests, and a runner that applies one job at a time. The default model is
   `claude-sonnet-5-5` at low effort.
6. **Secrets never reach the LLM:** the DigitalOcean token, the Spaces keys and sensitive Terraform outputs stay out
   of the model's context.
7. **Every resource is tagged** `resource-provisioner`, `rp-<requestId>` and `rp-owner-<short-sub>`, and the tags are
   forced by the runner rather than chosen by the LLM.

## Request states

```
AwaitingConfirmation → Queued → Applying → Succeeded | Failed
Succeeded → Destroying → Destroyed | Failed
AwaitingConfirmation → Cancelled
```

Transition rules and tests: *to be written in #14.*

## Spend caps

Configuration keys and defaults: *to be written in #14 and #17.*
