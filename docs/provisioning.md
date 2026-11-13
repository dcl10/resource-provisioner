# Provisioning

The Terraform runner turns a confirmed request into real DigitalOcean resources, and tears them down again when the
request is destroyed. It lives in `api/src/ResourceProvisioner.Api/Services/Provisioning/` and runs inside the API
process.

## The runner

```
POST /api/requests/{id}/confirm ─▶ status Queued ─────┐
POST /api/requests/{id}/destroy ─▶ status Destroying ─┴─▶ IJobDispatcher (in-memory channel)
                                                         │
                                  TerraformRunner (BackgroundService) takes one ID at a time
                                                         │
                                  ITerraformJobProcessor, in a fresh DI scope per job
```

- **One job at a time.** `TerraformRunner` reads request IDs from `IJobDispatcher` and finishes each job before taking
  the next, so two jobs never run at once (a spend cap, see [architecture.md](architecture.md#spend-caps)), and an
  apply and a destroy of the same request can't overlap.
- **The status decides the work.** A job is just a request ID; the processor looks at the request's status when it gets
  there. `Queued` is applied, `Destroying` is destroyed, and anything else is skipped, so a duplicate or stale entry is
  harmless.
- **Restarts.** The queue is in memory. When the runner starts it first moves any request left `Applying` to `Failed`,
  logging that the API stopped mid-apply and the request should be destroyed to clean up; a half-done apply isn't
  retried. Then it re-queues every `Queued` and `Destroying` request, oldest change first. A `Destroying` request may
  have been waiting its turn or half-done, and the runner can't tell which, but running `terraform destroy` again is
  safe either way. Confirm and destroy save the status before queueing, so a crash in between loses nothing.

## Applying

1. Move the request `Queued → Applying` (the status is a concurrency token, so a request that changed meanwhile is
   skipped).
2. Fail the job if Terraform already has state for the request ([Workspaces and state](#workspaces-and-state)), so an
   apply never runs over an earlier one.
3. Build the tfvars (below) and save them on the request (`ProvisioningRequests.TerraformVariables`) before Terraform
   first runs, so even a half-done apply can be destroyed with the same values. Then prepare the workspace.
4. Run, in the workspace, with `-input=false -no-color` (`workspace select` takes only `-no-color`):
   `terraform init`, `terraform workspace select -or-create=true <requestId>`, `terraform plan -out=plan.tfplan`,
   `terraform apply plan.tfplan`, `terraform output -json`. A non-zero exit stops the job.
5. Split the outputs: an output is sensitive if the manifest **or** Terraform marks it sensitive. Non-sensitive ones go
   in `ProvisioningRequests.Outputs`, sensitive ones in `RequestSensitiveOutputs`.
6. Move to `Succeeded`, or to `Failed` with a last log line saying why (`Failed: terraform plan exited with code 1.`).
   A failed apply may have created part of the module; the request can still be destroyed.

The job never sees the user or the LLM: everything it runs comes from the vetted module and the validated variables.

### Dependencies

A request that references others, or belongs to a project ([architecture.md](architecture.md#dependencies)), is only
applied once all of them, its project's request included, have `Succeeded`. When the runner takes a queued request, it
first reads what it depends on:

- **All succeeded:** it's applied as above.
- **Some still pending** (queued or applying): it's left `Queued`, and the API log notes it's waiting. After every job,
  the runner queues the requests waiting on that one again, so a sequence runs in order whatever order it was queued
  in, and still runs after a restart (startup recovery re-queues every `Queued` request).
- **One failed or is being destroyed:** it moves `Queued → Failed` without running Terraform, with the log line
  `Failed: didn't run, because <type> "<name>" (<id>), which it depends on, is <status>.`
- **One no longer exists** (it was cancelled or destroyed, and so deleted): the same, with
  `Failed: didn't run, because request <id>, which it depends on, no longer exists.` Deleting a request deletes the
  dependencies on it, and neither can happen while a confirmed request depends on it, so this only catches a delete
  that races the runner's read.

## Destroying

`POST /api/requests/{id}/destroy` is allowed from `Succeeded` or `Failed` (a failed apply or a failed destroy), by the
owner or an admin (see [architecture.md](architecture.md#ownership)), and only once nothing that depends on it can
still exist (see [Dependencies](architecture.md#dependencies)); otherwise it answers 409. It moves the
request to `Destroying` and queues it. The runner then:

1. Checks whether Terraform has state for the request. Without it, Terraform never ran (the apply failed before
   `workspace select`), so it logs `Nothing to destroy: ...` and skips to step 3.
2. Prepares the workspace again, from the type's module in the catalog and the tfvars the apply saved, and runs
   `terraform init`, `terraform workspace select <requestId>` and `terraform destroy -auto-approve` in it, with
   `-input=false -no-color`. Nothing an earlier job left on disk is needed. If the type is no longer in the catalog,
   or the request has no saved tfvars (it was applied before they were saved), the request moves to `Failed` without
   running Terraform, and the log says an admin can delete its resources by the `rp-<requestId>` tag.
3. Deletes the request, through `IRequestDeleter`, in one transaction: the request, its sensitive outputs (the
   credentials they held no longer work), its job log, its dependencies both ways, the project it backs (for a
   `project` request) and its Terraform state (`ITerraformStateStore.DeleteStateAsync`, the request's row in
   `terraform_state.states`). Nothing is kept; the API logs `Destroyed and deleted request <id>`. Proposals that
   depended on it stay, unlinked, and can't be confirmed ([architecture.md](architecture.md#deleting-a-request)). If
   the delete itself fails (something that may exist still depends on it), the transaction rolls back and the request
   moves to `Failed`, keeping everything.

If `destroy` exits non-zero the request moves to `Failed` with everything kept, its outputs and its state above all,
since some resources may still exist, and it can be destroyed again. State is only ever deleted after a successful
destroy. `Destroying` counts as active, so the active-request count drops only once the request is deleted.

An open request page or log stream sees the request go: the logs endpoint ends with a `deleted` event, and
`GET /api/requests/{id}` answers 404.

One gap: an apply killed (the API stopped) before Terraform first wrote its state can leave resources with no state to
destroy them from. Everything the runner creates is tagged `rp-<requestId>`, so an admin can find such leftovers in the
DigitalOcean console by that tag.

### Credentials

Credentials go to Terraform **only in its environment**, never in tfvars, the workspace or the log:

| Variable | From | Used by |
| --- | --- | --- |
| `PG_CONN_STR`, `PG_SCHEMA_NAME` | `ConnectionStrings:DefaultConnection`, converted to libpq form; the schema is `terraform_state` | the `pg` state backend ([below](#workspaces-and-state)) |
| `DIGITALOCEAN_TOKEN` | `DigitalOcean:Token` (required; without it the job fails before running anything) | the `digitalocean` provider |
| `SPACES_ACCESS_KEY_ID`, `SPACES_SECRET_ACCESS_KEY` | `Spaces:AccessId`, `Spaces:SecretKey` (set only when both are) | `digitalocean_spaces_bucket` |

The runner also sets `TF_IN_AUTOMATION=1`, `TF_INPUT=0`, `CHECKPOINT_DISABLE=1` and `TF_PLUGIN_CACHE_DIR` (below).

## Workspaces and state

Each job prepares `<Workspaces:Root>/<requestId>/` afresh, deleting whatever an earlier job left there:

| File | What it is |
| --- | --- |
| the module's files | copied from `catalog/<type>/`, leaving out `resource.json`, `.terraform/`, state, plans and tfvars files |
| `terraform.tfvars.json` | the request's variables (defaults applied), with the runner's `rp_name_suffix` and `rp_tags` (and the variables taken from requests it depends on: `rp_project_id`, its project's DigitalOcean ID, if the module declares it, and any reference's `rp_<x>_id`) laid over them, so a value smuggled into the variables can't replace the forced tags. Built by the apply and saved on the request as `TerraformVariables`; destroy writes the saved copy, so it uses exactly what the apply did, even if the request's dependencies have gone since. |
| `rp_backend.tf` | `terraform { backend "pg" {} }`, added by the runner; its settings come from the environment, so it holds no credentials |
| `plan.tfplan` | the saved plan that apply runs |

**State is in the app database**, not the folder. Terraform's [`pg`
backend](https://developer.hashicorp.com/terraform/language/backend/pg) keeps it in the `terraform_state` schema of
the app's Postgres, which Terraform creates on first use (table `states`: one row per Terraform workspace, `name` and
`data`). EF Core migrations don't create or change that schema (`DeleteFinishedRequests` only deleted the rows of
requests already destroyed). Each request has its own Terraform workspace, named by its request ID, so requests stay
independent; the row appears when `workspace select -or-create=true` first runs, `TerraformStateStore` checks for it
before an apply (which must find none) and a destroy (which has nothing to do without one), and deletes it, in the same
transaction as the request, once a destroy has succeeded ([Destroying](#destroying)). The backend locks a workspace with a Postgres advisory lock while Terraform writes it. State is backed up
with the app database, and is lost with it.

`PG_CONN_STR` is the app's connection string in libpq form (`host='…' port='…' dbname='…' user='…' password='…'
sslmode='…'`). Npgsql's `SslMode` maps to libpq's: `Disable`, `Allow` and `Prefer` (Npgsql's default) become
`disable`; `Require`, `VerifyCA` and `VerifyFull` become `require`, `verify-ca` and `verify-full`. A database that
needs TLS, such as DigitalOcean Managed Postgres, must say `SslMode=Require` (or stricter) in the connection string.

Catalog modules may not declare a backend of their own or contain `rp_backend.tf`; the API won't start if one does
([catalog.md](catalog.md#what-the-loader-checks)).

So the folder is **scratch space**: losing it loses nothing, and the API can run on a disk that doesn't survive a
restart. `workspaces/` is gitignored. Providers are downloaded into `<Workspaces:Root>/.plugin-cache/`, shared by
every workspace, so after the folder is lost the next job downloads them again.

A destroy uses the catalog's current module, not the one the apply ran. A module change that adds a required variable
without a default would stop older requests being destroyed, so give new variables defaults.

## Logs

Every line Terraform writes to stdout or stderr is saved as a `JobLogLine` (`JobLogLines` table: request ID, time,
`Stream` = `System` / `Stdout` / `Stderr`, text) as it arrives. `System` lines are the runner's own: the command it's
starting (`$ terraform init -input=false -no-color`) and the outcome (`Provisioned.`, `Destroyed.`, `Nothing to
destroy: ...` or `Failed: ...`). A request has one log: a destroy's lines follow its apply's. The log is deleted with
the request once a destroy succeeds.

`terraform output -json`'s stdout is **never** logged, because it holds the sensitive values; the log shows
`$ terraform output -json (values not logged)` instead. If its output can't be parsed, the error doesn't quote it.

### `GET /api/requests/{id}/logs`

Server-sent events. Visible to the owner and to admins; anyone else gets 404, as for `GET /api/requests/{id}`.

| Event | `data` | When |
| --- | --- | --- |
| `status` | `{"status":"Applying"}` | first, then whenever the status changes |
| `log` | `{"id":42,"at":"...","stream":"Stdout","text":"..."}`, with SSE `id: 42` | each line, oldest first |
| `end` | `{"status":"Succeeded"}` | once the request is not `Queued`, `Applying` or `Destroying`; the stream then closes |
| `deleted` | `{}` | instead of `end`, once a destroy has finished and the request (log and all) has been deleted; the stream then closes |

A finished request replays its whole log and ends at once. A reconnect sending `Last-Event-ID` resumes after that
line. The endpoint polls the database every `Runner:LogPollInterval` (default 1 s) while a job runs, because the runner
writes from its own scope. An `AwaitingConfirmation` request ends straight away; open the stream again after confirming.

## Configuration

| Key | Default | |
| --- | --- | --- |
| `Runner:TerraformPath` | `terraform` (looked up on `PATH`) | the CLI to run; the API image pins a version and sets this to `/usr/local/bin/terraform` ([local-development.md](local-development.md#the-images)) |
| `Runner:LogPollInterval` | `00:00:01` | how often the logs endpoint checks for new lines (10 ms to 1 min) |
| `Workspaces:Root` | `workspaces` | where workspaces go, relative to the working directory |

## Testing

`ITerraformProcessRunner` is the only thing that starts a process. Tests swap it for `FakeTerraform`, which records
each command and plays back scripted lines and exit codes, and the test host leaves `TerraformRunner` out so tests
decide when jobs run. Tests run on Postgres, each test host on its own database built by the migrations
([architecture.md](architecture.md#api-layout)), so the runner, the logs endpoint and the test can all use it at once.
`FakeTerraform` never writes state, so `ITerraformStateStore` is swapped for `FakeTerraformState`, where a test says
which requests have state and sees which were deleted; `TerraformStateStoreTests` runs the real store against a test
database (a `states` table like the `pg` backend's) and covers the connection-string conversion, and
`RequestDeleterTests` deletes requests with the real store, to show the request and its state go in one transaction. `TerraformProcessRunnerTests` runs the real wrapper against `/bin/sh` instead of Terraform.
