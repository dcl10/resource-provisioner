resource "digitalocean_droplet" "this" {
  # <name>-<rp_name_suffix>; just <name> when the suffix is empty (only outside the runner).
  name       = join("-", compact([var.name, var.rp_name_suffix]))
  region     = var.region
  size       = var.size
  image      = var.image
  monitoring = true
  tags       = var.rp_tags

  # The user's public key is installed for root by cloud-init rather than as a
  # DigitalOcean account SSH key: the account is shared by every user, and the same
  # key can only be added to it once. Password login over SSH is turned off.
  user_data = "#cloud-config\n${yamlencode({
    ssh_authorized_keys = [var.ssh_public_key]
    ssh_pwauth          = false
  })}"
}

# Only with a project: puts the droplet in it. Destroying this moves the droplet back to the
# default project, just before the droplet itself is destroyed.
resource "digitalocean_project_resources" "this" {
  count     = var.rp_project_id == "" ? 0 : 1
  project   = var.rp_project_id
  resources = [digitalocean_droplet.this.urn]
}
