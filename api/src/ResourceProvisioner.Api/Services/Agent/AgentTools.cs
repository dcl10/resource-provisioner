namespace ResourceProvisioner.Api.Services.Agent;

using System.Globalization;
using System.Text.Json.Nodes;
using ResourceCatalog;

/// <summary>
/// The agent's four tools, generated from the catalog's manifests (the single source of truth). Schemas use only the
/// JSON Schema keywords strict tool use supports: types, <c>enum</c>, <c>anyOf</c>, <c>required</c> and
/// <c>additionalProperties: false</c>. Constraints strict mode can't express (patterns, lengths, ranges, item counts,
/// defaults) go into descriptions, and the server checks them in <c>propose_request</c>.
/// <para>
/// <c>propose_request</c> is the one tool sent without <c>strict</c>. Its <c>variables</c> is an <c>anyOf</c> of every
/// type's variables, and strict decoding writes required properties first and commits to whichever branch still
/// matches: after <c>name</c> and <c>region</c> only the Spaces bucket's shape fits, so a droplet's
/// <c>ssh_public_key</c> could never be written. Unconstrained, Claude writes the type it means, and the server's
/// validation (the authority either way) returns any errors for it to fix.
/// </para>
/// </summary>
public static class AgentTools
{
  /// <summary>Lists the catalog's resource types.</summary>
  public const string ListResourceTypes = "list_resource_types";

  /// <summary>One resource type's manifest.</summary>
  public const string GetResourceSpec = "get_resource_spec";

  /// <summary>The user's own requests.</summary>
  public const string ListMyResources = "list_my_resources";

  /// <summary>Validates variables and creates a request awaiting the user's confirmation.</summary>
  public const string ProposeRequest = "propose_request";

  /// <summary>The tool definitions for a catalog.</summary>
  /// <param name="catalog">The resource catalog.</param>
  /// <returns>The four tools, in a fixed order so the prompt cache stays valid.</returns>
  public static IReadOnlyList<AgentTool> For(ICatalogService catalog)
  {
    var typeIds = catalog.Types.Select(t => t.Id).ToList();

    return
    [
      new AgentTool(
        ListResourceTypes,
        "Lists every resource type you can provision, with what each is for and roughly what it costs. Call this " +
        "first in a conversation, before suggesting anything: these are the only types that exist.",
        Object([])),
      new AgentTool(
        GetResourceSpec,
        "Returns one resource type's full spec: every variable you can set (type, description, allowed values, " +
        "patterns, lengths, ranges, defaults, which are required), any rules across variables, and its outputs. " +
        "Call it once you know which type the user wants, before asking about its settings.",
        Object(
          [
            ("type", new JsonObject
            {
              ["type"] = "string", ["enum"] = Strings(typeIds), ["description"] = "The type's ID.",
            }, true),
          ])),
      new AgentTool(
        ListMyResources,
        "Lists the user's own requests: request ID, type, status, variables and non-secret outputs. Call it when " +
        "the user refers to something they already have, or when a variable takes droplet request IDs (such as a " +
        "firewall's droplet_request_ids): only the user's Succeeded droplets can be referenced.",
        Object([])),
      new AgentTool(
        ProposeRequest,
        "Proposes a request for the user to review. The server checks the variables against the spec; if they're " +
        "invalid it returns the errors by field, so fix them (asking the user if needed) and call it again. If " +
        "they're valid it creates the request, awaiting confirmation: nothing is created in DigitalOcean until the " +
        "user clicks Confirm on the card the app shows them. Call it once every required variable is known and the " +
        "user has agreed to the choices; leave out variables to take their defaults. For resources that build on " +
        "each other (a droplet and a firewall on it), propose the one needed first, then reference it by the " +
        "requestId this tool returned: the app provisions them in that order.",
        Object(
        [
          ("type", new JsonObject
          {
            ["type"] = "string", ["enum"] = Strings(typeIds), ["description"] = "The resource type's ID.",
          }, true),
          ("variables", new JsonObject
          {
            ["description"] = "The variables, matching the spec of the chosen type (get_resource_spec).",
            ["anyOf"] = new JsonArray([.. catalog.Types.Select(VariablesSchema)]),
          }, true),
        ]),
        Strict: false),
    ];
  }

  /// <summary>The strict schema for one type's variables: an object of its non-sensitive variables.</summary>
  /// <param name="manifest">The type's manifest.</param>
  /// <returns>The schema.</returns>
  public static JsonObject VariablesSchema(ResourceManifest manifest)
  {
    var schema = PropertiesSchema(manifest.Variables.Where(v => !v.Sensitive));
    schema["description"] = $"Variables for {manifest.Id}.";
    return schema;
  }

  private static JsonObject PropertiesSchema(IEnumerable<VariableSpec> variables) =>
    Object([.. variables.Select(v => (v.Name, WithReference(ValueSchema(v, v.Default), v), v.Required))]);

  /// <summary>Tells the agent which requests a reference variable takes, pending ones included.</summary>
  private static JsonObject WithReference(JsonObject schema, VariableSpec variable)
  {
    if (variable.References is { } type)
    {
      var note = $"Request IDs of the user's own {type} requests, from {ListMyResources} or an earlier " +
                 $"{ProposeRequest} in this conversation; pending ones are fine, as they're provisioned first.";
      schema["description"] = schema["description"] is { } existing ? $"{existing} {note}" : note;
    }

    return schema;
  }

  private static JsonObject ValueSchema(ValueSpec spec, JsonNode? defaultValue)
  {
    JsonObject schema;
    switch (spec.Type)
    {
      case VariableType.Array:
        schema = new JsonObject { ["type"] = "array", ["items"] = ValueSchema(spec.Items!, null) };
        break;
      case VariableType.Object:
        schema = PropertiesSchema(spec.Properties ?? []);
        break;
      default:
        schema = new JsonObject { ["type"] = TypeName(spec.Type) };
        if (spec.AllowedValues is { Count: > 0 } allowed)
        {
          schema["enum"] = new JsonArray([.. allowed.Select(a => a.DeepClone())]);
        }

        break;
    }

    var description = Describe(spec, defaultValue);
    if (description.Length > 0)
    {
      schema["description"] = description;
    }

    return schema;
  }

  /// <summary>The description, followed by the constraints strict mode can't enforce.</summary>
  private static string Describe(ValueSpec spec, JsonNode? defaultValue)
  {
    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(spec.Description))
    {
      parts.Add(spec.Description.Trim());
    }

    if (spec.Pattern is { } pattern)
    {
      parts.Add($"Must match the regular expression {pattern}.");
    }

    if (spec.MinLength is not null || spec.MaxLength is not null)
    {
      parts.Add($"Length: {Range(spec.MinLength, spec.MaxLength)} characters.");
    }

    if (spec.Minimum is not null || spec.Maximum is not null)
    {
      parts.Add($"Range: {Range(spec.Minimum, spec.Maximum)}.");
    }

    if (spec.MinItems is not null || spec.MaxItems is not null)
    {
      parts.Add($"Items: {Range(spec.MinItems, spec.MaxItems)}.");
    }

    if (defaultValue is not null)
    {
      parts.Add($"Default: {defaultValue.ToJsonString()}.");
    }

    return string.Join(' ', parts);
  }

  private static string Range<T>(T? min, T? max)
    where T : struct, IFormattable =>
    (min, max) switch
    {
      ({ } a, { } b) => $"{Format(a)} to {Format(b)}",
      ({ } a, null) => $"at least {Format(a)}",
      (null, { } b) => $"at most {Format(b)}",
      _ => "",
    };

  private static string Format<T>(T value)
    where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

  private static string TypeName(VariableType type) =>
    type switch
    {
      VariableType.String => "string",
      VariableType.Integer => "integer",
      VariableType.Number => "number",
      VariableType.Boolean => "boolean",
      VariableType.Array => "array",
      _ => "object",
    };

  /// <summary>A strict object schema: <c>additionalProperties: false</c>, with its required properties.</summary>
  private static JsonObject Object(IReadOnlyList<(string Name, JsonObject Schema, bool Required)> properties)
  {
    var props = new JsonObject();
    foreach (var (name, schema, _) in properties)
    {
      props[name] = schema;
    }

    return new JsonObject
    {
      ["type"] = "object",
      ["properties"] = props,
      ["required"] = Strings([.. properties.Where(p => p.Required).Select(p => p.Name)]),
      ["additionalProperties"] = false,
    };
  }

  private static JsonArray Strings(IEnumerable<string> values) =>
    new([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);
}
