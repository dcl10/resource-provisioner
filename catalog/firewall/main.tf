locals {
  # The firewall applies to every droplet carrying one of these tags. Only rp-<requestId> tags of
  # the user's own droplet requests may end up here; see droplet_request_ids in variables.tf.
  target_tags = [for id in var.droplet_request_ids : "rp-${id}"]
}

resource "digitalocean_firewall" "this" {
  name = var.name

  # Not var.rp_tags: on a firewall, `tags` picks the droplets it applies to.
  tags = local.target_tags

  # Allow rules only (no `action`), and addresses only, so a rule can't name other droplets, tags
  # or load balancers. ICMP has no ports, so its port_range is dropped; "all" means 1-65535.
  dynamic "inbound_rule" {
    for_each = var.inbound_rules
    content {
      protocol         = inbound_rule.value.protocol
      port_range       = inbound_rule.value.protocol == "icmp" ? null : (inbound_rule.value.port_range == "all" ? "1-65535" : inbound_rule.value.port_range)
      source_addresses = inbound_rule.value.source_addresses
    }
  }

  dynamic "outbound_rule" {
    for_each = var.outbound_rules
    content {
      protocol              = outbound_rule.value.protocol
      port_range            = outbound_rule.value.protocol == "icmp" ? null : (outbound_rule.value.port_range == "all" ? "1-65535" : outbound_rule.value.port_range)
      destination_addresses = outbound_rule.value.destination_addresses
    }
  }
}
