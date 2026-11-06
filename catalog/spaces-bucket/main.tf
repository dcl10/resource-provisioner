# Created with the account's Spaces keys, which the provider reads from SPACES_ACCESS_KEY_ID and
# SPACES_SECRET_ACCESS_KEY (set by the runner). Those keys reach every bucket in the account, so
# they never leave the server.
resource "digitalocean_spaces_bucket" "this" {
  # <name>-<rp_name_suffix>; just <name> when the suffix is empty (only outside the runner).
  # Bucket names are unique across all of DigitalOcean, so the suffix also makes a clash with
  # another account's bucket unlikely.
  name   = join("-", compact([var.name, var.rp_name_suffix]))
  region = var.region

  # Always private: public-read isn't offered, so nothing in the bucket can be listed or read
  # without a key.
  acl = "private"

  # Destroying the request deletes the bucket's contents too, so a non-empty bucket can't be left
  # behind (and billed) after the user asked for it to go.
  force_destroy = true

  # No tags: digitalocean_spaces_bucket has no tags argument, so var.rp_tags isn't used. The
  # bucket's owner is tracked by its request.
}

# The user's own key, limited to this bucket. It's created through the DigitalOcean API (with
# DIGITALOCEAN_TOKEN) and is the only credential the user gets.
resource "digitalocean_spaces_key" "this" {
  name = digitalocean_spaces_bucket.this.name

  grant {
    bucket     = digitalocean_spaces_bucket.this.name
    permission = "readwrite"
  }
}
