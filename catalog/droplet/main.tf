resource "digitalocean_droplet" "this" {
  name       = var.name
  region     = var.region
  size       = var.size
  image      = var.image
  monitoring = true
  tags       = var.rp_tags

  # The user's public key is installed for root by cloud-init rather than as a
  # DigitalOcean account SSH key: the account is shared by every user, and the same
  # key can only be added to it once. Password login over SSH is turned off.
  user_data = "#cloud-config\n${yamlencode({
    ssh_authorized_keys = [var.ssh_public_key]
    ssh_pwauth          = false
  })}"
}
