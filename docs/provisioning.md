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
2. Prepare the workspace (below).
3. Run, in the workspace, with `-input=false -no-color`:
   `terraform init`, `terraform plan -out=plan.tfplan`, `terraform apply plan.tfplan`, `terraform output -json`.
   A non-zero exit stops the job.
4. Split the outputs: an output is sensitive if the manifest **or** Terraform marks it sensitive. Non-sensitive ones go
   in `ProvisioningRequests.Outputs`, sensitive ones in `RequestSensitiveOutputs`.
5. Move to `Succeeded`, or to `Failed` with a last log line saying why (`Failed: terraform plan exited with code 1.`).
   A failed apply may have created part of the module; the request can still be destroyed.

The job never sees the user or the LLM: everything it runs comes from the vetted module and the validated variables.

## Destroying

`POST /api/requests/{id}/destroy` is allowed from `Succeeded` or `Failed` (a failed apply or a failed destroy), by the
owner or an admin (see [architecture.md](architecture.md#ownership)); any other status answers 409. It moves the
request to `Destroying` and queues it. The runner then:

1. Checks the workspace for `terraform.tfstate`. Without it, Terraform never created anything (the apply failed before
   `apply`, or before the workspace was made), so it logs `Nothing to destroy: ...` and skips to step 3.
2. Runs `terraform init` and `terraform destroy -auto-approve` in the workspace, with `-input=false -no-color`. It uses
   the workspace's own copy of the module and its tfvars, so a type removed from the catalog can still be destroyed.
3. Clears the request's outputs and deletes its sensitive outputs (the credentials they held no longer work), then
   moves it to `Destroyed`, logging `Destroyed.`.

If `destroy` exits non-zero the request moves to `Failed` with its outputs kept, since some resources may still exist,
and it can be destroyed again. `Destroying` counts as active, so the active-request count drops only once the request is
`Destroyed`.

One gap: an apply killed (the API stopped) before Terraform first wrote its state can leave resources with no state to
destroy them from. Everything the runner creates is tagged `rp-<requestId>`, so an admin can find such leftovers in the
DigitalOcean console by that tag.

### Credentials

Credentials go to Terraform **only in its environment**, never in tfvars, the workspace or the log:

| Variable | From | Used by |
| --- | --- | --- |
| `DIGITALOCEAN_TOKEN` | `DigitalOcean:Token` (required; without it the job fails before running anything) | the `digitalocean` provider |
| `SPACES_ACCESS_KEY_ID`, `SPACES_SECRET_ACCESS_KEY` | `Spaces:AccessId`, `Spaces:SecretKey` (set only when both are) | `digitalocean_spaces_bucket` |

The runner also sets `TF_IN_AUTOMATION=1`, `TF_INPUT=0`, `CHECKPOINT_DISABLE=1` and `TF_PLUGIN_CACHE_DIR` (below).

## Workspaces and state

Each request gets `<Workspaces:Root>/<requestId>/`:

| File | What it is |
| --- | --- |
| the module's files | copied from `catalog/<type>/`, leaving out `resource.json`, `.terraform/`, state, plans and tfvars files |
| `terraform.tfvars.json` | the request's variables (defaults applied), with the runner's `rp_name_suffix` and `rp_tags` laid over them, so a value smuggled into the variables can't replace the forced tags |
| `plan.tfplan` | the saved plan that apply runs |
| `terraform.tfstate` | local state, kept so destroy can run in the same folder |

State is local on purpose: one folder per request keeps every request independent, and nothing else needs to read it.
Preparing a workspace that already has state fails the job instead of overwriting it. `workspaces/` is gitignored and
must be on a persistent volume, or the state, and with it the ability to destroy, is lost: in Docker it's the
`workspaces` volume at `/data/workspaces` ([local-development.md](local-development.md#data)).

Providers are downloaded once into `<Workspaces:Root>/.plugin-cache/`, shared by every workspace.

## Logs

Every line Terraform writes to stdout or stderr is saved as a `JobLogLine` (`JobLogLines` table: request ID, time,
`Stream` = `System` / `Stdout` / `Stderr`, text) as it arrives. `System` lines are the runner's own: the command it's
starting (`$ terraform init -input=false -no-color`) and the outcome (`Provisioned.`, `Destroyed.`, `Nothing to
destroy: ...` or `Failed: ...`). A request has one log: a destroy's lines follow its apply's.

`terraform output -json`'s stdout is **never** logged, because it holds the sensitive values; the log shows
`$ terraform output -json (values not logged)` instead. If its output can't be parsed, the error doesn't quote it.

### `GET /api/requests/{id}/logs`

Server-sent events. Visible to the owner and to admins; anyone else gets 404, as for `GET /api/requests/{id}`.

| Event | `data` | When |
| --- | --- | --- |
| `status` | `{"status":"Applying"}` | first, then whenever the status changes |
| `log` | `{"id":42,"at":"...","stream":"Stdout","text":"..."}`, with SSE `id: 42` | each line, oldest first |
| `end` | `{"status":"Succeeded"}` | once the request is not `Queued`, `Applying` or `Destroying`; the stream then closes |

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
decide when jobs run. `TerraformProcessRunnerTests` runs the real wrapper against `/bin/sh` instead of Terraform.
