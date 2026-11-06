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

resource "digitalocean_database_db" "this" {
  count = var.database_name == null ? 0 : 1

  cluster_id = digitalocean_database_cluster.this.id
  name       = var.database_name
}

resource "digitalocean_database_user" "this" {
  count = var.user_name == null ? 0 : 1

  cluster_id = digitalocean_database_cluster.this.id
  name       = var.user_name
}

# The connection details are for the requested database and user when given, otherwise
# for the cluster's defaultdb and doadmin.
locals {
  database = var.database_name == null ? digitalocean_database_cluster.this.database : digitalocean_database_db.this[0].name
  user     = var.user_name == null ? digitalocean_database_cluster.this.user : digitalocean_database_user.this[0].name
  password = var.user_name == null ? digitalocean_database_cluster.this.password : digitalocean_database_user.this[0].password
}
