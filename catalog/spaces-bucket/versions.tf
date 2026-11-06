terraform {
  required_version = ">= 1.6"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.107"
    }
  }
}

# Credentials come from environment variables set by the runner, never from Terraform variables,
# so they can't reach the manifest or the LLM: DIGITALOCEAN_TOKEN (for the bucket's access key)
# and SPACES_ACCESS_KEY_ID / SPACES_SECRET_ACCESS_KEY (for the bucket itself).
provider "digitalocean" {}
