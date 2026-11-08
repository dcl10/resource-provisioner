# Deployment

The POC runs on one DigitalOcean droplet, so colleagues can try it. `infra/` (Terraform, run by you) creates the host;
`docker-compose.prod.yml` (run on the host) runs the app on it. Sign-in stays invite-only, and the Keycloak admin
console is only reachable through an SSH tunnel.

```
Internet ──▶ DO firewall (22 from your IPs; 80, 443) ──▶ droplet ─▶ Caddy ─┬─ app.<host>  → web ─▶ api ─▶ terraform ─▶ DigitalOcean
                                                                          └─ auth.<host> → keycloak (no /admin, no master realm)
                                            127.0.0.1:8080 → keycloak admin console (SSH tunnel only)
```

## What `infra/` creates

Terraform with its own local state, separate from the per-request state the app keeps for users' resources.

| Resource | |
| --- | --- |
| Droplet | Ubuntu 24.04, `s-2vcpu-4gb` by default, monitoring on, tagged `resource-provisioner-host` (never `resource-provisioner`, which marks users' resources) |
| Block-storage volume | Mounted at `/mnt/rp-data`. Holds Docker's data (both databases, with **every request's Terraform state** in the app's, images), the checkout and the root-only `.env`. `prevent_destroy`, so Terraform won't delete it. |
| Reserved IP | The hostnames come from it, so they survive the droplet being replaced |
| Cloud firewall | SSH (22) only from `ssh_allowed_cidrs`; 80 and 443 from anywhere; ICMP |
| SSH key | Yours, for `root` |
| Project | Groups the droplet, volume and IP in the control panel |
| DNS records | Only with `domain` set: `app` and `auth` A records. Without one, the hostnames are `app.<ip>.sslip.io` and `auth.<ip>.sslip.io` (sslip.io resolves them to the IP; no DNS to manage). |

On first boot, cloud-init (`infra/cloud-init.yaml.tftpl`) mounts the volume, adds 2 GB of swap, installs Docker from
Docker's apt repository with its data on the volume, clones this repo to `/mnt/rp-data/resource-provisioner` and runs
`deploy/init-env.sh`, which creates `.env` (mode 600) with the hostnames filled in and the database passwords,
`AUTH_SECRET` and a temporary Keycloak admin password generated.

## What runs on the host

`docker-compose.prod.yml`, built on the host from the checkout:

| Service | |
| --- | --- |
| `caddy` | HTTPS for both hostnames, certificates from Let's Encrypt (`ACME_EMAIL`), renewed automatically. `app.<host>` → `web` (events streamed unbuffered); `auth.<host>` → `keycloak`, except `/admin` and `/realms/master`, which answer 404 (`deploy/Caddyfile`). |
| `keycloak` | Production mode (`start`) on Postgres, behind Caddy (`KC_PROXY_HEADERS=xforwarded`). Users and tokens use `https://auth.<host>`; containers use `http://keycloak:8080`; the admin console uses `http://localhost:8080` (`KC_HOSTNAME_ADMIN`), published on the host's loopback only. |
| `postgres-app`, `postgres-keycloak` | Not published |
| `migrate`, `api`, `web` | The images from [local-development.md](local-development.md#the-images), configured from `.env` |

**The realm** is imported on Keycloak's first start, with production values from `.env` through placeholders in
`keycloak/realm-export.json`: `rp-web`'s redirect, web-origin and post-logout URIs become `https://app.<host>`, and the
SMTP settings come from `SMTP_*`. Locally the placeholders fall back to `http://localhost:3000` and MailHog. The import
only happens once, so later changes to those values are made in the admin console.

## What you need

- A DigitalOcean account and **your own** API token, for `infra/` (in `DIGITALOCEAN_TOKEN` on your machine).
- A **separate** token for the app, which creates users' resources. It sits in the host's `.env`; it can manage
  anything its scopes allow, the host included, so use a token you can revoke on its own (with custom scopes limited
  to droplets, firewalls, databases, tags and Spaces if your account offers them).
- Terraform 1.17 or later, an SSH key, and your public IP (`curl -4 ifconfig.me`).
- An SMTP account for invitation emails: host, port, user, password and a verified sender. Any provider works
  (Postmark: `smtp.postmarkapp.com`, 587, the server token as user and password; Mailgun: `smtp.mailgun.org`, 587;
  Amazon SES: `email-smtp.<region>.amazonaws.com`, 587, SMTP credentials from IAM; SendGrid: `smtp.sendgrid.net`, 587,
  user `apikey`).
- An Anthropic API key.

## First deployment

1. **Create the host.** From `infra/`:

   ```bash
   cp terraform.tfvars.example terraform.tfvars   # your SSH public key and IP
   export DIGITALOCEAN_TOKEN=...                  # your token
   terraform init
   terraform plan                                 # read it
   terraform apply
   terraform output                               # app_url, auth_url, ssh, admin_console_tunnel
   ```

   Keep `terraform.tfstate` safe (it's gitignored): it's how you change or remove the host later.
2. **Wait for first boot**: `ssh root@<ip> cloud-init status --wait` (a few minutes).
3. **Fill in `.env`**: `ssh root@<ip>`, then edit `/mnt/rp-data/resource-provisioner/.env`. Set `ACME_EMAIL`, the
   `SMTP_*` settings, `ANTHROPIC_API_KEY` and the app's `DIGITALOCEAN_TOKEN` (and the Spaces keys for buckets). Leave
   the two `KEYCLOAK_*_CLIENT_SECRET` blank for now. Compose refuses to start without the hostnames, `ACME_EMAIL`, the
   SMTP host and sender, or the generated values.
4. **Deploy**: `/mnt/rp-data/resource-provisioner/deploy/deploy.sh`. The first build takes several minutes. Keycloak
   imports the realm with the production URLs and SMTP settings, and Caddy fetches the certificates.
5. **Permanent Keycloak admin**: `deploy/keycloak-admin.sh <your-admin-username>` asks for a password, points the
   admin console at the tunnel address, creates that admin in the master realm and deletes the temporary one. Then
   delete `KC_BOOTSTRAP_ADMIN_PASSWORD` from `.env`.
6. **Client secrets**: on your machine, open the tunnel (`terraform output -raw admin_console_tunnel`, then
   `http://localhost:8080/admin`), sign in as that admin, and copy the `rp-web` and `rp-api` secrets
   (realm **resource-provisioner** → **Clients** → **Credentials**) into `.env`. Run `deploy/deploy.sh` again.
7. **First app admin**: through the tunnel, create yourself in the **resource-provisioner** realm with `rp-admin`, as
   in [auth-setup.md](auth-setup.md#bootstrapping-the-first-admin), and sign in at the `app_url`.
8. **Billing alert**: in the DigitalOcean control panel, **Billing → Billing alerts**, set a monthly threshold.
   Terraform can't: the provider has no billing-alert resource.
9. **Check it**: run the [end-to-end checklist](local-development.md#end-to-end-checklist) against the `app_url`.

## Deploying updates

```bash
ssh root@<ip> /mnt/rp-data/resource-provisioner/deploy/deploy.sh          # main
ssh root@<ip> /mnt/rp-data/resource-provisioner/deploy/deploy.sh v1.2.0   # a branch or tag
```

It fetches, checks out the ref, rebuilds the images and restarts what changed; `migrate` applies any new migrations
before the API starts. A deploy restarts the API, which fails any job that was applying at that moment (see
[provisioning.md](provisioning.md#the-runner)), so deploy when nothing is provisioning.

There's no CI deploy. A GitHub Action could run the same command over SSH, with a deploy key and the host's IP as
secrets and the firewall opened to GitHub's runners; it isn't set up.

**Upgrading from local Terraform state.** Before Terraform state moved into the app database, it lived in the API's
`workspaces` volume. A host deployed before then must have every request destroyed (or never applied) before it
deploys this version: the runner no longer reads local state, so it couldn't destroy those resources. After deploying,
the old volume holds nothing the app uses; remove it with `docker volume ls` and `docker volume rm
<project>_workspaces`.

## Admin access

| What | How |
| --- | --- |
| Shell | `ssh root@<ip>`, only from `ssh_allowed_cidrs` |
| Keycloak admin console | `ssh -N -L 8080:127.0.0.1:8080 root@<ip>`, then `http://localhost:8080/admin`. The public `auth.<host>` answers 404 for `/admin` and for the master realm, so nobody can reach the console or try the admin's password from outside. |
| Logs | `cd /mnt/rp-data/resource-provisioner && docker compose -f docker-compose.prod.yml logs -f api web keycloak` |
| `.env` | `/mnt/rp-data/resource-provisioner/.env`, root-only. After a change, `docker compose -f docker-compose.prod.yml up -d` applies it. |

To let someone else SSH in, add their IP to `ssh_allowed_cidrs` and `terraform apply`.

## Data and backups

Everything that matters is on the volume: both databases (the app's holds the Terraform state of every user's
resource, in its `terraform_state` schema), the checkout and `.env`. The API keeps nothing it needs on its own disk:
each job rebuilds its Terraform workspace from the catalog and the database
([provisioning.md](provisioning.md#workspaces-and-state)). Replacing the droplet (`terraform apply -replace=digitalocean_droplet.host`, for example to
apply a changed cloud-init) reattaches the volume, and the new droplet's first boot finds the checkout and `.env` and
leaves them alone; run `deploy/deploy.sh` afterwards.

Droplet backups wouldn't include the volume, so take **volume snapshots** instead (control panel: **Volumes →
Take snapshot**, or `doctl compute volume snapshot <volume-id> --snapshot-name <name>`), at least before
changing the infrastructure. Losing the volume orphans every resource users created: they keep costing money until
someone deletes them by their `resource-provisioner` tag.

## Changing or removing the host

- **Settings** (size, region, SSH addresses, domain): edit `terraform.tfvars`, `terraform plan`, `terraform apply`.
  A changed region or a domain means new hostnames: update `APP_HOST` / `AUTH_HOST` in `.env`, and `rp-web`'s URIs
  in the admin console.
- **Cloud-init** changes are ignored for the existing droplet (it only runs at first boot); replace the droplet to
  apply one.
- **Tearing down**: destroy every user's resource from the app first. Then, because the volume is protected, remove
  its `prevent_destroy` on purpose before `terraform destroy`.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Compose says a variable "is missing a value" | That setting is required in `.env`. |
| Keycloak restarts with "Invalid sender address" | `SMTP_FROM` was empty at its first start. Fix `.env`, then reset Keycloak's database (`docker compose -f docker-compose.prod.yml down`, remove the `postgres-keycloak-data` volume, deploy again) only if nobody has signed up yet; otherwise set the sender in the admin console. |
| Browsers warn about the certificate | Caddy couldn't get one: `docker compose -f docker-compose.prod.yml logs caddy`. Port 80 must be open for the challenge, and the hostnames must resolve to the reserved IP. sslip.io hostnames share Let's Encrypt's limits with other users; with a domain you avoid that. |
| The admin console loops or fails to sign in through the tunnel | `deploy/keycloak-admin.sh` hasn't been run, so the master realm still points at the public hostname, which blocks it. |
| Sign-in fails after the first deploy | The client secrets in `.env` are blank or stale (step 6). |
