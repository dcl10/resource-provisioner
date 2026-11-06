resource "digitalocean_database_cluster" "this" {
  # <name>-<rp_name_suffix>; just <name> when the suffix is empty (only outside the runner).
  name       = join("-", compact([var.name, var.rp_name_suffix]))
  engine     = "pg"
  version    = var.postgres_version
  size       = var.size
  region     = var.region
  node_count = 1
  tags       = var.rp_tags
}

# The app's own database and non-admin user, always created. The main outputs are for these;
# doadmin's credentials are only in the admin_* outputs.
resource "digitalocean_database_db" "this" {
  cluster_id = digitalocean_database_cluster.this.id
  name       = var.database_name
}

resource "digitalocean_database_user" "this" {
  cluster_id = digitalocean_database_cluster.this.id
  name       = var.user_name
}

locals {
  # Only rp-<requestId> tags of the user's own droplet requests may end up here; see
  # allowed_droplet_request_ids in variables.tf.
  trusted_tags = [for id in var.allowed_droplet_request_ids : "rp-${id}"]
}

# Trusted sources: with any set, the cluster accepts connections only from them. There is always
# at least one, so the database is never open to the internet.
resource "digitalocean_database_firewall" "this" {
  cluster_id = digitalocean_database_cluster.this.id

  dynamic "rule" {
    for_each = local.trusted_tags
    content {
      type  = "tag"
      value = rule.value
    }
  }

  dynamic "rule" {
    for_each = var.allowed_ip_addresses
    content {
      type  = "ip_addr"
      value = rule.value
    }
  }

  lifecycle {
    # Checked at plan, before anything is created. Not a variable validation, which can't refer to
    # another variable before Terraform 1.9.
    precondition {
      condition     = length(var.allowed_droplet_request_ids) + length(var.allowed_ip_addresses) >= 1
      error_message = "Give at least one trusted source: a droplet request ID in allowed_droplet_request_ids or a CIDR in allowed_ip_addresses."
    }
  }
}
