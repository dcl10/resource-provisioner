terraform {
  required_version = ">= 1.17"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.107"
    }
  }

  # State stays in a local terraform.tfstate (gitignored). Keep it safe: it's how you change or tear down the host.
}

# Your own token, from the DIGITALOCEAN_TOKEN environment variable. Not the app's token, which only goes in the
# host's .env.
provider "digitalocean" {}
