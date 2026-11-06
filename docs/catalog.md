# Resource catalog

Every resource type the agent can provision is a folder under `catalog/<type>/`:

```
catalog/droplet/
├── main.tf          # the Terraform module
├── variables.tf
├── outputs.tf
└── resource.json    # the manifest
```

The manifest is the single source of truth for a type. The agent's tool schemas (#17), the server-side validation of
a request's variables and the confirmation summary are all generated from it, so adding a resource type means adding
a folder, with no API or agent code change.

- [Manifest format](#manifest-format)
- [Variables](#variables)
- [Outputs](#outputs)
- [What the loader checks](#what-the-loader-checks)
- [Validating a request's variables](#validating-a-requests-variables)
- [Mapping to tool schemas](#mapping-to-tool-schemas)
- [Adding a resource type](#adding-a-resource-type)
- [Current types](#current-types)

## Manifest format

```json
{
  "id": "droplet",
  "displayName": "Droplet",
  "description": "A Linux virtual machine. Pick this for a server you can SSH into.",
  "costHint": "About $4 to $12 a month, depending on size.",
  "variables": [
    {
      "name": "name",
      "label": "Name",
      "type": "string",
      "description": "Hostname: lowercase letters, digits and hyphens.",
      "required": true,
      "pattern": "^[a-z][a-z0-9-]*$",
      "maxLength": 63
    },
    {
      "name": "size",
      "label": "Size",
      "type": "string",
      "description": "s-1vcpu-512mb-10gb ($4/month) or s-1vcpu-1gb ($6/month).",
      "allowedValues": ["s-1vcpu-512mb-10gb", "s-1vcpu-1gb"],
      "default": "s-1vcpu-512mb-10gb"
    }
  ],
  "outputs": [
    { "name": "id", "description": "Droplet ID." },
    { "name": "ipv4_address", "description": "Public IPv4 address." }
  ]
}
```

(Illustrative only; the real droplet manifest comes with #10.)

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | The type's ID, in kebab-case. Must equal the folder name. |
| `displayName` | yes | Human name, shown in the UI and the confirmation summary. |
| `description` | yes | What the resource is and when to pick it. The agent reads this to choose a type, so say what it's for. |
| `costHint` | yes | A rough monthly cost, shown before the user confirms. |
| `variables` | no | What a request can set. See [Variables](#variables). |
| `outputs` | no | Every output in `outputs.tf`. See [Outputs](#outputs). |

Keys are camelCase and case-sensitive. An unknown key is an error, so a misspelt constraint can't be silently ignored.

## Variables

Each variable is a Terraform `variable` of the same name in `variables.tf`. The keywords mirror JSON Schema's, so a
variable maps straight onto a tool input schema property.

| Keyword | Applies to | Meaning | JSON Schema |
| --- | --- | --- | --- |
| `name` | all | snake_case; the Terraform variable (or object field) name | property name |
| `type` | all | `string`, `integer`, `number`, `boolean`, `array` or `object` | `type` |
| `description` | all | What the value is, written for the agent and the user. Required on variables and object fields, optional on `items`. Put cost and constraint hints here: the agent sees this, not the keywords it can't enforce. | `description` |
| `label` | variables, fields | Short label for the confirmation summary; defaults to `name` | (none) |
| `required` | variables, fields | A value must be given. Can't be combined with `default`. | `required` |
| `default` | variables, fields | Used when no value (or `null`) is given. Must pass the variable's own constraints. | `default` |
| `allowedValues` | string, integer, number | The only accepted values. This is how cheap sizes, regions and versions are allow-listed. | `enum` |
| `pattern` | string | .NET regular expression; anchor it with `^...$` | `pattern` |
| `minLength` / `maxLength` | string | Length bounds, inclusive | `minLength` / `maxLength` |
| `minimum` / `maximum` | integer, number | Value bounds, inclusive | `minimum` / `maximum` |
| `items` | array | The spec each element must match: any type, including `object` | `items` |
| `minItems` / `maxItems` | array | Element count bounds, inclusive | `minItems` / `maxItems` |
| `properties` | object | The object's fields, each a variable spec (`name`, `type`, `required`, `default`...). No other fields are accepted. | `properties` + `required` + `additionalProperties: false` |
| `sensitive` | top-level variables | The value is a secret. See below. | (left out) |

Lists of objects, such as a firewall's rules, use `"type": "array"` with `"items": { "type": "object", "properties": [...] }`;
the matching Terraform type is `list(object({...}))`, with `optional(...)` for fields that aren't required.

**Sensitive variables** are secrets the LLM must never see or set. They are left out of the agent's tool schemas and
masked in summaries, so a sensitive variable can't be `required` (the agent couldn't collect it) and can't have a
`default` (that would commit a secret). The server supplies the value. It must also be `sensitive = true` in
`variables.tf`.

**Variables the runner sets** (such as the forced `resource-provisioner` / `rp-<requestId>` tags) are declared in
`variables.tf` but left out of the manifest, so neither the user nor the LLM can set them. Give them a default in
Terraform.

- `rp_name_suffix` (`string`, default `""`): a short value from the request ID (up to 8 lowercase letters or digits).
  The resource's DigitalOcean name is `<name>-<rp_name_suffix>`, so names never clash in the shared account. Set the
  manifest's `name` `maxLength` to the resource's name limit minus 9 (for `-` and the suffix).

**Referencing the user's own droplets** (a firewall's targets, a database's trusted sources): never take a tag, name
or DigitalOcean ID. Take an array of droplet **request IDs** (`items.pattern` a lowercase GUID, `maxItems` the
provider's limit), named `droplet_request_ids` or `<purpose>_droplet_request_ids`, and have the module turn each into
its `rp-<requestId>` tag. The manifest can only check the shape, so:

- **#14** must, at `propose_request` and again at confirm, reject the request unless every ID is a `droplet` request
  owned by the requesting user (admins included) and `Succeeded`.
- **#17**'s `list_my_resources` must return the request ID and type of the user's own resources only.

A free-form tag would let a user name another user's `rp-<requestId>` or the shared `resource-provisioner` tag. Request
IDs are never reused, so a tag can't later match someone else's droplet.

**Rules across variables** ("at least one of these two lists") can't be written in a manifest. Say the rule in each
variable's `description` and enforce it in Terraform: a `lifecycle` `precondition` (variable `validation` can only
refer to other variables from Terraform 1.9). It then fails at plan, after confirm, so #14 may want to check it at
`propose_request` too.

## Outputs

List every output in `outputs.tf`, with `name`, `description` and `sensitive`. A sensitive output (a password, a
connection string) is stored apart from the others and never reaches the LLM. `sensitive` must match the output's
`sensitive = true` in `outputs.tf`, and every output must be listed, so a secret output can't slip past the manifest.

## What the loader checks

The API loads every manifest at startup (`CatalogLoader`, run by `CatalogStartupCheck`) and refuses to start if any is
invalid, listing every problem in every manifest. Hidden folders (`.terraform/`) are skipped. It checks:

- `resource.json`, `main.tf`, `variables.tf` and `outputs.tf` all exist
- the JSON parses, required fields are present, nothing unknown or `null` where a value is required
- `id` is kebab-case and equals the folder name; names are snake_case and unique
- each keyword applies to the variable's type (no `pattern` on an integer), bounds aren't inverted or negative,
  `pattern` compiles, `allowedValues` are non-empty, unique and of the right type
- arrays have `items`, objects have `properties`
- `required` and `default` aren't combined; sensitive variables are top-level, not required and not defaulted
- every default passes its own variable's constraints
- every manifest variable is declared in `variables.tf`, the outputs match `outputs.tf` exactly, and `sensitive`
  agrees on both sides. This reads `terraform fmt`-formatted files (top-level blocks close with `}` in column 0); it
  is not a full HCL parser.

`dotnet test` also loads the real `catalog/` (`CatalogLoaderTests.Load_LoadsRepositoryCatalog_WithoutErrors`), so a
bad manifest fails CI rather than a deploy.

### Where the catalog is

`Catalog:Root` (default `catalog`). An absolute path is used as-is. A relative path is looked for in the content root
(the project folder under `dotnet run`) and then each folder above it, so the default finds the repo's `catalog/`
wherever in the repo the API starts. Deployments (#21) set it explicitly. Tests point it at the fixture catalog in
`api/tests/ResourceProvisioner.Api.Tests/Fixtures/catalog/` (`test-server`, `test-firewall`), which covers every
variable type and isn't a real module.

## Validating a request's variables

`ICatalogService.ValidateVariables(type, values)` checks a request's variables (a `JsonObject`) against the manifest
and returns:

- `Values`: the variables with defaults applied, including defaults of fields inside objects in lists, ready to
  become `terraform.tfvars.json`. Optional variables with no default and no value are left out, so Terraform's default
  applies.
- `Errors`: messages keyed by field path (`size`, `tags[1]`, `rules[0].protocol`), in the shape of ASP.NET Core's
  validation problem details. An unknown type is reported on `type`, an unknown name as `Unknown field.`

A `null` value counts as not given. Every error is collected, not just the first, so the agent can fix them in one go.

`GET /api/catalog` returns every type with its variables and outputs, in the manifest's own shape. Like every
endpoint, it requires sign-in.

## Mapping to tool schemas

#17 generates the agent's tool input schemas from the manifests with `strict: true`. Strict mode supports `type`,
`enum`, `required`, `description`, nested objects and arrays, and requires `additionalProperties: false` on every
object. It does **not** support `minimum`/`maximum`, `minLength`/`maxLength` or array length constraints, so the
generator leaves those out and `ValidateVariables` enforces them on the server; state them in `description` too so the
agent asks for a valid value. Mapping:

- each non-sensitive variable becomes a property; `required: true` variables go in `required`
- `allowedValues` becomes `enum`; `items` and `properties` map recursively (object fields get their own `required`
  list and `additionalProperties: false`)
- sensitive variables and the server-only variables above are never in the schema
- sensitive outputs are never put in a tool result or the prompt

Because strict mode needs a fixed schema, the nesting in a manifest is finite (no recursive types).

## Adding a resource type

1. Create `catalog/<type>/` with `main.tf`, `variables.tf` and `outputs.tf`. Run `terraform fmt`. CI runs
   `terraform fmt -check` and `terraform validate` on it.
2. Write `catalog/<type>/resource.json`: one variable per user-settable Terraform variable, every output, matching
   `sensitive` flags. Allow-list cheap sizes, regions and versions with `allowedValues`, and give safe defaults.
3. Write descriptions for the agent: what the type is for, what each value means and costs.
4. Run `dotnet test` in `api/`: it loads the real catalog and fails on any problem.
5. Add the type to [Current types](#current-types).

No API or agent code changes.

## Current types

Droplet (#10), cloud firewall (#12) and Spaces bucket (#13) are planned; each adds a row to this table.

| ID | What it is |
| --- | --- |
| `postgres` | A single-node managed PostgreSQL cluster ($15 or $30 a month), six regions, PostgreSQL 16, 17 or 18, with an app database and non-admin user, reachable only from the user's own droplets and listed IPv4 addresses. See [Managed Postgres](#managed-postgres). |

### Managed Postgres

- **Allow-lists** (checked on 2026-11-06 against DigitalOcean's
  [pricing](https://www.digitalocean.com/pricing/managed-databases),
  [regional availability](https://docs.digitalocean.com/platform/regional-availability/) and
  [version support](https://docs.digitalocean.com/products/databases/version-support-policy/) pages): sizes
  `db-s-1vcpu-1gb` (default), `db-s-1vcpu-2gb`; regions `nyc3` (default), `sfo3`, `ams3`, `lon1`, `fra1`, `sgp1`;
  versions `18` (default), `17`, `16`. Version 15 is left out because new clusters can't use it after May 2027, and
  14 already can't. Always `node_count = 1`.
- **Variables:** `postgres_version`, not `version`, which Terraform reserves.
- **App database and user, always created:** `database_name` (default `app`) and `user_name` (default `app_user`)
  become a `digitalocean_database_db` and a non-admin `digitalocean_database_user`. The manifest patterns and the
  Terraform validations refuse names the cluster already uses (`defaultdb`, `doadmin`, `postgres`, `template0/1`,
  `pg_*`).
- **Restricted access, never open:** a `digitalocean_database_firewall` (trusted sources) always has at least one
  rule, and the cluster is unreachable from anywhere else
  ([trusted sources](https://docs.digitalocean.com/products/databases/postgresql/how-to/secure/)). Sources:
  - `allowed_droplet_request_ids`: up to 10 of the user's own droplets, as in
    [Referencing the user's own droplets](#variables); each becomes a `tag` rule on `rp-<requestId>`. Ownership is
    checked by #14, not here.
  - `allowed_ip_addresses`: up to 10 IPv4 CIDRs, `/8` to `/32`. DigitalOcean's trusted sources don't take IPv6.
    Prefixes shorter than `/8` are refused, so with 10 entries at most 10/256 of IPv4 space can be listed and
    `0.0.0.0/0` can't.

  Both default to `[]`, and at least one source in total is required. The manifest can't say that (see
  [Rules across variables](#variables)), so both descriptions state it and a `precondition` on the firewall fails the
  plan when both are empty. DigitalOcean allows 100 rules per cluster; 20 at most stays well under.
- **Outputs:** `id`, `host`, `port`, `database`, `user` and the sensitive `password` and `uri` are always the app
  user's. `uri` is built in `outputs.tf` (`postgresql://user:password@host:port/database?sslmode=require`) because
  the cluster's own `uri` is for `doadmin`. The sensitive `admin_password` and `admin_uri` (`doadmin` on `defaultdb`)
  are kept separately: users have no DigitalOcean console access, so this is their only admin route, for example to
  grant the app user `CREATE` on a schema (on PostgreSQL 15+, a user that doesn't own the database can't create
  tables in `public` by default).
- **Name:** `<name>-<rp_name_suffix>`; `name` is 3 to 34 characters, so the whole is at most 43. DigitalOcean doesn't
  document a name limit for PostgreSQL clusters. 43 is the limit it documents for its managed vector database
  clusters ([quickstart](https://docs.digitalocean.com/products/vector-databases/getting-started/quickstart/)), and
  the cluster name also goes into its hostname, so keeping it short leaves room in that DNS label.
- **Connecting an app:** run the app on a droplet listed in `allowed_droplet_request_ids` and connect with the `uri`
  output, or `host`, `port`, `database`, `user` and `password` with TLS on (`sslmode=require`). Plain connections
  are refused.
