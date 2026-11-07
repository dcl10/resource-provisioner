# A DigitalOcean project groups the user's droplets, databases and buckets in the control panel. Those
# types take its request ID as project_request_id; the runner passes them its id output as
# rp_project_id. It can't be destroyed while anything is in it (the app refuses, and so does
# DigitalOcean).
resource "digitalocean_project" "this" {
  # <name>-<rp_name_suffix>; just <name> when the suffix is empty (only outside the runner).
  name        = join("-", compact([var.name, var.rp_name_suffix]))
  description = var.description
  purpose     = var.purpose
  environment = var.environment

  # No tags: projects can't be tagged, so var.rp_tags isn't used. The project's owner is tracked
  # by its request.
}
