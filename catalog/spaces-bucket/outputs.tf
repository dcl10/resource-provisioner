output "name" {
  value = digitalocean_spaces_bucket.this.name
}

output "region" {
  value = digitalocean_spaces_bucket.this.region
}

output "bucket_domain_name" {
  value = digitalocean_spaces_bucket.this.bucket_domain_name
}

output "endpoint" {
  value = digitalocean_spaces_bucket.this.endpoint
}

# The bucket-scoped key's ID and secret. Sensitive: they're stored apart from the other outputs
# and never reach the LLM.
output "access_key_id" {
  value     = digitalocean_spaces_key.this.access_key
  sensitive = true
}

output "secret_access_key" {
  value     = digitalocean_spaces_key.this.secret_key
  sensitive = true
}
