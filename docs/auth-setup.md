# Authentication setup

Sign-in goes through Keycloak only, and it's invite-only: self-registration is off, so nobody can create an account and
start spending LLM tokens or DigitalOcean money. You bootstrap the first admin by hand (below); everyone else is
invited from the app.

- [Local services](#local-services)
- [What the realm import creates](#what-the-realm-import-creates)
- [Client settings](#client-settings)
- [Client secrets](#client-secrets)
- [Bootstrapping the first admin](#bootstrapping-the-first-admin)
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
defaults; the deployed setup (#21) uses its own.

The OIDC issuer is `http://localhost:8080/realms/resource-provisioner`.

## What the realm import creates

On its first start, Keycloak imports [keycloak/realm-export.json](../keycloak/realm-export.json) (`start-dev
--import-realm`). It creates the realm `resource-provisioner` with:

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

`web/.env.local` also needs the rest of the Auth.js settings (set up in #7):

```bash
AUTH_SECRET=<run: npx auth secret>
AUTH_KEYCLOAK_ID=rp-web
AUTH_KEYCLOAK_SECRET=<rp-web secret>
AUTH_KEYCLOAK_ISSUER=http://localhost:8080/realms/resource-provisioner
```

`.env*` files are gitignored, apart from `.env.example`.

**To regenerate a secret**, select **Regenerate** on the same Credentials tab, then update user-secrets or
`.env.local` and restart the API or web app. The old secret stops working straight away. Re-importing the realm
(below) also generates new secrets.

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

From then on, invite other people from the app's **Users** page (#8). The bootstrap admin is only for the Keycloak
admin console; it can't sign in to the app, because it lives in the `master` realm.

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
