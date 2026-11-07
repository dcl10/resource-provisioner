# Authentication setup

Sign-in goes through Keycloak only, and it's invite-only: self-registration is off, so nobody can create an account and
start spending LLM tokens or DigitalOcean money. You bootstrap the first admin by hand (below); everyone else is
invited from the app.

- [Local services](#local-services)
- [What the realm import creates](#what-the-realm-import-creates)
- [Client settings](#client-settings)
- [Client secrets](#client-secrets)
- [How the web app signs in](#how-the-web-app-signs-in)
- [How the API checks sign-in](#how-the-api-checks-sign-in)
- [Bootstrapping the first admin](#bootstrapping-the-first-admin)
- [Inviting users](#inviting-users)
- [Re-importing the realm](#re-importing-the-realm)

## Local services

From the repo root:

```bash
docker compose up -d
```

| Service | URL / port | Notes |
| --- | --- | --- |
| App Postgres | `localhost:5432` | database `resource_provisioner`, user `rp` / `dev_password` |
| Keycloak Postgres | `localhost:5433` | used by Keycloak only |
| Keycloak | `http://localhost:8080` | admin console at `/admin`, bootstrap admin `admin` / `admin` |
| MailHog | `http://localhost:8025` | catches every email Keycloak sends; SMTP on `1025` |

The bootstrap admin comes from `KC_BOOTSTRAP_ADMIN_USERNAME` and `KC_BOOTSTRAP_ADMIN_PASSWORD`, which default to
`admin` / `admin`. Set them in your shell or a root `.env` file to change them. All of these passwords are local-only
defaults; the deployed setup uses its own ([deployment.md](deployment.md)).

The OIDC issuer is `http://localhost:8080/realms/resource-provisioner`. Keycloak runs with
`KC_HOSTNAME=http://localhost:8080` and `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`, so the issuer stays the same when the
app itself runs in Compose and reaches Keycloak at `keycloak:8080` ([local-development.md](local-development.md#how-the-containers-reach-keycloak)).

## What the realm import creates

On its first start, Keycloak imports [keycloak/realm-export.json](../keycloak/realm-export.json) (`start-dev
--import-realm`). The app's URL and the SMTP settings in it are placeholders with local defaults
(`${APP_URL:http://localhost:3000}`, `${SMTP_HOST:mailhog}`...), which Keycloak fills from its environment at import,
so the deployed realm gets the production values ([deployment.md](deployment.md#what-runs-on-the-host)). It creates
the realm `resource-provisioner` with:

**Realm settings**

- User registration **off**, so accounts only come from invitations or the admin console
- Verify email on, forgot password on, login with email on, duplicate emails not allowed
- Brute-force protection on; passwords need at least 12 characters and can't match the username or email
- SMTP pointing at MailHog (`mailhog:1025`), from `noreply@resource-provisioner.local`

**Realm roles**

| Role | Meaning |
| --- | --- |
| `rp-user` | Can chat and provision resources. A **default role**, so every user gets it when they're created, including invited users; nothing has to assign it. |
| `rp-admin` | Sees every user's conversations and requests, and manages invitations. Assigned by hand. |

**Clients:** `rp-web` and `rp-api`, set up as in [Client settings](#client-settings).

## Client settings

The import sets these, so locally there is nothing to do. Use the tables to check a realm, or to set the clients up by
hand on a Keycloak that doesn't import `realm-export.json`. Create the realm and roles first. The setting names are
the ones in the Keycloak admin console. Replace `http://localhost:3000` with the app's URL when deployed.

**`rp-web`**: confidential client used by the Next.js server (Auth.js) to sign users in.

| Setting | Value |
| --- | --- |
| Client authentication | **On** |
| Authentication flow | **Standard flow** only (Direct access grants, Implicit flow and the rest off) |
| Valid redirect URIs | `http://localhost:3000/*` |
| Valid post logout redirect URIs | `http://localhost:3000/*` (federated sign-out returns here) |
| Web origins | `http://localhost:3000` |
| Advanced → Proof Key for Code Exchange Code Challenge Method | `S256` |

Under **Client scopes → `rp-web-dedicated` → Add mapper → By configuration**, add two mappers:

| Mapper type | Name | Settings |
| --- | --- | --- |
| Audience | `rp-api audience` | Included Client Audience `rp-api`; Add to access token **on**; Add to ID token off |
| User Realm Role | `realm roles` | Token Claim Name `realm_access.roles`; Multivalued on; Add to ID token and Add to access token **on** |

The audience mapper puts `rp-api` in the access token's `aud`, so the API accepts tokens issued to the web app. The
realm roles mapper makes sure the roles are in both tokens. The web app reads `rp-admin` from there to show admin
pages, and the API maps the roles into role claims.

Copy the client secret from the **Credentials** tab into `AUTH_KEYCLOAK_SECRET` (see [Client secrets](#client-secrets)).

**`rp-api`**: confidential client for the ASP.NET Core API. It's the JWT audience, and its service account calls the
Keycloak admin API to send invites.

| Setting | Value |
| --- | --- |
| Client authentication | **On** |
| Authentication flow | **Service accounts roles** only (Standard flow off: the API never signs users in) |
| Service accounts roles tab | **Assign role → Filter by clients**: `realm-management` `manage-users` and `view-users`, nothing else |

Copy the client secret from the **Credentials** tab into the API's `Keycloak:Secret`.

`manage-users` and `view-users` are enough to create users and send them action emails (invites), and nothing more:
the service account can't read or change clients or realm settings. It doesn't need permission to assign roles,
because `rp-user` is a default role.

**Differences from MidlandsHub**, whose client setup this follows:

- MidlandsHub's API client has standard flow on and the `realm-admin` role. Here it has no browser flow and only the
  two user-management roles it needs.
- MidlandsHub seeds its audience and groups mappers into shared client scopes from the API at startup. Here the
  audience and realm roles mappers sit on `rp-web` directly and come from the realm import. There is no groups mapper,
  because access is by role.
- There is no public client yet. MidlandsHub's `midlandshub-public` exists for its Scalar API docs, which this project
  doesn't have.

## Client secrets

Keycloak generates both client secrets during the import; they aren't in the repo. To read one, open the admin
console, switch to the `resource-provisioner` realm, go to **Clients → `<client>` → Credentials**, and copy the
**Client secret**.

| Secret | Where it goes |
| --- | --- |
| `rp-api` | API user-secrets, from `api/`: `dotnet user-secrets --project src/ResourceProvisioner.Api set "Keycloak:Secret" "<secret>"` |
| `rp-web` | `web/.env.local` as `AUTH_KEYCLOAK_SECRET` |

`web/.env.local` also needs the rest of the web app's settings. Copy [web/.env.example](../web/.env.example) and fill
in the two blanks:

| Variable | Value |
| --- | --- |
| `AUTH_SECRET` | Encrypts the session cookie. Generate one with `npx auth secret`. Changing it signs everyone out. |
| `AUTH_KEYCLOAK_ID` | `rp-web` |
| `AUTH_KEYCLOAK_SECRET` | the `rp-web` client secret |
| `AUTH_KEYCLOAK_ISSUER` | `http://localhost:8080/realms/resource-provisioner` |
| `AUTH_URL` | The app's public URL, `http://localhost:3000` locally. `npm run dev` works without it, but a production build (`npm run start`, or deployed) refuses to sign in unless it, or `AUTH_TRUST_HOST=true`, is set. Federated sign-out returns here. |
| `API_URL` | Where the API proxy forwards to, `http://localhost:5000` locally |

`.env*` files are gitignored, apart from `.env.example`.

**To regenerate a secret**, select **Regenerate** on the same Credentials tab, then update user-secrets or
`.env.local` and restart the API or web app. The old secret stops working straight away. Re-importing the realm
(below) also generates new secrets.

## How the web app signs in

The Next.js server signs users in with Auth.js v5 and its Keycloak provider, as the confidential client `rp-web`
(authorization code flow with PKCE `S256` and state). Code is in `web/lib/auth/`; the patterns come from MidlandsHub's
`frontend/lib/auth/`.

- **Session:** an Auth.js JWT session, kept in an encrypted, HTTP-only cookie. It holds Keycloak's access, ID and
  refresh tokens, the access token's expiry and the user's realm roles.
- **Roles:** read from `realm_access.roles` in the access token at sign-in and on every refresh. `rp-admin` makes
  `isAdmin` true, which shows the Users link. Hiding the link is cosmetic: the API checks the role itself.
- **Token refresh:** the `jwt` callback swaps the refresh token for a new access token at Keycloak's token endpoint
  once the access token is within 30 seconds of expiring (Keycloak's default lifetime is 5 minutes). If Keycloak
  refuses, for example because the SSO session ended, the session is marked failed and the next request goes to the
  sign-in page.
- **Every page needs sign-in:** `web/proxy.ts` runs Auth.js's `authorized` callback before every request except
  Auth.js's own endpoints and static files. Signed out, a page redirects to `/sign-in?callbackUrl=...` and
  `/api/proxy/*` answers 401. `/sign-in` is the only open page; its button starts the Keycloak login and returns to
  the callback URL (only paths on this site are accepted).
- **The browser never sees a token:** server code gets the tokens from `auth()`, but Auth.js's
  `/api/auth/session` endpoint, which the browser can call, returns only the user's name and email, the expiry and any
  error. The browser calls the API through `/api/proxy/<path>`, a route handler that adds
  `Authorization: Bearer <access token>` and forwards to `<API_URL>/api/<path>`. It passes on only `Accept`,
  `Accept-Language`, `Content-Type` and `Last-Event-ID` (never cookies) and streams the response back, so server-sent
  events pass through.
- **Keycloak inside Docker:** when `AUTH_KEYCLOAK_INTERNAL_ISSUER` is set, the server's own calls to Keycloak
  (discovery, the code exchange, refresh) go to that address instead of `AUTH_KEYCLOAK_ISSUER`'s, while the browser
  and the tokens keep the public one. See
  [local-development.md](local-development.md#how-the-containers-reach-keycloak).
- **Federated sign-out:** **Sign out** in the account menu clears the session cookie, then sends the browser to
  Keycloak's end-session endpoint with `id_token_hint`, `client_id` and `post_logout_redirect_uri` set to the app's
  URL. That ends the Keycloak session as well, so the next sign-in asks for a password again. The return URL has to
  match `rp-web`'s **Valid post logout redirect URIs**.

## How the API checks sign-in

The API accepts Keycloak access tokens as `Authorization: Bearer <token>`. The web app's server sends them through its
proxy route ([above](#how-the-web-app-signs-in)); the browser never holds them.

- **Token validation:** issuer `{Keycloak:AuthServerUrl}/realms/{Keycloak:Realm}` (signing keys from its discovery
  document), audience `rp-api` (added to `rp-web` tokens by the audience mapper), lifetime with 30 seconds' clock
  skew. HTTPS metadata is required unless `Keycloak:RequireHttpsMetadata` is `false` or the environment is
  Development; the settings are read from the bound `KeycloakOptions` when JWT validation is first used.
- **Claims:** the JWT claim names are kept (`sub`, `email`, `preferred_username`), and the realm roles in
  `realm_access.roles` are copied into standard role claims, so `RequireRole` and `IsInRole` work.
- **Signed in by default:** the fallback policy requires an authenticated user on every endpoint that doesn't name
  another policy, so a new endpoint can't be left open by mistake. Only `/health` opts out. No or an invalid token
  gets **401**, even for routes that don't exist.
- **Admins:** controllers or actions only admins can call use `[Authorize(Policy = AuthPolicies.AdminOnly)]`, which
  requires `rp-admin`. A signed-in user without it gets **403**.
- **The current user:** inject `ICurrentUser` for `UserId` (the `sub`, which is what data is owned by), `Email` and
  `IsAdmin`. Scope queries to `UserId` unless `IsAdmin`. `GET /api/me` returns these three, which is a quick way to
  check a token.

Integration tests replace JWT validation with a test scheme that reads claims from an `X-Test-Claims` header.
`ApiFactory.CreateUserClient(...)`, `CreateAdminClient()` and `CreateAnonymousClient()` build the clients; roles go
in `realm_access` as Keycloak sends them, so the role mapping is tested too.

## Bootstrapping the first admin

The app can only send invites once an admin exists, so you create the first admin by hand:

1. Start the services with `docker compose up -d` and wait for Keycloak to come up.
2. Sign in at `http://localhost:8080/admin` with the bootstrap admin (`admin` / `admin` unless you changed it).
3. Switch to the **`resource-provisioner`** realm with the realm selector, top left. Don't create the user in
   `master`.
4. **Users → Create new user.** Set **Username** and **Email** to your email address, turn **Email verified** on (or
   Keycloak will ask you to verify it at first sign-in, through MailHog), fill in your name, and select **Create**.
5. **Credentials → Set password.** Use at least 12 characters and turn **Temporary** off.
6. **Role mapping → Assign role.** Filter by realm roles, tick **`rp-admin`** and select **Assign**. `rp-user` is
   already there as a default role.
7. Start the API and web app, open `http://localhost:3000` and sign in with your email and password.

From then on, invite other people from the app's **Users** page ([below](#inviting-users)). The bootstrap admin is only for the Keycloak
admin console; it can't sign in to the app, because it lives in the `master` realm.

## Inviting users

Admins invite colleagues from **Users** (`/admin/users`) in the web app. It's the only way to get an account, because
self-registration is off. The page calls the API's admin-only invitations endpoints through the API proxy; non-admins
are sent to the chat, and the API answers them 403 anyway.

**What an invite does.** `POST /api/invitations` with `{ "email": "..." }`:

1. Lower-cases the email and refuses it (409) if a Keycloak user already has it.
2. Creates the Keycloak user: username and email both the address, enabled, email not verified. Nothing assigns a
   role: `rp-user` is a default realm role, so Keycloak gives it to every new user.
3. Calls Keycloak's `execute-actions-email` for the user with `VERIFY_EMAIL` and `UPDATE_PASSWORD`, `lifespan`
   604800 (7 days), `client_id=rp-web` and `redirect_uri` set to the app URL. Keycloak emails a link (locally, open
   MailHog at `http://localhost:8025`).
4. Saves an `Invitation` row: email, Keycloak user ID, the inviting admin's `sub`, when it was sent and when the link
   expires. If the email can't be sent, the Keycloak user is deleted again and the API answers 502, so the admin can
   just try again.

**What the colleague does.** The link opens Keycloak, which verifies their email, asks for a password (the realm
policy: at least 12 characters), then sends them to the app. Signing in the first time, Keycloak also asks for their
first and last name, because the realm's user profile requires them and the invite only knows the email.

**Statuses.** Worked out every time invitations are read, never stored:

| Status | When |
| --- | --- |
| `Revoked` | An admin revoked it. Wins over everything else. |
| `Accepted` | Keycloak reports the user's email as verified, i.e. they used the link. |
| `Expired` | Not accepted, and the link's expiry has passed. |
| `Pending` | Otherwise. |

Listing asks Keycloak for each non-revoked invitation's user, so it needs only `view-users`.

**Resend and revoke.** Both work only on `Pending` or `Expired` invitations (409 otherwise).

- `POST /api/invitations/{id}/resend` sends a fresh action email and moves the expiry 7 days on. Keycloak's links
  are self-contained tokens, so an earlier link still works until its own expiry.
- `POST /api/invitations/{id}/revoke` disables the Keycloak user and records when. Opening the link then shows
  Keycloak's "Account is disabled" page. The user isn't deleted, so the address can't be invited again until someone
  deletes or re-enables the user in the Keycloak admin console.

Accepted users are managed in the Keycloak admin console (disabling them, making them admins), not in the app.

**Configuration.** `Keycloak:Secret` (the `rp-api` secret) must be set, or invite calls fail with 502 and a message
saying so. `Invitations:AppUrl`, `Invitations:ClientId` and `Invitations:ExpiryDays` set the link's return URL, client
and lifetime; see [architecture.md](architecture.md#configuration). Deployed, `AppUrl` is the app's public URL and has
to match one of `rp-web`'s valid redirect URIs.

**Under the hood.** The API calls Keycloak's admin REST API through the Kiota client from
`Keycloak.AuthServices.Sdk.Kiota`. `Duende.AccessTokenManagement` gets its tokens with client credentials as `rp-api`
and caches them until they expire. The calls sit behind `IKeycloakUserAdmin` (`api/.../Services/Keycloak/`), so tests
use an in-memory fake; the rules live in `InvitationService`. This follows MidlandsHub, without its Entra parts or
its own accept-invite page and token: Keycloak's action link does that job.

## Re-importing the realm

`--import-realm` skips a realm that already exists, so changes to `realm-export.json` only apply to a fresh Keycloak
database. To start Keycloak over without touching the app database:

```bash
docker compose rm -sf keycloak postgres-keycloak
docker volume rm resource-provisioner_postgres-keycloak-data
docker compose up -d
```

This deletes every Keycloak user, including your admin, and generates new client secrets. Bootstrap the admin again
and update the secrets afterwards. `docker compose down -v` does the same but also wipes the app database.
