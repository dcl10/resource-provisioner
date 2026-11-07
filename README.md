# resource-provisioner

A proof of concept for provisioning infrastructure through chat. You ask a chat bot for a resource (a droplet, a
managed Postgres database, a cloud firewall or a Spaces bucket). An agent backed by Claude works out which resource
type you need and asks for the variables it requires. After you confirm, it provisions the resource with Terraform on
DigitalOcean.

## Why

At work, getting simple, low-risk resources provisioned can take weeks: you search through self-service forms, then
the ticket waits in an outsourced queue. Time-limited projects lose money and reputation while they wait. This POC
tests whether a guarded chat agent over a vetted catalog of Terraform modules can do the same job in minutes.

Sign-in is invite-only, through Keycloak, and per-user limits cap LLM and cloud spend.

## Repo layout

```
/
├── web/        Next.js chat UI (App Router, Auth.js + Keycloak, shadcn/ui)
├── api/        ASP.NET Core 10 API: agent, catalog, Terraform runner, invitations
├── catalog/    One Terraform module + resource.json manifest per resource type
├── keycloak/   Realm export imported on first start
├── infra/      Terraform for the droplet that hosts the POC (separate state)
├── deploy/     Caddyfile and scripts for the host (docker-compose.prod.yml is at the root)
└── docs/       How everything works, one file per topic
```

## Quick start

With Docker, from the repo root:

```bash
cp .env.example .env          # then set AUTH_SECRET: openssl rand -base64 33
docker compose up -d          # Postgres, Keycloak and MailHog; Keycloak generates the client secrets
# copy the rp-web and rp-api client secrets from http://localhost:8080/admin into .env,
# and add ANTHROPIC_API_KEY and DIGITALOCEAN_TOKEN
docker compose --profile app up -d --build
```

Then create the first admin in Keycloak and sign in at http://localhost:3000. Step by step, with an end-to-end
checklist: [docs/local-development.md](docs/local-development.md). To host it for colleagues on a DigitalOcean droplet:
[docs/deployment.md](docs/deployment.md).

## Docs

Start at [docs/README.md](docs/README.md). The design rules are in [docs/architecture.md](docs/architecture.md).

Contributors and coding agents: see [CLAUDE.md](CLAUDE.md) (identical to [AGENTS.md](AGENTS.md)).

## License

[MIT](LICENSE)
