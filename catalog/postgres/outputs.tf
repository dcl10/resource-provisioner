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
  value = local.database
}

output "user" {
  value = local.user
}

output "password" {
  value     = local.password
  sensitive = true
}

# Built here rather than taken from the cluster's uri, which is always for doadmin and defaultdb.
output "uri" {
  value = format(
    "postgresql://%s:%s@%s:%d/%s?sslmode=require",
    urlencode(local.user),
    urlencode(local.password),
    digitalocean_database_cluster.this.host,
    digitalocean_database_cluster.this.port,
    local.database,
  )
  sensitive = true
}
