resource "terraform_data" "server" {
  input = {
    name       = var.name
    size       = var.size
    node_count = var.node_count
    backups    = var.backups
    tags       = concat(var.tags, var.rp_tags)
  }
}
