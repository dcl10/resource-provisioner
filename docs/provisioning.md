# Provisioning

> Destroy is added by #16.

The Terraform runner turns a confirmed request into real DigitalOcean resources. It lives in
`api/src/ResourceProvisioner.Api/Services/Provisioning/` and runs inside the API process.

## The runner

```
POST /api/requests/{id}/confirm ─▶ status Queued ─▶ IJobDispatcher (in-memory channel)
                                                         │
                                  TerraformRunner (BackgroundService) takes one ID at a time
                                                         │
                                  ITerraformJobProcessor, in a fresh DI scope per job
```

- **One job at a time.** `TerraformRunner` reads request IDs from `IJobDispatcher` and finishes each job before taking
  the next, so two applies never run at once (a spend cap, see [architecture.md](architecture.md#spend-caps)).
- **The status decides the work.** A job is just a request ID; the processor looks at the request's status when it gets
  there. `Queued` is applied, and anything else is skipped, so a duplicate or stale entry is harmless.
- **Restarts.** The queue is in memory. When the runner starts it first moves any request left `Applying` or
  `Destroying` to `Failed`, logging that the API stopped mid-job and the request should be destroyed to clean up, and
  then re-queues every `Queued` request in the order it was confirmed. Confirm saves the status before queueing, so a
  crash in between loses nothing.

## A job

1. Move the request `Queued → Applying` (the status is a concurrency token, so a request that changed meanwhile is
   skipped).
2. Prepare the workspace (below).
3. Run, in the workspace, with `-input=false -no-color`:
   `terraform init`, `terraform plan -out=plan.tfplan`, `terraform apply plan.tfplan`, `terraform output -json`.
   A non-zero exit stops the job.
4. Split the outputs: an output is sensitive if the manifest **or** Terraform marks it sensitive. Non-sensitive ones go
   in `ProvisioningRequests.Outputs`, sensitive ones in `RequestSensitiveOutputs`.
5. Move to `Succeeded`, or to `Failed` with a last log line saying why (`Failed: terraform plan exited with code 1.`).
   A failed apply may have created part of the module; the request can still be destroyed (#16).

The job never sees the user or the LLM: everything it runs comes from the vetted module and the validated variables.

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
| `terraform.tfstate` | local state, kept so destroy (#16) can run in the same folder |

State is local on purpose: one folder per request keeps every request independent, and nothing else needs to read it.
Preparing a workspace that already has state fails the job instead of overwriting it. `workspaces/` is gitignored and
must be on a persistent volume when deployed (#20), or the state, and with it the ability to destroy, is lost.

Providers are downloaded once into `<Workspaces:Root>/.plugin-cache/`, shared by every workspace.

## Logs

Every line Terraform writes to stdout or stderr is saved as a `JobLogLine` (`JobLogLines` table: request ID, time,
`Stream` = `System` / `Stdout` / `Stderr`, text) as it arrives. `System` lines are the runner's own: the command it's
starting (`$ terraform init -input=false -no-color`) and the final `Provisioned.` or `Failed: ...`.

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
| `Runner:TerraformPath` | `terraform` (looked up on `PATH`) | the CLI to run; the container pins a version (#20) |
| `Runner:LogPollInterval` | `00:00:01` | how often the logs endpoint checks for new lines (10 ms to 1 min) |
| `Workspaces:Root` | `workspaces` | where workspaces go, relative to the working directory |

## Testing

`ITerraformProcessRunner` is the only thing that starts a process. Tests swap it for `FakeTerraform`, which records
each command and plays back scripted lines and exit codes, and the test host leaves `TerraformRunner` out so tests
decide when jobs run. `TerraformProcessRunnerTests` runs the real wrapper against `/bin/sh` instead of Terraform.
