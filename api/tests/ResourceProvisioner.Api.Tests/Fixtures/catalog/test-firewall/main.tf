resource "terraform_data" "firewall" {
  input = var.rules
}
