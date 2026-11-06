variable "name" {
  type = string
}

variable "size" {
  type    = string
  default = "small"
}

variable "node_count" {
  type    = number
  default = 1
}

variable "backups" {
  type    = bool
  default = false
}

variable "tags" {
  type    = list(string)
  default = []
}

variable "admin_password" {
  type      = string
  default   = null
  sensitive = true
}

# Set by the runner, not the request, so it isn't in resource.json.
variable "rp_tags" {
  type    = list(string)
  default = []
}
