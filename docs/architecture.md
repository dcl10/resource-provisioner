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
| `web/` | Chat page, projects list and pages, requests list and detail, admin Users page. A `/api/proxy/[...path]` route attaches the session's access token (and passes SSE through), so the browser never holds the API token. |
| `api/` | Auth and policies, invitations, catalog, the Claude agent, projects, provisioning requests and the Terraform runner. |
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
│   │                            # RequestsController: /api/requests, ProjectsController: /api/projects,
│   │                            # ConversationsController: /api/conversations)
│   ├── Data/                    # AppDbContext, design-time factory, Migrations/
│   ├── Extensions/              # service registration helpers
│   ├── Models/                  # request and response types, the Invitation, ProvisioningRequest, Project,
│   │                            # JobLogLine, Conversation and ConversationMessage entities
│   ├── ResourceCatalog/         # manifest types, loader and checks, ICatalogService (see catalog.md)
│   └── Services/                # Keycloak/ (IKeycloakUserAdmin over the admin API), Invitations/ (see auth-setup.md),
│                                # Requests/ (IProvisioningRequestService, the state machine, IRequestDeleter,
│                                # RequestGraph (dependencies both ways), RunnerVariables, logs),
│                                # Projects/ (IProjectService),
│                                # Provisioning/ (the Terraform runner, see provisioning.md),
│                                # Terraform/ (ITerraformProcessRunner, the only thing that starts the CLI),
│                                # Agent/ (the chat agent: IChatModel over Anthropic, tools, loop; see agent.md)
└── tests/ResourceProvisioner.Api.Tests/
    └── Fixtures/                # ApiFactory (WebApplicationFactory on its own migrated Postgres database, dropped
                                 # with it; runner off), TestAuthHandler,
                                 # TestAdminController (test-only admin endpoint), InMemoryKeycloakUserAdmin,
                                 # FakeTerraform, FakeChatModel (scripted Claude), Sse (parses event streams),
                                 # TempCatalog, RequestSeeding (requests and projects in any status; a request
                                 # goes in its owner's "default" project unless told otherwise),
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
   `POST /api/requests/{id}/confirm`, or submits the New project form (`POST /api/projects`), where the submit is
   the confirmation.
2. **Each `resource.json` manifest is the single source of truth.** Tool schemas, server-side validation and the
   confirmation summary are all generated from it.
3. **The agent has four tools:** `list_resource_types`, `get_resource_spec`, `list_my_resources` and
   `propose_request`.
4. **Every endpoint requires sign-in.** Sign-in is invite-only through Keycloak. Users see only their own
   conversations, projects and requests; `rp-admin` sees everything, manages invitations and can destroy anyone's request
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
Succeeded | Failed | DestroyFailed → Destroying → (deleted) | DestroyFailed
Destroying           → its StatusBeforeDestroy (the runner: something that depends on it failed to destroy)
AwaitingConfirmation → (deleted)             (the owner cancels, or a destroy cascades to it)
```

There is no final status. A request that's finished with, because the owner cancelled it before confirming or because
the runner has destroyed what it created, is **deleted**, with everything stored for it (see
[Deleting a request](#deleting-a-request)). From then on it answers 404 like any request that never existed, and it's
left out of every list.

A provisioning request (`ProvisioningRequest`, table `ProvisioningRequests`) holds the owner's `sub` (and their email
at the time, so admins can tell whose it is), the type, the validated variables with defaults applied (JSON), the
tfvars its apply used (JSON, saved by the runner for destroy, never returned by an endpoint), the status, the
non-sensitive outputs (JSON), the [project](#projects) it belongs to, the conversation it came from and when it was
created, last changed and confirmed. Its ID is a GUID, always rendered lowercase (`3f2b8c1e-5d4a-...`): that's the form the `rp-<requestId>`
tag and the manifests' droplet-reference patterns use.

| Status | Meaning | Moved there by |
| --- | --- | --- |
| `AwaitingConfirmation` | Proposed; nothing has happened | the agent's `propose_request` tool ([agent.md](agent.md)) |
| `Queued` | Confirmed, waiting for the runner (and for anything it depends on) | the owner's Confirm (`POST /api/requests/{id}/confirm`, or several at once with `POST /api/requests/confirm`) |
| `Applying` | `terraform apply` is running | the runner ([provisioning.md](provisioning.md)) |
| `Succeeded` | The resource exists; outputs are stored | the runner |
| `Failed` | Apply failed, so resources may be left behind; or a request it depended on failed or went away, so it never ran | the runner |
| `Destroying` | Waiting for what depends on it to be destroyed, or running `terraform destroy`; once that succeeds, the runner deletes the request | the owner's or an admin's destroy (`POST /api/requests/{id}/destroy`, or `DELETE /api/projects/{id}`), of it or of something it depends on |
| `DestroyFailed` | `terraform destroy` failed or couldn't run, so resources may still exist; everything is kept, its Terraform state above all | the runner |

Every status change goes through `RequestStateMachine.Move`, which refuses any move not drawn above (the API answers
409). `Failed → Destroying` is allowed because a failed apply can leave part of a module created, and
`DestroyFailed → Destroying` because a failed destroy certainly does, so either can always be destroyed (again).
Moving to `Destroying` records the status the request had in `StatusBeforeDestroy`; `RequestStateMachine.RevertDestroy`
moves it back there when its destroy stops before Terraform has touched it (see [Dependencies](#dependencies)), and
records the request whose failed destroy stopped it in `DestroyStoppedById`. A request that was already `Destroying`
when `StatusBeforeDestroy` was added (the `AddDestroyCascade` migration) has none, and goes back to `Failed`. `Queued`
can't be cancelled: the runner may already have picked it up. `Status` is also an EF Core concurrency token, so if two changes race (a confirm and a
cancel, or the runner), the second fails with 409 instead of overwriting the first; a delete only goes ahead while the
request is still in the status it expects.

### Deleting a request

`IRequestDeleter` deletes a request in two cases, each in one database transaction:

- **Cancel** (`POST /api/requests/{id}/cancel`, owner only, from `AwaitingConfirmation`): nothing was created, so the
  request is deleted and the API answers **204**.
- **Destroy finished**: once `terraform destroy` succeeds, or there was no Terraform state to destroy, the runner
  deletes the request ([provisioning.md](provisioning.md#destroying)). A destroy that fails deletes nothing: the
  request moves to `DestroyFailed` and keeps its outputs, log and, above all, its Terraform state, so it can be
  destroyed again.

A destroy that cascades to proposals cancels them, which deletes them as above, in the destroy's own transaction:
`IRequestDeleter` joins a transaction the caller has already opened.

The transaction covers the request and its sensitive outputs, its job log, its dependencies in both directions, the
[project](#projects) it backs (if it's a `project` request) and, after a destroy, its Terraform state (a row in the
same database's `terraform_state` schema, deleted through `ITerraformStateStore`). State is deleted only after a
successful destroy, never for a request whose resources may still exist. Requests still awaiting confirmation that
depend on it by then (proposed after its destroy began) are kept, but lose the link: confirming one then fails its
reference or project check (422), and a member of a deleted project is left in no project. Anything else that depends
on it (which the runner waits for) stops the delete.

Nothing else is kept: the only trace is a line in the API's own log, `Destroyed and deleted request <id>` (or
`Cancelled and deleted request <id>`). Spend caps are unaffected, since they only count active requests. The
`DeleteFinishedRequests` migration deleted the requests left `Cancelled` or `Destroyed` from before, the same way.

**Proposing.** `IProvisioningRequestService.ProposeAsync(owner, type, variables, projectId, conversationId)` is what
`propose_request` and `POST /api/projects` call. It validates the variables with `ICatalogService.ValidateVariables`
(which includes the manifest's `atLeastOneOf` rules, see [catalog.md](catalog.md#rules-across-variables)), then
checks the [project](#projects) and every reference (see [below](#dependencies)), and creates the request in
`AwaitingConfirmation` with its dependencies (and, for a `project` request, its project), or returns the errors by
field path for the agent to fix. There is no HTTP endpoint for proposing other types: only the agent creates them.

**Confirming** checks again, because time passes between propose and confirm: the variables, project and references
are re-validated (a project or referenced request may have been cancelled or destroyed, and so deleted, meanwhile; the
API answers 422 with the errors), then the [spend caps](#spend-caps), then the request moves to `Queued` and onto the runner's queue. A request
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
| `POST .../destroy`, `GET .../destroy-plan` | allowed | 404 | allowed |
| `GET .../sensitive-outputs` | allowed | 404 | 403 |
| `GET /api/projects` | their own | their own | every user's, with `ownerId` and `ownerEmail` |
| `GET /api/projects/{id}` | 200 | 404 | 200 |
| `DELETE /api/projects/{id}` | allowed | 404 | allowed |
| Putting a request in a project | allowed | refused, as if it didn't exist | refused, as if it didn't exist |

`POST /api/projects` always creates the caller's own project, admins included.

Admins can see everything, and their one extra power over other users' cloud resources is taking them down: an admin
can destroy a resource someone left running (and with it what depends on it, cancelling the owner's proposals among
them) or delete their project, but can't create, confirm or reference anything in their name. A request
someone can't see answers 404, so its existence isn't revealed.

### Dependencies

A variable marked `"references": "<type>"` in its manifest (see [catalog.md](catalog.md#variables)) holds request IDs
of that type, such as a firewall's `droplet_request_ids`. At propose and again at confirm, every ID must be a request
of that type **owned by the requesting user**, admins included, that is pending (`AwaitingConfirmation`, `Queued`,
`Applying`) or `Succeeded`. A missing ID (including one that was cancelled or destroyed, and so deleted) and another
user's ID get the same error, so the check doesn't reveal other users' request IDs; the user's own request that's
failed or being destroyed gets an error naming its status.

Each referenced request becomes a **dependency** (`RequestDependencies`: request → the request it depends on), and so
does the `project` request backing the request's [project](#projects), recorded automatically at propose. A request
only references earlier ones, so there are no cycles. Dependencies mean:

- **Provisioned in order.** A queued request waits until everything it depends on has `Succeeded`; the runner queues
  it again whenever one of them finishes. If one fails, is being destroyed, or no longer exists, it moves
  `Queued → Failed` without running ([provisioning.md](provisioning.md#dependencies)).
- **Confirmed in order.** See **Confirming** above.
- **Destroyed in reverse, as a cascade.** Destroying a request destroys everything that depends on it, directly or
  not (found through `RequestDependencies` and project membership, by `RequestGraph`):
  - dependents awaiting confirmation are cancelled, and so deleted;
  - if any is `Queued`, `Applying` or `Destroying`, the destroy is refused (409, `Wait for <x> to finish, then try
    again: it depends on this request.`) and nothing changes;
  - otherwise the request and its `Succeeded`, `Failed` and `DestroyFailed` dependents all move to `Destroying` in one
    transaction, each recording its `StatusBeforeDestroy`, and are queued.

  The runner destroys a `Destroying` request only once nothing that may exist depends on it any more, so the tree is
  torn down from the top ([provisioning.md](provisioning.md#destroying)). If one of them fails to destroy, it becomes
  `DestroyFailed`, and everything that was waiting for it (what it depends on, in the same cascade) goes back to its
  `StatusBeforeDestroy`, untouched, with the log line `Destroy stopped: <type> "<name>" (<id>) failed to destroy.` and
  `destroyStoppedBy` in its response. Destroying the request again (or the `DestroyFailed` one) resumes: what was
  already destroyed is gone, and the rest go round again. `DestroyFailed`, like `Failed`, doesn't count towards the
  [spend caps](#spend-caps).
- **Planned before.** `GET /api/requests/{id}/destroy-plan` answers what a destroy would do, changing nothing:
  `{canDestroy, reason, destroy, cancel, waitFor}`, where `destroy` lists what would be destroyed in teardown order
  (each before anything it depends on; the request itself last), `cancel` the proposals that would be deleted,
  `waitFor` what's in the runner's hands, and `reason` why the destroy would be refused (the 409's detail), or null.
  The lists are request links. The destroy works the plan out again, so it acts on what's there by then.
- **Shown both ways.** Every request response has `dependsOn` and `dependents`: `{id, type, displayName, name,
  status}` for each. A request whose destroy stopped also has `destroyStoppedBy`, the request that failed to destroy,
  while it still exists (otherwise null).

## Projects

A project is the top level that holds a user's resources together. **Every request belongs to exactly one project**,
except the `project` request that backs it: a project is always backed by a request of the catalog's `project` type,
so its DigitalOcean project, status, logs, Terraform state and outputs come from the existing runner like any other
request's.

A `Project` (table `Projects`) holds the owner's `sub` and email, the name, description, purpose and environment
(copied from its request's variables), when it was created, and `ProjectRequestId`, the request that backs it.
`ProvisioningRequest.ProjectId` is the project a request belongs to: required for every type but `project`, and null
on the `project` request itself. The service enforces that, not the database, so a request from before projects
(which has none) can still be read and destroyed, but not confirmed. The `AddProjects` migration gave each existing
`project` request that wasn't cancelled or destroyed its project, and moved the requests that named one through the
old `project_request_id` variable into it.

**Creating one.** A project's row is created with its request, whenever a `project` request is proposed:

- **in chat:** the agent proposes a `project` request, and the user confirms it on the card like any other;
- **from the UI:** `POST /api/projects` with `{"variables": {"name": "shop", ...}}` proposes the request and confirms
  it in one step, because the form's submit *is* the confirmation. The variables are checked against the `project`
  manifest (422 with `errors` by field), and the confirm makes the usual checks, spend caps included (projects are
  free, so they never hit them). If the confirm is refused, the request and project are deleted, so nothing is left
  awaiting a confirmation that already happened. It answers 201 with the project.

**Joining one.** Proposing any other type takes a project ID (reported as `project_id` in errors), which must be one of
the user's own projects, admins included, whose request is pending or provisioned, under the same rules as
[references](#dependencies): someone else's project and a missing one get the same error. A `RequestDependency` on the
project's request is recorded automatically, so the project is provisioned first, and confirm refuses a member on its
own while its project still awaits confirmation. A `project` request takes no project.

**Status.** A project's status is its request's (`AwaitingConfirmation` through `Succeeded`, `Failed`,
`Destroying`, `DestroyFailed`). When the request is cancelled or destroyed, the project is deleted with it: it's no
longer in `GET /api/projects`, `GET /api/projects/{id}` answers 404, and nothing more can be put in it. Proposals
proposed into it after its destroy began are left in no project, so they can't be confirmed.

**Deleting one.** Every request in a project depends on the project's request, so `DELETE /api/projects/{id}` is a
cascading destroy of that request ([Dependencies](#dependencies)): the proposals in it are cancelled, everything else in
it is destroyed first, and the project goes once its request is destroyed. It answers **202** with the project (now
`Destroying`), or 409 as a destroy would. Its plan is the project request's,
`GET /api/requests/{projectRequestId}/destroy-plan`. A project still awaiting confirmation is cancelled through its
request instead.

**Endpoints** (ownership as in [the table above](#ownership)):

| Endpoint | What it does |
| --- | --- |
| `GET /api/projects` | The user's projects (every user's for an admin), newest first: `{id, ownerId, ownerEmail, name, description, purpose, environment, status, projectRequestId, resourceCount, createdAt}`. |
| `GET /api/projects/{id}` | The same, plus `request` (the `project` request) and `resources` (every request in it, newest first, each with `dependsOn` and `dependents`). |
| `POST /api/projects` | Creates a project from the form, as above. |
| `DELETE /api/projects/{id}` | Destroys the project and everything in it, as above. |
| `GET /api/requests?projectId=<id>` | The requests in a project and the one that backs it, within what the user can see. |

Every request response carries `project: {id, name}`: the project it belongs to, or for a `project` request, the one
it backs (null only for a request from before projects).

**In Terraform.** The runner passes the project's DigitalOcean ID (its request's `id` output) as `rp_project_id` to
every module that declares it, so droplets, Postgres clusters and Spaces buckets go in the DigitalOcean project
([catalog.md](catalog.md#variables)). Firewalls can't go in a DigitalOcean project, so a firewall belongs to its app
project only.

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
`AwaitingConfirmation` costs nothing, and cancelled and destroyed requests are deleted. `Failed` and `DestroyFailed`
don't count either, so a failure can't lock a user out; a failed apply or destroy may still have left something
behind, which the user destroys (again) from the request. Over a limit, confirm answers **409** with the title `Active request limit reached` and a `detail`
saying which limit and what to do; the request stays `AwaitingConfirmation`, so it can be confirmed once something is
destroyed.

The chat rate limit (`Limits:ChatMessagesPerMinute`, per user) is described in [agent.md](agent.md#rate-limit).
