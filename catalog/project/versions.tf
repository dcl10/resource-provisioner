terraform {
  required_version = ">= 1.6"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.107"
    }
  }
}

# The token comes from the DIGITALOCEAN_TOKEN environment variable set by the runner,
# never from a Terraform variable, so it can't reach the manifest or the LLM.
provider "digitalocean" {}
