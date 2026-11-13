# resource-provisioner — Agent Instructions

A chat UI (`web/`, Next.js) and an API (`api/`, ASP.NET Core 10) where a Claude agent picks a resource type from a
vetted Terraform catalog (`catalog/`), collects its variables and, once the user confirms, provisions it on
DigitalOcean. How it works is documented in [docs/](docs/README.md); this file holds the rules only.

Read [docs/architecture.md](docs/architecture.md) before changing the agent, catalog, requests or Terraform code: its
design rules (the LLM never writes HCL or triggers provisioning, manifests are the single source of truth, secrets never
reach the LLM, and so on) are hard constraints.

---

## Development Workflow

When implementing a feature or bug fix:

0. **Create a GitHub issue** if one describing the work doesn't already exist, using the body format from
   `.github/ISSUE_TEMPLATE.md`:
   ```bash
   gh issue create --title "<title>" --body "<description>"
   ```
1. **Ensure `main` is up to date** before branching:
   ```bash
   git checkout main && git pull
   ```
2. **Create a branch** with a descriptive name:
   ```bash
   git checkout -b feature/<name>   # new functionality
   git checkout -b fix/<name>       # bug fix
   ```
3. **Develop** the requested change.
4. **Commit** to the branch. Group commits by type and file:
   - App code per file
   - Tests per file, separate from app code
   - `CLAUDE.md`/`AGENTS.md` updates last
5. **Open a PR** using the `gh` CLI, with a body matching `.github/pull_request_template.md`:
   ```bash
   gh pr create --title "<title>" --body "..."
   ```

---

## Build & Test

```bash
cd web && npm ci && npm run lint && npm test && npm run build   # web
docker compose up -d postgres-app                                # Postgres for the API tests
cd api && dotnet build && dotnet test                            # API
terraform fmt -recursive catalog infra                           # Terraform formatting
```

CI (`.github/workflows/ci.yml`) runs on PRs into, and pushes to, `main`, in three jobs:

- **web:** `npm ci`, `npm run lint -- --max-warnings=0` (Prettier and import-order warnings fail CI), `vitest run`,
  `npm run build`
- **api:** `dotnet build`, `dotnet test` against a `postgres:16` service container
- **terraform:** `terraform fmt -check -recursive` over `catalog/` and `infra/`, then `init -backend=false` and
  `validate` for each `catalog/*/` module and `infra/`; folders without `.tf` files are skipped

Run the matching checks locally before opening a PR.

---

## Code Style

Follow `.editorconfig` at the repo root for formatting: indent size, line endings, charset, C# naming conventions and
C# language preferences. Do not override or duplicate its rules. In `web/`, Prettier and ESLint are the authority.

- `web/`: Next.js 16 changes often. Before writing Next.js code, read the matching guide in
  `web/node_modules/next/dist/docs/`, not what you remember. Cache Components is on.
- `web/`: TypeScript function components with hooks. Tailwind v4 + shadcn/ui, style `base-nova` (Base UI, not Radix:
  compose with the `render` prop, not `asChild`). Add primitives with `npx shadcn@latest add <component>` and never
  hand-edit `components/ui/*`; customise with `className` at the call site. Forms use shadcn's `field` component with
  react-hook-form + zod (there is no `form` component in `base-nova`). Icons from `lucide-react` only. The full stack
  is listed in [docs/web.md](docs/web.md); don't add a library that duplicates one already there.
- `web/`: the browser never holds a Keycloak token. Read the user with `getCurrentUser()` behind `<Suspense>` and pass
  only that to Client Components, never the session; call the API through `/api/proxy/*`. See
  [docs/web.md](docs/web.md#sign-in-and-the-session).
- `api/`: controllers (`[ApiController]`, one per resource in `Controllers/`), not minimal APIs. Keep them thin;
  logic belongs in services behind interfaces so tests can fake them. Request and response types go in `Models/`.
- `catalog/`: every module is run through `terraform fmt`. Each module's `resource.json` must match its `variables.tf`
  and `outputs.tf`; the API won't start otherwise. The manifest format is in [docs/catalog.md](docs/catalog.md).

---

## Documentation

### Backend (C#)

All public classes, methods and properties must have XML doc comments:

```csharp
/// <summary>Brief description of what this does.</summary>
/// <param name="paramName">What the parameter is.</param>
/// <returns>What is returned.</returns>
```

Internal and private members: add a comment only when the intent is not obvious from the name and types alone.

### Frontend (TypeScript)

All exported functions, components and hooks must have JSDoc comments:

```ts
/**
 * Brief description of what this does.
 * @param paramName - what the parameter is
 * @returns what is returned
 */
```

Inline comments for non-obvious logic only. Do not comment self-evident code.

### Keep `docs/` current

README, CLAUDE.md and AGENTS.md stay short. Explanations live in `docs/`, one file per topic, indexed in
[docs/README.md](docs/README.md). A change that alters how something works updates its doc in the same PR; a new topic
gets a new file and an index entry. Keep `AGENTS.md` identical to `CLAUDE.md`.

---

## Testing

### Backend

- **Framework:** xUnit, in `api/tests/ResourceProvisioner.Api.Tests/`; run `docker compose up -d postgres-app`, then
  `dotnet test` from `api/`
- **Attributes:** `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterised cases
- **Naming:** `MethodName_ExpectedBehaviour_Condition`
- **Pattern:** `WebApplicationFactory` integration tests on Postgres (a migrated database per `ApiFactory`, server
  from `RP_TEST_POSTGRES`) for endpoints and middleware, with a test auth scheme instead of Keycloak; plain unit tests
  for pure logic. Fake Keycloak, Claude and the Terraform process
  runner. Tests never call real external services.

### Frontend

- **Framework:** Vitest + React Testing Library
- **Location:** colocate tests as `ComponentName.test.tsx`, or group them in a `__tests__/` folder in the same
  directory
- **Naming:** a `describe` block per component or function; `it('does x when y')`

### Terraform

- `terraform fmt -check` and `terraform validate` pass for every `catalog/*` module and for `infra/`.
