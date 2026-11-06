variable "name" {
  type = string
}

variable "allowed_droplet_request_ids" {
  type    = list(string)
  default = []
}

variable "allowed_ip_addresses" {
  type    = list(string)
  default = []
}
