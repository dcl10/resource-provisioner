variable "name" {
  type = string
}

variable "region" {
  type    = string
  default = "nyc3"
}

variable "size" {
  type    = string
  default = "s-1vcpu-512mb-10gb"
}

variable "image" {
  type    = string
  default = "ubuntu-24-04-x64"
}

variable "ssh_public_key" {
  type = string
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
