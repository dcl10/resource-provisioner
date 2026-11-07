namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text.Json;
using System.Text.Json.Nodes;
using ResourceCatalog;

/// <summary>Reads <c>terraform output -json</c> and splits it into the outputs anyone may see and the secrets.</summary>
public static class TerraformOutputs
{
  /// <summary>
  /// Splits <c>terraform output -json</c> (<c>{ "name": { "value": ..., "sensitive": bool, "type": ... } }</c>) into
  /// plain values by name. An output is sensitive if the manifest or Terraform says so.
  /// </summary>
  /// <param name="json">The command's standard output.</param>
  /// <param name="manifest">The request's resource type.</param>
  /// <returns>The non-sensitive outputs and the sensitive ones, each as a JSON object of values.</returns>
  /// <exception cref="JobFailedException">The output isn't the JSON object Terraform writes.</exception>
  public static (JsonObject Plain, JsonObject Sensitive) Split(string json, ResourceManifest manifest)
  {
    JsonObject outputs;
    try
    {
      outputs = JsonNode.Parse(json)?.AsObject() ?? [];
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
    {
      // Don't echo the text: it holds the sensitive values.
      throw new JobFailedException("terraform output -json didn't return a JSON object.");
    }

    var sensitiveNames = manifest.Outputs.Where(o => o.Sensitive).Select(o => o.Name).ToHashSet();
    JsonObject plain = [];
    JsonObject sensitive = [];
    foreach (var (name, output) in outputs)
    {
      var value = output?["value"]?.DeepClone();
      var terraformSensitive = output?["sensitive"]?.GetValueKind() == JsonValueKind.True;
      (sensitiveNames.Contains(name) || terraformSensitive ? sensitive : plain)[name] = value;
    }

    return (plain, sensitive);
  }
}
