locals {
  # sslip.io resolves app.203-0-113-7.sslip.io to 203.0.113.7, so no DNS is needed without a domain.
  base_domain = var.domain != "" ? var.domain : "${replace(digitalocean_reserved_ip.host.ip_address, ".", "-")}.sslip.io"
  app_host    = "app.${local.base_domain}"
  auth_host   = "auth.${local.base_domain}"

  # Where cloud-init mounts the data volume, and keeps the checkout, .env and Docker's data on it.
  data_mount = "/mnt/rp-data"
  app_dir    = "${local.data_mount}/resource-provisioner"
}

# Not "resource-provisioner", which is the tag on every resource the app creates for users.
resource "digitalocean_tag" "host" {
  name = "${var.name}-host"
}

resource "digitalocean_ssh_key" "admin" {
  name       = "${var.name}-admin"
  public_key = var.ssh_public_key
}

# A fixed address, so the hostnames (and Keycloak's issuer) survive the droplet being replaced.
resource "digitalocean_reserved_ip" "host" {
  region = var.region
}

# Docker's data lives here: both databases, every request's Terraform state, images. A rebuilt droplet reattaches it,
# so nothing is lost. Losing it would orphan every resource users have created.
resource "digitalocean_volume" "data" {
  name                    = "${var.name}-data"
  region                  = var.region
  size                    = var.data_volume_size_gb
  initial_filesystem_type = "ext4"
  description             = "Docker data, the checkout and .env for ${var.name}"

  lifecycle {
    prevent_destroy = true
  }
}

resource "digitalocean_droplet" "host" {
  name       = var.name
  region     = var.region
  size       = var.droplet_size
  image      = "ubuntu-24-04-x64"
  ssh_keys   = [digitalocean_ssh_key.admin.fingerprint]
  volume_ids = [digitalocean_volume.data.id]
  monitoring = true
  tags       = [digitalocean_tag.host.id]

  user_data = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    volume_device = "/dev/disk/by-id/scsi-0DO_Volume_${digitalocean_volume.data.name}"
    data_mount    = local.data_mount
    app_dir       = local.app_dir
    repo_url      = var.repo_url
    repo_ref      = var.repo_ref
    app_host      = local.app_host
    auth_host     = local.auth_host
  })

  lifecycle {
    # cloud-init runs once, at first boot. A later change to it would replace the droplet, so it's ignored;
    # replace the droplet on purpose (terraform apply -replace=digitalocean_droplet.host) to apply one.
    ignore_changes = [user_data]
  }
}

resource "digitalocean_reserved_ip_assignment" "host" {
  ip_address = digitalocean_reserved_ip.host.ip_address
  droplet_id = digitalocean_droplet.host.id
}

resource "digitalocean_firewall" "host" {
  name        = "${var.name}-host"
  droplet_ids = [digitalocean_droplet.host.id]

  inbound_rule {
    protocol         = "tcp"
    port_range       = "22"
    source_addresses = var.ssh_allowed_cidrs
  }

  inbound_rule {
    protocol         = "tcp"
    port_range       = "80"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  inbound_rule {
    protocol         = "tcp"
    port_range       = "443"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  inbound_rule {
    protocol         = "icmp"
    source_addresses = ["0.0.0.0/0", "::/0"]
  }

  outbound_rule {
    protocol              = "tcp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }

  outbound_rule {
    protocol              = "udp"
    port_range            = "1-65535"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }

  outbound_rule {
    protocol              = "icmp"
    destination_addresses = ["0.0.0.0/0", "::/0"]
  }
}

resource "digitalocean_project" "host" {
  name        = var.name
  description = "The host for the resource-provisioner POC. Resources users provision are tagged resource-provisioner instead."
  purpose     = "Web Application"
  environment = "Production"
  resources = [
    digitalocean_droplet.host.urn,
    digitalocean_volume.data.urn,
    digitalocean_reserved_ip.host.urn,
  ]
}

# Only with a domain; sslip.io needs no records.
resource "digitalocean_record" "app" {
  count  = var.domain != "" ? 1 : 0
  domain = var.domain
  type   = "A"
  name   = "app"
  value  = digitalocean_reserved_ip.host.ip_address
  ttl    = 300
}

resource "digitalocean_record" "auth" {
  count  = var.domain != "" ? 1 : 0
  domain = var.domain
  type   = "A"
  name   = "auth"
  value  = digitalocean_reserved_ip.host.ip_address
  ttl    = 300
}
