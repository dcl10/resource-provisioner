variable "name" {
  type = string
}

variable "description" {
  type    = string
  default = ""
}

variable "purpose" {
  type    = string
  default = "Web Application"
}

variable "environment" {
  type    = string
  default = "Development"
}

# Set by the runner, not the request, so it isn't in resource.json. Declared like every module's,
# but unused: projects can't be tagged.
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
