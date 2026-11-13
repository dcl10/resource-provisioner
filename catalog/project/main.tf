# A DigitalOcean project groups the user's droplets, databases and buckets in the control panel.
# Every request belongs to an app project backed by one of these; the runner passes its id output
# as rp_project_id to every module that declares it. It can't be destroyed while anything is in
# it (the app refuses, and so does DigitalOcean).
resource "digitalocean_project" "this" {
  # <name>-<rp_name_suffix>; just <name> when the suffix is empty (only outside the runner).
  name        = join("-", compact([var.name, var.rp_name_suffix]))
  description = var.description
  purpose     = var.purpose
  environment = var.environment

  # No tags: projects can't be tagged, so var.rp_tags isn't used. The project's owner is tracked
  # by its request.
}
