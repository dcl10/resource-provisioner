output "host" {
  value = terraform_data.database.id
}

output "password" {
  value     = "not-a-real-password"
  sensitive = true
}
