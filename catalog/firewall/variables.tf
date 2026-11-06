variable "name" {
  type = string
}

# IDs of the user's own droplet requests. The firewall targets each one's rp-<requestId> tag.
# This module only checks the shape: the request layer (#14) must confirm every ID is a droplet
# request owned by the requesting user before the values reach Terraform.
variable "droplet_request_ids" {
  type = list(string)

  validation {
    condition = alltrue([
      for id in var.droplet_request_ids :
      can(regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id))
    ])
    error_message = "Each droplet request ID must be a lowercase GUID."
  }

  validation {
    condition     = length(var.droplet_request_ids) >= 1 && length(var.droplet_request_ids) <= 5
    error_message = "Give between 1 and 5 droplet request IDs (DigitalOcean allows 5 tags per firewall)."
  }
}

variable "inbound_rules" {
  type = list(object({
    protocol         = string
    port_range       = string
    source_addresses = list(string)
  }))

  validation {
    condition     = alltrue([for r in var.inbound_rules : contains(["tcp", "udp", "icmp"], r.protocol)])
    error_message = "protocol must be tcp, udp or icmp."
  }

  validation {
    condition = alltrue([
      for r in var.inbound_rules : r.port_range == "all" || try(
        regex("^[0-9]+(-[0-9]+)?$", r.port_range) != "" &&
        tonumber(split("-", r.port_range)[0]) >= 1 &&
        tonumber(reverse(split("-", r.port_range))[0]) <= 65535 &&
        tonumber(split("-", r.port_range)[0]) <= tonumber(reverse(split("-", r.port_range))[0]),
        false
      )
    ])
    error_message = "port_range must be \"all\", a port (22) or a range (8000-9000) within 1-65535, low to high."
  }

  validation {
    condition = alltrue(flatten([
      for r in var.inbound_rules : [for a in r.source_addresses : can(cidrhost(a, 0))]
    ]))
    error_message = "Each source address must be an IPv4 or IPv6 CIDR, such as 203.0.113.7/32 or 0.0.0.0/0."
  }
}

variable "outbound_rules" {
  type = list(object({
    protocol              = string
    port_range            = string
    destination_addresses = list(string)
  }))
  default = [
    { protocol = "tcp", port_range = "all", destination_addresses = ["0.0.0.0/0", "::/0"] },
    { protocol = "udp", port_range = "all", destination_addresses = ["0.0.0.0/0", "::/0"] },
    { protocol = "icmp", port_range = "all", destination_addresses = ["0.0.0.0/0", "::/0"] },
  ]

  validation {
    condition     = alltrue([for r in var.outbound_rules : contains(["tcp", "udp", "icmp"], r.protocol)])
    error_message = "protocol must be tcp, udp or icmp."
  }

  validation {
    condition = alltrue([
      for r in var.outbound_rules : r.port_range == "all" || try(
        regex("^[0-9]+(-[0-9]+)?$", r.port_range) != "" &&
        tonumber(split("-", r.port_range)[0]) >= 1 &&
        tonumber(reverse(split("-", r.port_range))[0]) <= 65535 &&
        tonumber(split("-", r.port_range)[0]) <= tonumber(reverse(split("-", r.port_range))[0]),
        false
      )
    ])
    error_message = "port_range must be \"all\", a port (443) or a range (8000-9000) within 1-65535, low to high."
  }

  validation {
    condition = alltrue(flatten([
      for r in var.outbound_rules : [for a in r.destination_addresses : can(cidrhost(a, 0))]
    ]))
    error_message = "Each destination address must be an IPv4 or IPv6 CIDR, such as 203.0.113.7/32 or ::/0."
  }
}

# Set by the runner, not the request, so it isn't in resource.json: resource-provisioner,
# rp-<requestId> and rp-owner-<short-sub>. Declared so the runner can pass it to every module the
# same way, but deliberately NOT used: a DigitalOcean firewall can't carry tags of its own, and
# digitalocean_firewall's `tags` argument chooses the droplets it applies to. Passing rp_tags there
# would attach this firewall to every droplet tagged resource-provisioner, i.e. every user's.
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
