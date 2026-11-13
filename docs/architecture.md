# Architecture

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
                                                                   scratch workspace)
```

| Component | Role |
| --- | --- |
| `web/` | Chat page, requests list and detail, admin Users page. A `/api/proxy/[...path]` route attaches the session's access token (and passes SSE through), so the browser never holds the API token. |
| `api/` | Auth and policies, invitations, catalog, the Claude agent, provisioning requests and the Terraform runner. |
| `catalog/<type>/` | `main.tf`, `variables.tf`, `outputs.tf` and a `resource.json` manifest for each supported resource type. |
| `keycloak/` | `realm-export.json` for the `resource-provisioner` realm, imported on first start. |
| `infra/` | Terraform for the droplet that hosts the POC, with its own state, separate from the catalog ([deployment.md](deployment.md)). |
| `deploy/` | The host's Caddyfile and scripts (`init-env.sh`, `deploy.sh`, `keycloak-admin.sh`), used with `docker-compose.prod.yml`. |
| `workspaces/<requestId>/` | A per-request copy of the module and its tfvars. State is in the app database ([provisioning.md](provisioning.md#workspaces-and-state)). Runtime only, gitignored. |

## API layout

```
api/
├── ResourceProvisioner.slnx
├── Directory.Build.props        # net10.0, nullable, implicit usings for every project
├── Directory.Packages.props     # Central Package Management: every NuGet version lives here
├── .config/dotnet-tools.json    # dotnet-ef, pinned to the EF Core version
├── src/ResourceProvisioner.Api/
│   ├── Program.cs               # host setup: options, auth, invitations, DbContext, catalog, requests, runner,
│   │                            # chat agent and its rate limiter, controllers, /health
│   ├── Auth/                    # roles, policies, realm roles mapping, ICurrentUser
│   ├── Config/                  # typed options, one class per config section
│   ├── Controllers/             # API controllers, one per resource (MeController: GET /api/me,
│   │                            # CatalogController: GET /api/catalog, InvitationsController: /api/invitations,
│   │                            # RequestsController: /api/requests, ConversationsController: /api/conversations)
│   ├── Data/                    # AppDbContext, design-time factory, Migrations/
│   ├── Extensions/              # service registration helpers
│   ├── Models/                  # request and response types, the Invitation, ProvisioningRequest, JobLogLine,
│   │                            # Conversation and ConversationMessage entities
│   ├── ResourceCatalog/         # manifest types, loader and checks, ICatalogService (see catalog.md)
│   └── Services/                # Keycloak/ (IKeycloakUserAdmin over the admin API), Invitations/ (see auth-setup.md),
│                                # Requests/ (IProvisioningRequestService, the state machine, RunnerVariables, logs),
│                                # Provisioning/ (the Terraform runner, see provisioning.md),
│                                # Terraform/ (ITerraformProcessRunner, the only thing that starts the CLI),
│                                # Agent/ (the chat agent: IChatModel over Anthropic, tools, loop; see agent.md)
└── tests/ResourceProvisioner.Api.Tests/
    └── Fixtures/                # ApiFactory (WebApplicationFactory on its own migrated Postgres database, dropped
                                 # with it; runner off), TestAuthHandler,
                                 # TestAdminController (test-only admin endpoint), InMemoryKeycloakUserAdmin,
                                 # FakeTerraform, FakeChatModel (scripted Claude), Sse (parses event streams),
                                 # TempCatalog, RequestSeeding (requests in any status),
                                 # catalog/ (fixture resource types, not real modules)
```

Run it from `api/`:

```bash
dotnet tool restore                                   # installs dotnet-ef
dotnet run --project src/ResourceProvisioner.Api      # http://localhost:5000/health
docker compose up -d postgres-app                     # the tests need Postgres
dotnet test
dotnet ef database update --project src/ResourceProvisioner.Api
```

Integration tests run on Postgres, the production provider. Each `ApiFactory` creates its own database,
`rp_test_<guid>`, builds it with the real migrations (`Database.Migrate()`) and drops it when disposed, so test
classes stay isolated and run in parallel. The server comes from the `RP_TEST_POSTGRES` environment variable, an Npgsql
connection string whose `Database` is the one connected to while creating and dropping test databases; it defaults to
the compose `postgres-app` (`Host=localhost;Port=5432;Database=postgres;Username=rp;Password=dev_password`). CI runs a
`postgres:16` service container for the `api` job.

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
| `Keycloak:RequireHttpsMetadata` | unset: required except in Development (`false` when Keycloak is reached over a private network by HTTP, as in Docker Compose) | no |
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
   conversations and requests; `rp-admin` sees everything, manages invitations and can destroy anyone's request
   (to clean up spend), but confirms, cancels and references only their own.
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
Queued               → Failed                (the runner: something it depends on won't succeed)
Succeeded | Failed   → Destroying → Destroyed | Failed
AwaitingConfirmation → Cancelled
```

A provisioning request (`ProvisioningRequest`, table `ProvisioningRequests`) holds the owner's `sub` (and their email
at the time, so admins can tell whose it is), the type, the validated variables with defaults applied (JSON), the
tfvars its apply used (JSON, saved by the runner for destroy, never returned by an endpoint), the status, the
non-sensitive outputs (JSON), the conversation it came from and when it was created, last changed and
confirmed. Its ID is a GUID, always rendered lowercase (`3f2b8c1e-5d4a-...`): that's the form the `rp-<requestId>`
tag and the manifests' droplet-reference patterns use.

| Status | Meaning | Moved there by |
| --- | --- | --- |
| `AwaitingConfirmation` | Proposed; nothing has happened | the agent's `propose_request` tool ([agent.md](agent.md)) |
| `Queued` | Confirmed, waiting for the runner (and for anything it depends on) | the owner's Confirm (`POST /api/requests/{id}/confirm`, or several at once with `POST /api/requests/confirm`) |
| `Applying` | `terraform apply` is running | the runner ([provisioning.md](provisioning.md)) |
| `Succeeded` | The resource exists; outputs are stored | the runner |
| `Failed` | Apply or destroy failed, so resources may be left behind; or a request it depended on failed or went away, so it never ran | the runner |
| `Destroying` | Waiting for, or running, `terraform destroy` | the owner's or an admin's destroy (`POST /api/requests/{id}/destroy`) |
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
reference (see [below](#dependencies)), and creates the request in `AwaitingConfirmation` with its dependencies, or
returns the errors by field path for the agent to fix. There is no HTTP endpoint for proposing: only the agent creates requests.

**Confirming** checks again, because time passes between propose and confirm: the variables and references are
re-validated (a referenced request may have been cancelled or destroyed meanwhile; the API answers 422 with the
errors), then the [spend caps](#spend-caps), then the request moves to `Queued` and onto the runner's queue. A request
that depends on one still awaiting confirmation can't be confirmed on its own (409 naming it). **Confirm all**
(`POST /api/requests/confirm`, `{"requestIds": [...]}`, up to 20) confirms a set together: every one is checked, the
spend caps are checked for the whole set, and they're queued in dependency order, all or none; with several, each
422 error's path starts with its request ID. Running Terraform is the
runner's job: see [provisioning.md](provisioning.md).

**Outputs.** Non-sensitive outputs go in `ProvisioningRequests.Outputs`. Sensitive ones (`"sensitive": true` in the
manifest: passwords, connection URIs) go in their own table, `RequestSensitiveOutputs`, keyed by request ID. The list
and detail endpoints never load that table, and sensitive variables are left out of their `variables` too. The only
way to read them is the owner's explicit reveal, `GET /api/requests/{id}/sensitive-outputs`, which answers with the
values by name (`{}` if there are none) and `Cache-Control: no-store`. The request page calls it only when the owner
clicks **Reveal secrets**.

**Runner variables.** `RunnerVariables.For(request)` gives the runner the variables it forces on every module:
`rp_name_suffix`, the request ID's first 8 hex digits, and `rp_tags`, which are `resource-provisioner`,
`rp-<requestId>` and `rp-owner-<short-sub>` (the first 8 lowercase letters or digits of the owner's `sub`).

### Ownership

| | Owner | Another user | Admin (`rp-admin`), someone else's request |
| --- | --- | --- | --- |
| `GET /api/requests` | their own | their own | every user's, with `ownerId` and `ownerEmail` |
| `GET /api/requests/{id}`, `GET .../logs` | 200 | 404 | 200 |
| `POST .../confirm`, `POST .../cancel`, `POST /api/requests/confirm` | allowed | 404 | 403 |
| `POST .../destroy` | allowed | 404 | allowed |
| `GET .../sensitive-outputs` | allowed | 404 | 403 |

Admins can see everything, and their one extra power over other users' cloud resources is taking them down: an admin
can destroy a resource someone left running, but can't create, confirm or reference anything in their name. A request
someone can't see answers 404, so its existence isn't revealed.

### Dependencies

A variable marked `"references": "<type>"` in its manifest (see [catalog.md](catalog.md#variables)) holds request IDs
of that type, such as a firewall's `droplet_request_ids`. At propose and again at confirm, every ID must be a request
of that type **owned by the requesting user**, admins included, that is pending (`AwaitingConfirmation`, `Queued`,
`Applying`) or `Succeeded`. A missing ID and another user's ID get the same error, so the check doesn't reveal other
users' request IDs; the user's own request that's failed, cancelled, being destroyed or destroyed gets an error naming
its status.

Each referenced request becomes a **dependency** (`RequestDependencies`: request → the request it depends on). A
request only references earlier ones, so there are no cycles. Dependencies mean:

- **Provisioned in order.** A queued request waits until everything it depends on has `Succeeded`; the runner queues
  it again whenever one of them finishes. If one fails, is cancelled, or is being or has been destroyed, it moves
  `Queued → Failed` without running ([provisioning.md](provisioning.md#dependencies)).
- **Confirmed in order.** See **Confirming** above.
- **Destroyed in reverse.** A request can't be destroyed (409, naming them) while something that depends on it is
  `Queued`, `Applying`, `Succeeded`, `Failed` (it may have created something) or `Destroying`.
- **Shown both ways.** Every request response has `dependsOn` and `dependents`: `{id, type, displayName, name,
  status}` for each.

## Spend caps

Checked when the owner confirms (for every request in a Confirm all together), inside one lock so two confirms can't
both take the last slot (the POC runs one API
instance; several would need a database lock instead).

| Key | Default | What it caps |
| --- | --- | --- |
| `Limits:MaxActiveRequestsPerUser` | `3` | one user's active requests |
| `Limits:MaxActiveRequestsTotal` | `10` | every user's active requests together |

**Active** means `Queued`, `Applying`, `Succeeded` or `Destroying`: confirmed, and the resource exists or is about to.
Free types (`"free": true` in the manifest: projects, firewalls) never count, whether active or being confirmed.
`AwaitingConfirmation` costs nothing, and `Cancelled` and `Destroyed` are finished. `Failed` doesn't count either, so a
failure can't lock a user out; a failed apply may still have left something behind, which the user destroys from the
request. Over a limit, confirm answers **409** with the title `Active request limit reached` and a `detail`
saying which limit and what to do; the request stays `AwaitingConfirmation`, so it can be confirmed once something is
destroyed.

The chat rate limit (`Limits:ChatMessagesPerMinute`, per user) is described in [agent.md](agent.md#rate-limit).
