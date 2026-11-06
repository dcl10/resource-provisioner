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
