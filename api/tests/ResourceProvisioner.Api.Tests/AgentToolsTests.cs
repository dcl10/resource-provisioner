namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Fixtures;
using ResourceCatalog;
using Services.Agent;
using Xunit;

/// <summary>The agent's tool definitions, generated from manifests for strict tool use.</summary>
public class AgentToolsTests
{
  /// <summary>Strict tool use's limits, combined across every strict tool in a request.</summary>
  private const int MaxStrictTools = 20;
  private const int MaxOptionalParameters = 24;
  private const int MaxUnionParameters = 16;

  private static readonly HashSet<string> StrictKeywords =
    ["type", "description", "properties", "required", "additionalProperties", "items", "enum", "anyOf"];

  private readonly CatalogService _fixtures = new(CatalogLoader.Load(ApiFactory.FixtureCatalogRoot));

  [Fact]
  public void For_ReturnsTheFourTools_InAFixedOrder()
  {
    Assert.Equal(
      [AgentTools.ListResourceTypes, AgentTools.GetResourceSpec, AgentTools.ListMyResources, AgentTools.ProposeRequest],
      AgentTools.For(_fixtures).Select(t => t.Name));
  }

  [Fact]
  public void For_SendsEveryToolStrict_ExceptProposeRequest()
  {
    var strict = AgentTools.For(_fixtures).ToDictionary(t => t.Name, t => t.Strict);

    Assert.False(strict[AgentTools.ProposeRequest]);
    Assert.True(strict[AgentTools.ListResourceTypes]);
    Assert.True(strict[AgentTools.GetResourceSpec]);
    Assert.True(strict[AgentTools.ListMyResources]);
  }

  [Fact]
  public void For_UsesOnlyStrictKeywords_AndClosesEveryObject()
  {
    foreach (var tool in AgentTools.For(_fixtures))
    {
      Walk(tool.InputSchema, schema =>
      {
        Assert.All(schema.Select(p => p.Key), key => Assert.Contains(key, StrictKeywords));
        // A nullable parameter's type is an array, such as ["string", "null"].
        if (schema["type"] is JsonValue type && type.GetValue<string>() == "object")
        {
          Assert.False(schema["additionalProperties"]!.GetValue<bool>(), $"{tool.Name}: an object isn't closed");
        }
      });
    }
  }

  [Fact]
  public void VariablesSchema_LeavesOutSensitiveVariables_AndRequiresRequiredOnes()
  {
    var schema = AgentTools.VariablesSchema(_fixtures.Find("test-server")!);

    var properties = schema["properties"]!.AsObject().Select(p => p.Key).ToList();
    Assert.Equal(["name", "size", "node_count", "backups", "tags"], properties);
    Assert.Equal(["name"], schema["required"]!.AsArray().Select(r => r!.GetValue<string>()));
  }

  [Fact]
  public void VariablesSchema_MovesUnsupportedConstraintsIntoDescriptions()
  {
    var properties = AgentTools.VariablesSchema(_fixtures.Find("test-server")!)["properties"]!;

    var name = properties["name"]!["description"]!.GetValue<string>();
    Assert.Contains("^[a-z][a-z0-9-]*$", name);
    Assert.Contains("at most 20 characters", name);
    Assert.Contains("Range: 1 to 3.", properties["node_count"]!["description"]!.GetValue<string>());
    Assert.Contains("Items: at most 2.", properties["tags"]!["description"]!.GetValue<string>());
    Assert.Contains("Default: \"small\".", properties["size"]!["description"]!.GetValue<string>());
    Assert.Equal(["small", "medium"], properties["size"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
  }

  [Fact]
  public void VariablesSchema_ExplainsWhichRequestsAReferenceTakes()
  {
    var properties = AgentTools.VariablesSchema(_fixtures.Find("test-database")!)["properties"]!;

    var description = properties["allowed_droplet_request_ids"]!["description"]!.GetValue<string>();
    Assert.Contains("Request IDs of the user's own test-server requests", description);
    Assert.Contains("pending ones are fine", description);
  }

  [Fact]
  public void ProposeRequest_OffersEachTypesVariables()
  {
    var propose = AgentTools.For(_fixtures).Single(t => t.Name == AgentTools.ProposeRequest).InputSchema;

    Assert.Equal(
      _fixtures.Types.Select(t => t.Id),
      propose["properties"]!["type"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
    Assert.Equal(_fixtures.Types.Count, propose["properties"]!["variables"]!["anyOf"]!.AsArray().Count);
  }

  [Fact]
  public void ProposeRequest_RequiresANullableProjectId()
  {
    var propose = AgentTools.For(_fixtures).Single(t => t.Name == AgentTools.ProposeRequest).InputSchema;

    Assert.Equal(
      ["string", "null"],
      propose["properties"]![AgentTools.ProjectId]!["type"]!.AsArray().Select(t => t!.GetValue<string>()));
    Assert.Equal(
      ["type", AgentTools.ProjectId, "variables"],
      propose["required"]!.AsArray().Select(r => r!.GetValue<string>()));
  }

  /// <summary>
  /// The real catalog's strict tools must fit strict tool use's limits, or every chat request fails with a 400.
  /// </summary>
  [Fact]
  public void RealCatalog_StaysWithinStrictToolLimits()
  {
    var root = CatalogLoader.ResolveRoot("catalog", AppContext.BaseDirectory);
    var all = AgentTools.For(new CatalogService(CatalogLoader.Load(root)));
    // Only strict schemas count towards the limits.
    var tools = all.Where(t => t.Strict).ToList();

    // propose_request's own parameters (project_id included) are all required, so they would add no optional
    // parameters if it were ever sent strict.
    var propose = all.Single(t => t.Name == AgentTools.ProposeRequest).InputSchema;
    Assert.Equal(
      propose["properties"]!.AsObject().Select(p => p.Key),
      propose["required"]!.AsArray().Select(r => r!.GetValue<string>()));

    var optional = 0;
    var unions = 0;
    foreach (var tool in tools)
    {
      Walk(tool.InputSchema, schema =>
      {
        if (schema["properties"] is JsonObject properties)
        {
          var required = schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ToHashSet();
          optional += properties.Count(p => !required.Contains(p.Key));
        }

        if (schema["anyOf"] is not null || schema["type"] is JsonArray)
        {
          unions++;
        }
      });
    }

    Assert.True(tools.Count <= MaxStrictTools, $"{tools.Count} strict tools");
    Assert.True(optional <= MaxOptionalParameters, $"{optional} optional parameters across the strict tools");
    Assert.True(unions <= MaxUnionParameters, $"{unions} union parameters across the strict tools");
  }

  private static void Walk(JsonObject schema, Action<JsonObject> visit)
  {
    visit(schema);
    foreach (var property in schema["properties"]?.AsObject() ?? [])
    {
      Walk(property.Value!.AsObject(), visit);
    }

    if (schema["items"] is JsonObject items)
    {
      Walk(items, visit);
    }

    foreach (var option in schema["anyOf"]?.AsArray() ?? [])
    {
      Walk(option!.AsObject(), visit);
    }
  }
}
