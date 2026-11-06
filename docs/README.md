# Docs

One file per topic. Each file is created by the issue that builds that part of the app; links marked *(planned)* don't
exist yet.

| Doc | What it covers |
| --- | --- |
| [architecture.md](architecture.md) | Components, key design rules, request states, spend caps |
| auth-setup.md *(planned, #5, #8)* | Keycloak realm import, clients, bootstrapping the first admin, the invite flow |
| catalog.md *(planned, #9–#13)* | The `resource.json` manifest format, adding a resource type, the current types |
| agent.md *(planned, #17)* | System prompt intent, agent tools, rate limits, model and effort config |
| provisioning.md *(planned, #15, #16)* | The Terraform runner, workspaces and state, logs, destroy |
| local-development.md *(planned, #20)* | Running the full stack locally, secrets, end-to-end checklist |
| deployment.md *(planned, #21)* | Hosting on a droplet, Caddy, Keycloak production mode, SMTP, admin access |

When you add a doc, replace its *(planned)* entry with a link.
