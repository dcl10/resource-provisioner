# The bucket name before the suffix. Spaces names are DNS labels: 3 to 63 lowercase letters,
# digits and hyphens. With the hyphen and an 8-character suffix, name can be at most 54.
variable "name" {
  type = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", var.name)) && length(var.name) >= 3 && length(var.name) <= 54
    error_message = "name must be 3 to 54 lowercase letters, digits and hyphens, starting and ending with a letter or digit."
  }
}

variable "region" {
  type    = string
  default = "nyc3"

  validation {
    condition     = contains(["nyc3", "sfo3", "ams3", "lon1", "fra1", "sgp1"], var.region)
    error_message = "region must be nyc3, sfo3, ams3, lon1, fra1 or sgp1."
  }
}

# Set by the runner, not the request, so it isn't in resource.json: resource-provisioner,
# rp-<requestId> and rp-owner-<short-sub>. Declared so the runner can pass it to every module the
# same way, but not used: neither digitalocean_spaces_bucket nor digitalocean_spaces_key takes
# tags. The bucket's owner is tracked by its request.
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
