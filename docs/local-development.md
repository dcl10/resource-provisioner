# Local development

Docker Compose runs the app's dependencies, or the whole app. From the repo root:

| Command | Starts | Use it to |
| --- | --- | --- |
| `docker compose up -d` | Postgres ×2, Keycloak, MailHog | work on the code: run the API (`dotnet run`) and web app (`npm run dev`) yourself ([web.md](web.md#running-it), [architecture.md](architecture.md#api-layout)) |
| `docker compose --profile app up -d --build` | all of the above, plus `migrate`, `api` and `web` | run the whole app as it will be deployed, and try it end to end |

The `app` profile keeps the second set out of the first, so the dev setup in the other docs doesn't change.

## Running the whole app

You need Docker. To actually provision something you also need a DigitalOcean token and an Anthropic API key. Without
them the app runs, but the agent replies with an error and confirmed requests fail.

1. `cp .env.example .env`, then set `AUTH_SECRET` (`openssl rand -base64 33`). `.env` is gitignored; Compose reads it
   by itself.
2. `docker compose up -d` and wait for Keycloak (`http://localhost:8080`). On first start it imports the realm and
   generates the two client secrets.
3. Copy the secrets into `.env`: in the admin console (`admin` / `admin`), realm **resource-provisioner** → **Clients**
   → `rp-web` → **Credentials** → `KEYCLOAK_WEB_CLIENT_SECRET`, and `rp-api` → `KEYCLOAK_API_CLIENT_SECRET`
   ([auth-setup.md](auth-setup.md#client-secrets)).
4. Add `ANTHROPIC_API_KEY` and `DIGITALOCEAN_TOKEN` (and the Spaces keys if you want buckets).
5. `docker compose --profile app up -d --build`. The `migrate` service applies the database migrations and exits,
   then the API and web app start.
6. Create the first admin by hand ([auth-setup.md](auth-setup.md#bootstrapping-the-first-admin)) and sign in at
   `http://localhost:3000`.

| Service | URL | |
| --- | --- | --- |
| Web app | `http://localhost:3000` | |
| API | `http://localhost:5080` | `/health`; only the web app needs it. Not 5000, which macOS's AirPlay receiver often holds. |
| Keycloak | `http://localhost:8080` | admin console at `/admin` |
| MailHog | `http://localhost:8025` | every email Keycloak sends, such as invitations |
| Postgres | `localhost:5432` (app), `localhost:5433` (Keycloak) | user `rp` / `dev_password` for the app's |

After changing code, `docker compose --profile app up -d --build` rebuilds and restarts what changed.
`docker compose --profile app logs -f api web` follows the logs. `docker compose --profile app down` stops everything
and keeps the data.

### Settings

Every setting and secret is in [.env.example](../.env.example), with what it's for. Compose maps them onto the
services' own names (`Anthropic__ApiKey`, `AUTH_KEYCLOAK_SECRET`...), and sets the addresses between containers
itself. The API's settings are described in [architecture.md](architecture.md#configuration).

### Data

| Volume | Holds |
| --- | --- |
| `postgres-app-data` | users' conversations and requests, and **Terraform state for every request** (schema `terraform_state`) |
| `postgres-keycloak-data` | users, invitations, the realm and its client secrets |

Don't delete `postgres-app-data` (`docker compose down -v`) while anything is provisioned. Without the state the app
can't destroy those resources, and they keep costing money until someone deletes them in DigitalOcean (everything is
tagged `resource-provisioner` and `rp-<requestId>`). Destroy them from the app first.

## How the containers reach Keycloak

The browser reaches Keycloak at `localhost:8080`, but inside the Compose network the web and API containers reach it
at `keycloak:8080`. Tokens name one issuer, so three settings make both work:

- **Keycloak** has `KC_HOSTNAME=http://localhost:8080` and `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`. Every token's
  issuer, and every URL a browser follows (login, logout, the link in an invitation email), is `localhost:8080`. The
  back-channel endpoints (token, signing keys, admin API) answer on whichever host the request used, so a container
  asking `keycloak:8080` is told to use `keycloak:8080`.
- **The web app** has `AUTH_KEYCLOAK_ISSUER` set to the public issuer and `AUTH_KEYCLOAK_INTERNAL_ISSUER` set to the
  same realm at `keycloak:8080`. Its server-side calls to Keycloak (discovery, the code exchange, token refresh) go to
  the internal address (`lib/auth/keycloak-url.ts`). Unset, as under `npm run dev`, nothing is rewritten.
- **The API** has `Keycloak__AuthServerUrl=http://keycloak:8080`, so it fetches the discovery document (which names the
  public issuer) and signing keys internally. `Keycloak__RequireHttpsMetadata=false` allows that over plain HTTP; the
  API still runs as Production.

Outside Docker everything is `localhost:8080`, so these settings change nothing there.

## The images

| Image | Built from | |
| --- | --- | --- |
| `api` | `api/Dockerfile` (context: the repo root) | ASP.NET Core runtime, the published API, the `catalog/` modules and a pinned Terraform CLI. Runs as the non-root `app` user; Terraform's scratch workspaces go in `/data/workspaces`, inside the container, with no volume, since state is in the app database. |
| `migrate` | `api/Dockerfile`, target `migrate` | An EF Core migrations bundle that applies pending migrations to `ConnectionStrings__DefaultConnection` and exits. The API never migrates at startup. |
| `web` | `web/Dockerfile` | Next.js's standalone server on Node 22, as the `node` user. Every setting is read at runtime. |

**Terraform** is pinned in `api/Dockerfile` (`TERRAFORM_VERSION`) and checked against the SHA-256 sums HashiCorp
publishes, for `amd64` and `arm64`. To upgrade it, change the version and both sums, from
`https://releases.hashicorp.com/terraform/<version>/terraform_<version>_SHA256SUMS`, and `terraform_version` in
`.github/workflows/ci.yml`, so CI validates the modules with the same version the app runs.

## End-to-end checklist

This costs a few cents: it creates a real droplet and firewall in DigitalOcean for a few minutes. Run it after a change
that touches sign-in, the agent, the runner or the images.

1. **Admin:** bootstrap the first admin ([auth-setup.md](auth-setup.md#bootstrapping-the-first-admin)) and sign in at
   `http://localhost:3000`. The header shows **Users**.
2. **Invite:** on **Users**, invite a second address. It's listed as **Pending**, and the email appears in MailHog
   (`http://localhost:8025`).
3. **Accept:** in a private window, open the email's link, set a password, and sign in at `http://localhost:3000`. No
   **Users** link for them.
4. **Ask for a droplet:** as that user, on **Chat**, start a conversation and ask for a small droplet. The agent asks
   for what it needs and shows a proposal card with the settings and the cost.
5. **Confirm:** click **Confirm**. A toast offers **View request**; the request page shows **Queued**, then
   **Provisioning** with the Terraform log streaming, then **Provisioned** with the droplet's IP address.
6. **In DigitalOcean:** the droplet is in the control panel, named `<name>-<8 characters of the request ID>` and
   tagged `resource-provisioner`, `rp-<request ID>` and `rp-owner-...`.
7. **Firewall on its tag:** in the chat, ask for a firewall allowing SSH to that droplet. The agent finds it with
   `list_my_resources` and proposes a firewall targeting its request ID. Confirm it. In DigitalOcean, the firewall
   applies to the droplet through its `rp-<request ID>` tag. While the firewall exists, the droplet's page offers no
   Destroy, only "first destroy Cloud firewall ...".
8. **Secrets (optional; the cost hint on its card says what it's billed at):** droplets and firewalls have no secret outputs. To check
   the reveal, ask for a Spaces bucket or a Postgres database: its request page masks the keys or passwords until the
   owner clicks **Reveal secrets**. Signed in as the admin, that request has no reveal at all. Destroy it afterwards.
9. **Destroy both:** destroy the firewall, then the droplet, from their request pages. Each confirms in a dialog, then
   streams `terraform destroy`; once it's done the request is deleted and the page goes back to the requests list,
   where it's no longer listed. Nothing is left in DigitalOcean, and its chat card now says **No longer exists**.
10. **A sequence in one go:** in a new conversation, ask for a droplet and a firewall allowing SSH to it, together. The
    agent proposes both; the firewall's card says **Runs after** the droplet. Click **Confirm all (2)**: the droplet is
    provisioned first, the firewall's page says it's waiting for it, then provisions. Destroy both again.
11. **A project:** ask for a project and a droplet in it, together. The droplet's card names the **Project** and says
    **Runs after** the project; confirm both. In DigitalOcean the droplet is in the new project, not the default one. The project's page
    lists the droplet under **Needed by** and offers no Destroy until the droplet is destroyed; then destroy the
    project too.
12. **Limits:** optionally, set `LIMITS_MAX_ACTIVE_REQUESTS_PER_USER=1` in `.env`, run
    `docker compose --profile app up -d` again, and confirm two proposals. The second card says you're at your limit.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Sign-in ends on the sign-in page with an error | A client secret in `.env` doesn't match Keycloak's (it changes if the realm is re-imported), or `AUTH_SECRET` is empty. Check `docker compose --profile app logs web`. |
| Every API call is 401 | The API can't validate the token: check `KEYCLOAK_API_CLIENT_SECRET`, and that Keycloak was started from this compose file (it needs `KC_HOSTNAME`). |
| `migrate` exits non-zero | Postgres isn't healthy yet or its data is from another schema. `docker compose --profile app logs migrate` says which. |
| Port 3000, 5080 or 8080 is in use | Stop `npm run dev` / `dotnet run` / another stack first. |
| The agent always replies with an error | `ANTHROPIC_API_KEY` is missing, or rejected (the API's log says which). |
| A confirmed request fails at once | `DIGITALOCEAN_TOKEN` is missing; the request's log says so. |
