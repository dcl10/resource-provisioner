output "id" {
  value = terraform_data.server.id
}

output "ipv4_address" {
  value = "192.0.2.1"
}

output "root_password" {
  value     = "not-a-real-password"
  sensitive = true
}
