resource "terraform_data" "database" {
  input = {
    name    = var.name
    sources = concat(var.allowed_droplet_request_ids, var.allowed_ip_addresses)
  }
}
