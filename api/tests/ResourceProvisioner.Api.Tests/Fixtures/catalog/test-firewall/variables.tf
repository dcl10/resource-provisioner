variable "rules" {
  type = list(object({
    protocol = string
    port     = number
    sources  = optional(list(string), ["0.0.0.0/0"])
  }))
}
