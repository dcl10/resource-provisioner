variable "name" {
  type = string
}

variable "region" {
  type    = string
  default = "nyc3"
}

variable "size" {
  type    = string
  default = "db-s-1vcpu-1gb"
}

# Not "version": Terraform reserves that variable name.
variable "postgres_version" {
  type    = string
  default = "18"
}

# Optional: when null, no database is created and the cluster's defaultdb is used.
variable "database_name" {
  type    = string
  default = null
}

# Optional: when null, no user is created and the cluster's admin user, doadmin, is used.
variable "user_name" {
  type    = string
  default = null
}

# Set by the runner, not the request, so it isn't in resource.json: resource-provisioner,
# rp-<requestId> and rp-owner-<short-sub>.
variable "rp_tags" {
  type    = list(string)
  default = []
}

# Set by the runner, not the request: a short value from the request ID (up to 8 lowercase
# letters or digits), appended to the name so names never clash in the shared account.
variable "rp_name_suffix" {
  type    = string
  default = ""

  validation {
    condition     = can(regex("^[a-z0-9]{0,8}$", var.rp_name_suffix))
    error_message = "rp_name_suffix must be at most 8 lowercase letters or digits."
  }
}
