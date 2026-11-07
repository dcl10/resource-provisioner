# Architecture

> The agent loop is filled in by #17.

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
│   ├── Program.cs               # host setup: options, auth, invitations, DbContext, catalog, requests, runner,
│   │                            # controllers, /health
│   ├── Auth/                    # roles, policies, realm roles mapping, ICurrentUser
│   ├── Config/                  # typed options, one class per config section
│   ├── Controllers/             # API controllers, one per resource (MeController: GET /api/me,
│   │                            # CatalogController: GET /api/catalog, InvitationsController: /api/invitations,
│   │                            # RequestsController: /api/requests)
│   ├── Data/                    # AppDbContext, design-time factory, Migrations/
│   ├── Extensions/              # service registration helpers
│   ├── Models/                  # request and response types, the Invitation, ProvisioningRequest and JobLogLine
│   │                            # entities
│   ├── ResourceCatalog/         # manifest types, loader and checks, ICatalogService (see catalog.md)
│   └── Services/                # Keycloak/ (IKeycloakUserAdmin over the admin API), Invitations/ (see auth-setup.md),
│                                # Requests/ (IProvisioningRequestService, the state machine, RunnerVariables, logs),
│                                # Provisioning/ (the Terraform runner, see provisioning.md),
│                                # Terraform/ (ITerraformProcessRunner, the only thing that starts the CLI)
└── tests/ResourceProvisioner.Api.Tests/
    └── Fixtures/                # ApiFactory (WebApplicationFactory on a temp SQLite file, runner off), TestAuthHandler,
                                 # TestAdminController (test-only admin endpoint), InMemoryKeycloakUserAdmin,
                                 # FakeTerraform, TempCatalog, RequestSeeding (requests in any status),
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
| `Runner:TerraformPath` | `terraform` (on `PATH`) | no |
| `Runner:LogPollInterval` | `00:00:01` (how often the logs endpoint checks for new lines) | no |
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
   conversations and requests; `rp-admin` sees everything and manages invitations, but confirms, cancels and
   references only their own requests.
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
Succeeded | Failed   → Destroying → Destroyed | Failed
AwaitingConfirmation → Cancelled
```

A provisioning request (`ProvisioningRequest`, table `ProvisioningRequests`) holds the owner's `sub` (and their email
at the time, so admins can tell whose it is), the type, the validated variables with defaults applied (JSON), the
status, the non-sensitive outputs (JSON), the conversation it came from and when it was created, last changed and
confirmed. Its ID is a GUID, always rendered lowercase (`3f2b8c1e-5d4a-...`): that's the form the `rp-<requestId>`
tag and the manifests' droplet-reference patterns use.

| Status | Meaning | Moved there by |
| --- | --- | --- |
| `AwaitingConfirmation` | Proposed; nothing has happened | the agent's `propose_request` tool (#17) |
| `Queued` | Confirmed, waiting for the runner | the owner's Confirm (`POST /api/requests/{id}/confirm`) |
| `Applying` | `terraform apply` is running | the runner ([provisioning.md](provisioning.md)) |
| `Succeeded` | The resource exists; outputs are stored | the runner |
| `Failed` | Apply or destroy failed; resources may be left behind | the runner |
| `Destroying` | `terraform destroy` is running | destroy (#16) |
| `Destroyed` | Nothing is left | the runner |
| `Cancelled` | The owner cancelled before confirming; nothing was created | the owner (`POST /api/requests/{id}/cancel`) |

Every status change goes through `RequestStateMachine.Move`, which refuses any move not drawn above (the API answers
409). `Failed → Destroying` is allowed because a failed apply can leave part of a module created, and a failed destroy
certainly does, so a failed request can always be destroyed (again). `Queued` can't be cancelled: the runner may
already have picked it up. `Status` is also an EF Core concurrency token, so if two changes race (a confirm and a
cancel, or the runner), the second fails with 409 instead of overwriting the first.

**Proposing.** `IProvisioningRequestService.ProposeAsync(owner, type, variables, conversationId)` is what
`propose_request` calls. It validates the variables with `ICatalogService.ValidateVariables` (which includes the
manifest's `atLeastOneOf` rules, see [catalog.md](catalog.md#rules-across-variables)), then checks every
droplet reference (see [below](#ownership)), and creates the request in `AwaitingConfirmation`, or returns the errors
by field path for the agent to fix. There is no HTTP endpoint for proposing: only the agent creates requests.

**Confirming** checks again, because time passes between propose and confirm: the variables and droplet references
are re-validated (a referenced droplet may have been destroyed meanwhile; the API answers 422 with the errors), then
the [spend caps](#spend-caps), then the request moves to `Queued` and onto the runner's queue. Running Terraform is the
runner's job: see [provisioning.md](provisioning.md).

**Outputs.** Non-sensitive outputs go in `ProvisioningRequests.Outputs`. Sensitive ones (`"sensitive": true` in the
manifest: passwords, connection URIs) go in their own table, `RequestSensitiveOutputs`, keyed by request ID. The list
and detail endpoints never load that table, and sensitive variables are left out of their `variables` too. An explicit,
owner-only reveal is #19's job; it reads `RequestSensitiveOutputs` directly.

**Runner variables.** `RunnerVariables.For(request)` gives the runner the variables it forces on every module:
`rp_name_suffix`, the request ID's first 8 hex digits, and `rp_tags`, which are `resource-provisioner`,
`rp-<requestId>` and `rp-owner-<short-sub>` (the first 8 lowercase letters or digits of the owner's `sub`).

### Ownership

| | Owner | Another user | Admin (`rp-admin`), someone else's request |
| --- | --- | --- | --- |
| `GET /api/requests` | their own | their own | every user's, with `ownerId` and `ownerEmail` |
| `GET /api/requests/{id}`, `GET .../logs` | 200 | 404 | 200 |
| `POST .../confirm`, `POST .../cancel` | allowed | 404 | 403 |

Admins can see everything but have no extra power over other users' cloud resources. A request someone can't see
answers 404, so its existence isn't revealed.

**Droplet references.** A variable named `droplet_request_ids` or `<purpose>_droplet_request_ids` (see
[catalog.md](catalog.md#variables)) lists droplets by request ID. At propose and again at confirm, every ID must be a
`droplet` request **owned by the requesting user** that is `Succeeded`, admins included. A missing ID and another
user's ID get the same error, so the check doesn't reveal other users' request IDs; the user's own droplet that isn't
provisioned yet gets an error naming its status.

## Spend caps

Checked when the owner confirms, inside one lock so two confirms can't both take the last slot (the POC runs one API
instance; several would need a database lock instead).

| Key | Default | What it caps |
| --- | --- | --- |
| `Limits:MaxActiveRequestsPerUser` | `3` | one user's active requests |
| `Limits:MaxActiveRequestsTotal` | `10` | every user's active requests together |

**Active** means `Queued`, `Applying`, `Succeeded` or `Destroying`: confirmed, and the resource exists or is about to.
`AwaitingConfirmation` costs nothing, and `Cancelled` and `Destroyed` are finished. `Failed` doesn't count either, so a
failure can't lock a user out; a failed apply may still have left something behind, which the user destroys from the
request (#16). Over a limit, confirm answers **409** with the title `Active request limit reached` and a `detail`
saying which limit and what to do; the request stays `AwaitingConfirmation`, so it can be confirmed once something is
destroyed.

The chat rate limit (`Limits:ChatMessagesPerMinute`) is #17's.
