# Docs

One file per topic. Each file is created by the issue that builds that part of the app; links marked *(planned)* don't
exist yet.

| Doc | What it covers |
| --- | --- |
| [architecture.md](architecture.md) | Components, key design rules, request states, spend caps |
| [web.md](web.md) | Running the web app, its layout, using the session and the API proxy, the frontend stack and shadcn conventions |
| [auth-setup.md](auth-setup.md) | Local services, the Keycloak realm and clients, client secrets, how the web app and API handle sign-in, bootstrapping the first admin, the invite flow |
| [catalog.md](catalog.md) | The `resource.json` manifest format, what the loader checks, variable validation, adding a resource type, the current types |
| [agent.md](agent.md) | How a message is answered, the system prompt's intent, the four tools and their strict schemas, the conversation endpoints and events, stored history, refusals and errors, the rate limit, model and effort config |
| [provisioning.md](provisioning.md) | The Terraform runner, applying and destroying, credentials, workspaces and state, the logs endpoint |
| [local-development.md](local-development.md) | Running the dependencies or the whole app with Docker Compose, settings, volumes, how containers reach Keycloak, the images and Terraform pin, the end-to-end checklist |
| deployment.md *(planned, #21)* | Hosting on a droplet, Caddy, Keycloak production mode, SMTP, admin access |

When you add a doc, replace its *(planned)* entry with a link.
