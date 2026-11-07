variable "rules" {
  type = list(object({
    protocol = string
    port     = number
    sources  = optional(list(string), ["0.0.0.0/0"])
  }))
}

variable "group_request_id" {
  type    = string
  default = null
}

variable "rp_group_id" {
  type    = string
  default = ""
}
