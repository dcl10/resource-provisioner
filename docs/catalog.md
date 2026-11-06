# Resource catalog

Every resource type the agent can provision is a folder under `catalog/<type>/`:

```
catalog/droplet/
├── main.tf          # the Terraform module
├── variables.tf
├── outputs.tf
├── versions.tf      # optional: pins Terraform and the provider
└── resource.json    # the manifest
```

The manifest is the single source of truth for a type. The agent's tool schemas (#17), the server-side validation of
a request's variables and the confirmation summary are all generated from it, so adding a resource type means adding
a folder, with no API or agent code change.

- [Manifest format](#manifest-format)
- [Variables](#variables)
- [Rules across variables](#rules-across-variables)
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

(Shortened; the real one is [catalog/droplet/resource.json](../catalog/droplet/resource.json).)

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | yes | The type's ID, in kebab-case. Must equal the folder name. |
| `displayName` | yes | Human name, shown in the UI and the confirmation summary. |
| `description` | yes | What the resource is and when to pick it. The agent reads this to choose a type, so say what it's for. |
| `costHint` | yes | A rough monthly cost, shown before the user confirms. |
| `variables` | no | What a request can set. See [Variables](#variables). |
| `outputs` | no | Every output in `outputs.tf`. See [Outputs](#outputs). |
| `atLeastOneOf` | no | Groups of variables of which a request must set at least one. See [Rules across variables](#rules-across-variables). |

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
Terraform. Every module takes the tags as `rp_tags` (`list(string)`, default `[]`) and puts them on each resource it
creates; a request that sets `rp_tags` is rejected as an unknown field.

- `rp_name_suffix` (`string`, default `""`): a short value from the request ID (up to 8 lowercase letters or digits).
  The resource's DigitalOcean name is `<name>-<rp_name_suffix>`, so names never clash in the shared account. Set the
  manifest's `name` `maxLength` to the resource's name limit minus 9 (for `-` and the suffix).

**Provider credentials** never appear as variables. A module declares an empty `provider` block, and the runner passes
the token in the environment (`DIGITALOCEAN_TOKEN` for the `digitalocean/digitalocean` provider).

**Referencing the user's own droplets** (a firewall's targets, a database's trusted sources): never take a tag, name
or DigitalOcean ID. Take an array of droplet **request IDs** (`items.pattern` a lowercase GUID, `maxItems` the
provider's limit), named `droplet_request_ids` or `<purpose>_droplet_request_ids`, and have the module turn each into
its `rp-<requestId>` tag. The manifest can only check the shape, so:

- The API, at `propose_request` and again at confirm, rejects the request unless every ID is a `droplet` request
  owned by the requesting user (admins included) and `Succeeded`. See
  [architecture.md](architecture.md#ownership). It finds these variables by name, so the loader requires them to be
  top-level arrays of strings.
- **#17**'s `list_my_resources` must return the request ID and type of the user's own resources only.

A free-form tag would let a user name another user's `rp-<requestId>` or the shared `resource-provisioner` tag. Request
IDs are never reused, so a tag can't later match someone else's droplet.

## Rules across variables

Some rules involve more than one variable, such as a database that needs at least one trusted source from either of
two lists. Keywords on a single variable can't say that, so the manifest has one top-level rule for it:

```json
"atLeastOneOf": [
  ["allowed_droplet_request_ids", "allowed_ip_addresses"]
]
```

Each group names two or more top-level variables; a request must **set** at least one of them, after defaults are
applied. Set means not `null`, not an empty string and not an empty array. `ValidateVariables` checks it, so a
request breaking it is refused at `propose_request` and again at confirm, instead of failing at `terraform plan` after
the user confirmed. The error goes on every variable in the group (`Give at least one of a, b.`). A group is skipped
while one of its variables has its own errors, so a malformed value isn't also reported as missing.

Tool schemas can't express it either, so say the rule in each variable's `description` as well. Enforcing it in
Terraform too (a `lifecycle` `precondition`) is still a good idea, as a last line of defence.

Only `atLeastOneOf` exists; add another rule kind the same way when a module needs one, rather than writing per-type
code, so adding a resource type stays a folder-only change.

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
- each `atLeastOneOf` group names at least two distinct top-level variables that exist and are neither required (the
  rule would always hold) nor sensitive (the agent can't set them)
- variables named `droplet_request_ids` or `*_droplet_request_ids` are top-level arrays of strings, so the ownership
  check can find and read them
- every manifest variable is declared in `variables.tf`, the outputs match `outputs.tf` exactly, and `sensitive`
  agrees on both sides. This reads `terraform fmt`-formatted files (top-level blocks close with `}` in column 0); it
  is not a full HCL parser.

`dotnet test` also loads the real `catalog/` (`CatalogLoaderTests.Load_LoadsRepositoryCatalog_WithoutErrors`), so a
bad manifest fails CI rather than a deploy.

### Where the catalog is

`Catalog:Root` (default `catalog`). An absolute path is used as-is. A relative path is looked for in the content root
(the project folder under `dotnet run`) and then each folder above it, so the default finds the repo's `catalog/`
wherever in the repo the API starts. Deployments (#21) set it explicitly. Tests point it at the fixture catalog in
`api/tests/ResourceProvisioner.Api.Tests/Fixtures/catalog/` (`test-server`, `test-firewall`, `test-database`), which
covers every variable type, a droplet reference and an `atLeastOneOf` rule, and isn't a real module.

## Validating a request's variables

`ICatalogService.ValidateVariables(type, values)` checks a request's variables (a `JsonObject`) against the manifest
and returns:

- `Values`: the variables with defaults applied, including defaults of fields inside objects in lists, ready to
  become `terraform.tfvars.json`. Optional variables with no default and no value are left out, so Terraform's default
  applies.
- `Errors`: messages keyed by field path (`size`, `tags[1]`, `rules[0].protocol`), in the shape of ASP.NET Core's
  validation problem details. An unknown type is reported on `type`, an unknown name as `Unknown field.`

A `null` value counts as not given. Every error is collected, not just the first, so the agent can fix them in one go.
The manifest's [`atLeastOneOf`](#rules-across-variables) rules are checked here too. Whether droplet references
belong to the user needs the database, so `IProvisioningRequestService` checks that on top.

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
- `atLeastOneOf` has no strict-mode equivalent; it is enforced on the server and stated in the descriptions
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

Managed Postgres (#11) and Spaces bucket (#13) are planned; each adds a row to this table.

| ID | What it is |
| --- | --- |
| `droplet` | A Linux VM: the three cheapest Basic sizes ($4, $6, $12 a month), six regions, Ubuntu 22.04/24.04/26.04 LTS or Debian 13. See [Droplet](#droplet). |
| `firewall` | A free Cloud Firewall on up to five of the user's own droplets, named by their request IDs: typed inbound rules, outbound open to anywhere unless set. See [Cloud Firewall](#cloud-firewall). |

### Droplet

- **Allow-lists** (checked against DigitalOcean's [pricing](https://www.digitalocean.com/pricing/droplets),
  [regional availability](https://docs.digitalocean.com/platform/regional-availability/) and
  [images](https://docs.digitalocean.com/products/droplets/details/images/) pages): sizes `s-1vcpu-512mb-10gb`,
  `s-1vcpu-1gb`, `s-1vcpu-2gb`; regions `nyc3`, `sfo3`, `ams3`, `lon1`, `fra1`, `sgp1`; images `ubuntu-24-04-x64`
  (default), `ubuntu-26-04-x64`, `ubuntu-22-04-x64`, `debian-13-x64`.
- **SSH access:** the request carries the user's SSH **public** key (`ssh_public_key`, required, not sensitive).
  cloud-init installs it for `root` and turns SSH password login off, so the user logs in with
  `ssh root@<ipv4_address>`. It is not added as a DigitalOcean account SSH key: the account is shared by every user,
  and DigitalOcean refuses the same key twice, so a second droplet with the same key would fail. Because no account
  key is attached, DigitalOcean emails a root password to the account owner; it works only on the web console.
- **Name:** `<name>-<rp_name_suffix>`; `name` is at most 54 characters, keeping the hostname within DigitalOcean's 63.
- **Outputs:** `id`, `ipv4_address`, `tags`.

### Cloud Firewall

- **Targets:** `droplet_request_ids`, 1 to 5, as in
  [Referencing the user's own droplets](#variables); the firewall applies to droplets with those `rp-<requestId>` tags.
  Five is DigitalOcean's tag limit per firewall
  ([limits](https://docs.digitalocean.com/products/networking/firewalls/details/limits/)). Ownership is checked by #14,
  not here.
- **Rules:** `inbound_rules` (required, 1 to 20) and `outbound_rules` (up to 20), each a list of `protocol` (`tcp`,
  `udp` or `icmp`), `port_range` (`22`, `8000-9000` or `all`; the module drops it for `icmp` and sends `all` as
  `1-65535`) and `source_addresses` / `destination_addresses` (1 to 10 IPv4 or IPv6 CIDRs). Allow rules only, and
  addresses only: the provider's `source_tags`, `source_droplet_ids` and the like are left out so a rule can't name
  other users' resources. 40 rules at most stays under DigitalOcean's 50.
- **Defaults:** outbound allows tcp, udp and icmp to `0.0.0.0/0` and `::/0`. A DigitalOcean firewall blocks whatever
  no rule allows, so without that default a droplet couldn't reach DNS or package mirrors. Inbound has no default.
- **SSH from anywhere** (`0.0.0.0/0` on port 22) is allowed. Droplets are key-only (#10), and a droplet with no
  firewall is already open on 22, so refusing it would only push users to skip the firewall. The description tells
  the agent to suggest the user's own `/32` and to warn when the source is anywhere.
- **Checks:** the manifest's patterns check ports are 1 to 65535 and addresses look like CIDRs. A low-high range
  (`9000-8000`) and malformed IPv6 pass the manifest but fail the Terraform variable validation at plan time.
- **`rp_tags` is declared but unused.** A firewall can't carry tags: `digitalocean_firewall`'s `tags` picks the droplets
  it applies to, so passing `rp_tags` there would attach it to every user's droplets. Ownership of a firewall is
  tracked by its request only.
- **Name:** `<name>-<rp_name_suffix>`; `name` is at most 54 characters, keeping the whole within 63.
- **Outputs:** `id`, `status`, `target_tags`.
