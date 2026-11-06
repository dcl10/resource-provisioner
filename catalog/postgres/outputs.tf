output "id" {
  value = digitalocean_database_cluster.this.id
}

output "host" {
  value = digitalocean_database_cluster.this.host
}

output "port" {
  value = digitalocean_database_cluster.this.port
}

output "database" {
  value = digitalocean_database_db.this.name
}

output "user" {
  value = digitalocean_database_user.this.name
}

output "password" {
  value     = digitalocean_database_user.this.password
  sensitive = true
}

# Built here rather than taken from the cluster's uri, which is always for doadmin and defaultdb.
output "uri" {
  value = format(
    "postgresql://%s:%s@%s:%d/%s?sslmode=require",
    urlencode(digitalocean_database_user.this.name),
    urlencode(digitalocean_database_user.this.password),
    digitalocean_database_cluster.this.host,
    digitalocean_database_cluster.this.port,
    digitalocean_database_db.this.name,
  )
  sensitive = true
}

# doadmin's credentials. Users have no DigitalOcean console access, so this is their only admin
# route, for example to grant the app user CREATE on a schema (PostgreSQL 15+).
output "admin_password" {
  value     = digitalocean_database_cluster.this.password
  sensitive = true
}

# The cluster's own URI: doadmin on defaultdb, sslmode=require.
output "admin_uri" {
  value     = digitalocean_database_cluster.this.uri
  sensitive = true
}
