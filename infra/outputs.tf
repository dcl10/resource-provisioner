output "ip_address" {
  description = "The host's reserved IP."
  value       = digitalocean_reserved_ip.host.ip_address
}

output "app_url" {
  description = "Where colleagues use the app."
  value       = "https://${local.app_host}"
}

output "auth_url" {
  description = "Keycloak's public URL (sign-in only; the admin console isn't served here)."
  value       = "https://${local.auth_host}"
}

output "ssh" {
  description = "SSH in as root."
  value       = "ssh root@${digitalocean_reserved_ip.host.ip_address}"
}

output "admin_console_tunnel" {
  description = "Forward the Keycloak admin console to http://localhost:8080/admin."
  value       = "ssh -N -L 8080:127.0.0.1:8080 root@${digitalocean_reserved_ip.host.ip_address}"
}

output "app_dir" {
  description = "Where the checkout and its root-only .env live on the host."
  value       = local.app_dir
}
