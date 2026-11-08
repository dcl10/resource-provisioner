variable "name" {
  description = "Prefix for every resource this creates (droplet, volume, firewall, project, tag)."
  type        = string
  default     = "resource-provisioner"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,40}$", var.name))
    error_message = "Use 2 to 41 lowercase letters, digits and hyphens, starting with a letter."
  }
}

variable "region" {
  description = "DigitalOcean region for the host, its volume and reserved IP."
  type        = string
  default     = "lon1"
}

variable "droplet_size" {
  description = "Host size. Keycloak, two Postgres databases, the API and the web app need about 4 GB, plus room to build images."
  type        = string
  default     = "s-2vcpu-4gb"
}

variable "data_volume_size_gb" {
  description = "Size of the block-storage volume holding Docker's data (databases, users' Terraform state, images)."
  type        = number
  default     = 25

  validation {
    condition     = var.data_volume_size_gb >= 10
    error_message = "Use at least 10 GB: the images alone take several."
  }
}

variable "ssh_public_key" {
  description = "Public key allowed to SSH in as root, such as the contents of ~/.ssh/id_ed25519.pub."
  type        = string
}

variable "ssh_allowed_cidrs" {
  description = "Addresses allowed to SSH in, such as [\"203.0.113.7/32\"] for your IP. Never 0.0.0.0/0."
  type        = list(string)

  validation {
    condition     = length(var.ssh_allowed_cidrs) > 0 && alltrue([for c in var.ssh_allowed_cidrs : can(cidrnetmask(c)) && !startswith(c, "0.0.0.0/")])
    error_message = "List one or more IPv4 CIDRs, such as 203.0.113.7/32; SSH can't be open to everyone."
  }
}

variable "domain" {
  description = "A domain hosted in this DigitalOcean account, for app.<domain> and auth.<domain>. Empty uses sslip.io hostnames from the reserved IP."
  type        = string
  default     = ""
}

variable "repo_url" {
  description = "Git repository the host clones and deploys from."
  type        = string
  default     = "https://github.com/dcl10/resource-provisioner.git"
}

variable "repo_ref" {
  description = "Branch or tag the host checks out on first boot."
  type        = string
  default     = "main"
}
