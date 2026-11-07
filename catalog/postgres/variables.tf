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

# Always created, so the app never has to use defaultdb or the admin user.
variable "database_name" {
  type     = string
  default  = "app"
  nullable = false

  validation {
    condition = (
      can(regex("^[a-z][a-z0-9_]*$", var.database_name)) && length(var.database_name) <= 63 &&
      !contains(["defaultdb", "postgres", "template0", "template1"], var.database_name)
    )
    error_message = "database_name must be lowercase letters, digits and underscores, at most 63 characters, and not defaultdb, postgres, template0 or template1."
  }
}

# Always created; the main outputs are this user's, not doadmin's.
variable "user_name" {
  type     = string
  default  = "app_user"
  nullable = false

  validation {
    condition = (
      can(regex("^[a-z][a-z0-9_]*$", var.user_name)) && length(var.user_name) <= 63 &&
      !contains(["doadmin", "postgres"], var.user_name) && !startswith(var.user_name, "pg_")
    )
    error_message = "user_name must be lowercase letters, digits and underscores, at most 63 characters, not doadmin or postgres, and not start with pg_."
  }
}

# IDs of the user's own droplet requests allowed to connect, through each one's rp-<requestId>
# tag. This module only checks the shape: the request layer (#14) must confirm every ID is a
# droplet request owned by the requesting user before the values reach Terraform.
variable "allowed_droplet_request_ids" {
  type     = list(string)
  default  = []
  nullable = false

  validation {
    condition = alltrue([
      for id in var.allowed_droplet_request_ids :
      can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id))
    ])
    error_message = "Each droplet request ID must be a lowercase GUID."
  }

  validation {
    condition     = length(var.allowed_droplet_request_ids) <= 10
    error_message = "Give at most 10 droplet request IDs."
  }
}

# IPv4 CIDRs allowed to connect. DigitalOcean's trusted sources don't take IPv6. Prefixes shorter
# than /8 are refused so the list can't be used to open the database to the whole internet.
variable "allowed_ip_addresses" {
  type     = list(string)
  default  = []
  nullable = false

  validation {
    condition = alltrue([
      for a in var.allowed_ip_addresses : can(regex("^[0-9.]+/[0-9]+$", a)) && can(cidrhost(a, 0)) && try(tonumber(split("/", a)[1]) >= 8, false)
    ])
    error_message = "Each address must be an IPv4 CIDR from /8 to /32, such as 203.0.113.7/32."
  }

  validation {
    condition     = length(var.allowed_ip_addresses) <= 10
    error_message = "Give at most 10 IP addresses."
  }
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

# The request ID of one of the user's projects (from the request; the API checks it's theirs).
# The module doesn't use it: the runner reads that project's id output and sets rp_project_id.
variable "project_request_id" {
  type    = string
  default = null
}

# Set by the runner from project_request_id, not the request: the project's DigitalOcean ID, or
# "" for the account's default project.
variable "rp_project_id" {
  type    = string
  default = ""
}
