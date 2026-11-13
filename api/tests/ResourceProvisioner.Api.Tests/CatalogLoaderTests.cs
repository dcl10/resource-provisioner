namespace ResourceProvisioner.Api.Tests;

using Fixtures;
using ResourceCatalog;
using Xunit;

public sealed class CatalogLoaderTests : IDisposable
{
  private readonly TempCatalog _catalog = new();

  public void Dispose() => _catalog.Dispose();

  [Fact]
  public void Load_ReturnsTypesOrderedById_ForFixtureCatalog()
  {
    var entries = CatalogLoader.Load(ApiFactory.FixtureCatalogRoot);

    Assert.Equal(
      ["project", "test-database", "test-firewall", "test-group", "test-server"],
      entries.Select(e => e.Manifest.Id));
    Assert.All(entries, e => Assert.True(File.Exists(Path.Combine(e.ModuleDirectory, "main.tf"))));
  }

  [Fact]
  public void Load_ParsesEveryKeyword_ForFixtureManifest()
  {
    var server = CatalogLoader.Load(ApiFactory.FixtureCatalogRoot).Single(e => e.Manifest.Id == "test-server").Manifest;

    var name = server.Variables.Single(v => v.Name == "name");
    Assert.Equal(VariableType.String, name.Type);
    Assert.True(name.Required);
    Assert.Equal("^[a-z][a-z0-9-]*$", name.Pattern);
    Assert.Equal(20, name.MaxLength);

    var count = server.Variables.Single(v => v.Name == "node_count");
    Assert.Equal((1m, 3m), (count.Minimum!.Value, count.Maximum!.Value));

    var tags = server.Variables.Single(v => v.Name == "tags");
    Assert.Equal(VariableType.String, tags.Items!.Type);

    Assert.True(server.Variables.Single(v => v.Name == "admin_password").Sensitive);
    Assert.True(server.Outputs.Single(o => o.Name == "root_password").Sensitive);
  }

  [Fact]
  public void Load_ReturnsEmpty_WhenCatalogHasNoTypes()
  {
    Assert.Empty(CatalogLoader.Load(_catalog.Root));
  }

  [Fact]
  public void Load_SkipsHiddenFolders_WhenScanning()
  {
    Directory.CreateDirectory(Path.Combine(_catalog.Root, ".terraform"));

    Assert.Empty(CatalogLoader.Load(_catalog.Root));
  }

  [Fact]
  public void Load_LoadsRepositoryCatalog_WithoutErrors()
  {
    // Catches a bad manifest in catalog/ in CI rather than at deploy time.
    var repoCatalog = Path.Combine(FindRepoRoot(), "catalog");

    var entries = CatalogLoader.Load(repoCatalog);

    Assert.All(entries, e => Assert.Equal(Path.GetFileName(e.ModuleDirectory), e.Manifest.Id));
  }

  [Theory]
  [InlineData("api/src/ResourceProvisioner.Api")]
  [InlineData("api")]
  [InlineData("")]
  public void ResolveRoot_FindsRepoCatalog_FromFoldersInsideRepo(string start)
  {
    var repoRoot = FindRepoRoot();

    var resolved = CatalogLoader.ResolveRoot("catalog", Path.Combine(repoRoot, start));

    Assert.Equal(Path.Combine(repoRoot, "catalog"), resolved);
  }

  [Fact]
  public void ResolveRoot_PrefersNearestFolder_WhenSeveralMatch()
  {
    var nested = Path.Combine(_catalog.Root, "a", "b");
    Directory.CreateDirectory(Path.Combine(nested, "catalog"));
    Directory.CreateDirectory(Path.Combine(_catalog.Root, "catalog"));

    Assert.Equal(Path.Combine(nested, "catalog"), CatalogLoader.ResolveRoot("catalog", nested));
  }

  [Fact]
  public void ResolveRoot_ReturnsPathUnderStart_WhenNothingMatches()
  {
    var name = $"no-such-catalog-{Guid.NewGuid():N}";

    Assert.Equal(Path.Combine(_catalog.Root, name), CatalogLoader.ResolveRoot(name, _catalog.Root));
  }

  [Fact]
  public void ResolveRoot_ReturnsAbsolutePathUnchanged()
  {
    Assert.Equal(ApiFactory.FixtureCatalogRoot, CatalogLoader.ResolveRoot(ApiFactory.FixtureCatalogRoot, "/elsewhere"));
  }

  [Fact]
  public void Load_Throws_WhenRootDoesNotExist()
  {
    var ex = Assert.Throws<CatalogException>(() => CatalogLoader.Load(Path.Combine(_catalog.Root, "missing")));

    Assert.Contains("Catalog:Root", ex.Message);
  }

  [Fact]
  public void Load_Throws_WhenManifestIsMissing()
  {
    _catalog.AddType("sample", manifest: null);

    AssertLoadFails("resource.json is missing");
  }

  [Theory]
  [InlineData("main.tf")]
  [InlineData("variables.tf")]
  [InlineData("outputs.tf")]
  public void Load_Throws_WhenModuleFileIsMissing(string file)
  {
    var dir = _catalog.AddType("sample");
    File.Delete(Path.Combine(dir, file));

    AssertLoadFails($"{file} is missing");
  }

  [Theory]
  [InlineData("main.tf", "terraform {\n  backend \"s3\" {}\n}\n")]
  [InlineData("versions.tf", "terraform {\n  required_version = \">= 1.6\"\n  backend \"local\" {\n  }\n}\n")]
  public void Load_Throws_WhenModuleDeclaresBackend(string file, string content)
  {
    var dir = _catalog.AddType("sample");
    File.WriteAllText(Path.Combine(dir, file), content);

    AssertLoadFails($"{file} declares a backend");
  }

  [Fact]
  public void Load_Throws_WhenModuleHasRunnerBackendFile()
  {
    var dir = _catalog.AddType("sample");
    File.WriteAllText(Path.Combine(dir, "rp_backend.tf"), "");

    AssertLoadFails("rp_backend.tf is reserved");
  }

  [Theory]
  [InlineData("{ not json", "not a valid manifest")]
  [InlineData("""{ "id": "sample" }""", "not a valid manifest")]
  [InlineData("""{ "id": "sample", "displayName": "S", "description": "D", "costHint": "C", "extra": 1 }""",
    "not a valid manifest")]
  [InlineData("""{ "id": null, "displayName": "S", "description": "D", "costHint": "C" }""", "not a valid manifest")]
  [InlineData("""{ "id": "other", "displayName": "S", "description": "D", "costHint": "C" }""",
    "must equal the folder name")]
  [InlineData("""{ "id": "sample", "displayName": " ", "description": "D", "costHint": "C" }""",
    "displayName must not be empty")]
  public void Load_Throws_WhenManifestIsInvalid(string manifest, string expected)
  {
    _catalog.AddType("sample", manifest, variablesTf: "", outputsTf: "");

    AssertLoadFails(expected);
  }

  [Fact]
  public void Load_Throws_WhenIdIsNotKebabCase()
  {
    _catalog.AddType("Bad_Id", TempCatalog.ValidManifest.Replace("\"sample\"", "\"Bad_Id\"", StringComparison.Ordinal));

    AssertLoadFails("must be kebab-case");
  }

  [Theory]
  [InlineData("""{ "name": "v", "type": "text", "description": "d" }""", "not a valid manifest")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "allowedValue": ["a"] }""",
    "not a valid manifest")]
  [InlineData("""{ "name": "Bad-Name", "type": "string", "description": "d" }""", "must be snake_case")]
  [InlineData("""{ "name": "v", "type": "string", "description": "" }""", "description must not be empty")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "required": true, "default": "a" }""",
    "required variable can't have a default")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "sensitive": true, "required": true }""",
    "sensitive variable can't be required")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "sensitive": true, "default": "a" }""",
    "sensitive variable can't have a default")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "pattern": "([" }""",
    "not a valid regular expression")]
  [InlineData("""{ "name": "v", "type": "integer", "description": "d", "pattern": "^a$" }""",
    "pattern only applies to strings")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "minimum": 1 }""",
    "minimum/maximum only applies to integers and numbers")]
  [InlineData("""{ "name": "v", "type": "integer", "description": "d", "minimum": 5, "maximum": 1 }""",
    "minimum must not be greater than maximum")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "minLength": 5, "maxLength": 1 }""",
    "minLength must not be greater than maxLength")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "maxLength": -1 }""",
    "must not be negative")]
  [InlineData("""{ "name": "v", "type": "boolean", "description": "d", "allowedValues": [true] }""",
    "allowedValues only applies to strings, integers and numbers")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "allowedValues": [] }""",
    "allowedValues must not be empty")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "allowedValues": ["a", 1] }""",
    "allowed value 1 is not of type string")]
  [InlineData("""{ "name": "v", "type": "integer", "description": "d", "allowedValues": [1.5] }""",
    "allowed value 1.5 is not of type integer")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "allowedValues": ["a", "a"] }""",
    "listed more than once")]
  [InlineData("""{ "name": "v", "type": "array", "description": "d" }""", "an array needs items")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "items": { "type": "string" } }""",
    "items only applies to arrays")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "minItems": 1 }""",
    "minItems/maxItems only applies to arrays")]
  [InlineData("""{ "name": "v", "type": "object", "description": "d", "properties": [] }""",
    "an object needs properties")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "properties": [] }""",
    "properties only applies to objects")]
  [InlineData("""
    { "name": "v", "type": "object", "description": "d",
      "properties": [{ "name": "p", "type": "string", "description": "d", "sensitive": true }] }
    """, "only top-level variables can be sensitive")]
  [InlineData("""
    { "name": "v", "type": "object", "description": "d",
      "properties": [{ "name": "p", "type": "string" }] }
    """, "v.p: description must not be empty")]
  [InlineData("""
    { "name": "v", "type": "array", "description": "d",
      "items": { "type": "object", "properties": [{ "name": "p", "type": "integer", "description": "d", "minLength": 1 }] } }
    """, "v[].p: minLength/maxLength only applies to strings")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "allowedValues": ["a"], "default": "b" }""",
    "default of v: Must be one of: a.")]
  [InlineData("""{ "name": "v", "type": "integer", "description": "d", "default": "1" }""",
    "default of v: Must be a whole number.")]
  [InlineData("""
    { "name": "v", "type": "array", "description": "d", "items": { "type": "string", "pattern": "^[a-z]+$" },
      "default": ["ok", "NO"] }
    """, "default of v[1]: Must match the pattern")]
  public void Load_Throws_WhenVariableIsInvalid(string variableJson, string expected)
  {
    _catalog.AddTypeWithVariable(variableJson);

    AssertLoadFails(expected);
  }

  [Fact]
  public void Load_Throws_WhenVariableNameIsDuplicated()
  {
    _catalog.AddTypeWithVariable("""
      { "name": "v", "type": "string", "description": "d" },
      { "name": "v", "type": "string", "description": "d" }
      """);

    AssertLoadFails("v: name is used more than once");
  }

  [Fact]
  public void Load_Throws_WhenVariableIsNotInVariablesTf()
  {
    _catalog.AddTypeWithVariable("""{ "name": "v", "type": "string", "description": "d" }""", tfName: "other");

    AssertLoadFails("variables.v is not declared in variables.tf");
  }

  [Theory]
  [InlineData(true, false, "sensitive is true in resource.json but false in variables.tf")]
  [InlineData(false, true, "sensitive is false in resource.json but true in variables.tf")]
  public void Load_Throws_WhenVariableSensitivityDiffersFromTerraform(
    bool manifestSensitive,
    bool tfSensitive,
    string expected)
  {
    _catalog.AddTypeWithVariable(
      $$"""{ "name": "v", "type": "string", "description": "d", "sensitive": {{(manifestSensitive ? "true" : "false")}} }""",
      tfSensitive: tfSensitive);

    AssertLoadFails(expected);
  }

  [Fact]
  public void Load_Succeeds_WhenVariablesTfDeclaresExtraRunnerVariables()
  {
    _catalog.AddType("sample", variablesTf: TempCatalog.ValidVariablesTf + "variable \"rp_tags\" {\n}\n");

    Assert.Single(CatalogLoader.Load(_catalog.Root));
  }

  [Fact]
  public void Load_Throws_WhenOutputIsMissingFromManifest()
  {
    _catalog.AddType("sample", outputsTf: TempCatalog.ValidOutputsTf + "output \"secret\" {\n  value = \"x\"\n}\n");

    AssertLoadFails("output 'secret' in outputs.tf is missing from resource.json");
  }

  [Fact]
  public void Load_Throws_WhenOutputIsMissingFromOutputsTf()
  {
    _catalog.AddType("sample", outputsTf: "");

    AssertLoadFails("outputs.id is not declared in outputs.tf");
  }

  [Fact]
  public void Load_Throws_WhenSensitiveOutputIsNotMarkedInManifest()
  {
    _catalog.AddType("sample", outputsTf: "output \"id\" {\n  value     = \"x\"\n  sensitive = true\n}\n");

    AssertLoadFails("outputs.id: sensitive is false in resource.json but true in outputs.tf");
  }

  [Theory]
  [InlineData("""[["a"]]""", "atLeastOneOf[0]: name at least two variables")]
  [InlineData("""[["a", "a"]]""", "atLeastOneOf[0]: a variable is listed more than once")]
  [InlineData("""[["a", "missing"]]""", "atLeastOneOf[0]: 'missing' is not a variable")]
  [InlineData("""[["a", "name"]]""", "atLeastOneOf[0]: 'name' is required, so the rule always holds")]
  [InlineData("""[["a", "secret"]]""", "atLeastOneOf[0]: 'secret' is sensitive, so the agent can't set it")]
  public void Load_Throws_WhenAtLeastOneOfIsInvalid(string groups, string expected)
  {
    _catalog.AddType(
      "sample",
      $$"""
        {
          "id": "sample", "displayName": "S", "description": "D", "costHint": "C",
          "variables": [
            { "name": "name", "type": "string", "description": "d", "required": true },
            { "name": "a", "type": "string", "description": "d" },
            { "name": "secret", "type": "string", "description": "d", "sensitive": true }
          ],
          "atLeastOneOf": {{groups}},
          "outputs": [{ "name": "id", "description": "ID." }]
        }
        """,
      """
        variable "name" {
        }

        variable "a" {
        }

        variable "secret" {
          sensitive = true
        }

        """);

    AssertLoadFails(expected);
  }

  [Theory]
  [InlineData("""{ "name": "v", "type": "integer", "description": "d", "references": "sample" }""",
    "a reference must be a string or an array of strings")]
  [InlineData("""
    { "name": "v", "type": "array", "description": "d", "references": "sample", "items": { "type": "integer" } }
    """,
    "a reference must be a string or an array of strings")]
  [InlineData("""
    { "name": "v", "type": "array", "description": "d",
      "items": { "type": "object", "properties": [
        { "name": "w", "type": "string", "description": "d", "references": "sample" }] } }
    """, "references (w) must be top-level variables")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "references": "Not_An_Id" }""",
    "references 'Not_An_Id', which isn't a type ID")]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "references": "nope" }""",
    "references 'nope', which isn't a type in the catalog")]
  public void Load_Throws_WhenReferenceIsInvalid(string variable, string expected)
  {
    _catalog.AddTypeWithVariable(variable);

    AssertLoadFails(expected);
  }

  [Fact]
  public void Load_Throws_WhenModuleDoesNotTakeTheDependencyVariable()
  {
    _catalog.AddTypeWithVariable(
      """{ "name": "group_request_id", "type": "string", "description": "d", "references": "sample" }""",
      "group_request_id");

    AssertLoadFails("the runner sets rp_group_id from it, but variables.tf doesn't declare it");
  }

  [Fact]
  public void Load_Throws_WhenTheReferencedTypeHasNoIdOutput()
  {
    _catalog.AddType(
      "other",
      TempCatalog.ValidManifest
        .Replace("\"sample\"", "\"other\"", StringComparison.Ordinal)
        .Replace("""{ "name": "id", "description": "ID." }""", """{ "name": "x", "description": "X." }""",
          StringComparison.Ordinal),
      outputsTf: "output \"x\" {\n  value = \"x\"\n}\n");
    _catalog.AddTypeWithVariable(
      """{ "name": "other_request_id", "type": "string", "description": "d", "references": "other" }""",
      "other_request_id");
    File.AppendAllText(Path.Combine(_catalog.Root, "sample", "variables.tf"), "variable \"rp_other_id\" {\n}\n");

    AssertLoadFails("the runner reads the 'id' output of 'other', which has none");
  }

  [Fact]
  public void Load_Throws_WhenAModuleTakesTheProjectId_ButThereIsNoProjectType()
  {
    _catalog.AddType("sample");
    File.AppendAllText(Path.Combine(_catalog.Root, "sample", "variables.tf"), "variable \"rp_project_id\" {\n}\n");

    AssertLoadFails("variables.tf declares rp_project_id, which the runner reads from the 'id' output of the " +
                    "'project' type, but there's no such type or output.");
  }

  [Fact]
  public void Load_Throws_WhenAReferenceWouldSetTheProjectId()
  {
    _catalog.AddTypeWithVariable(
      """{ "name": "project_request_id", "type": "string", "description": "d", "references": "sample" }""",
      "project_request_id");
    File.AppendAllText(Path.Combine(_catalog.Root, "sample", "variables.tf"), "variable \"rp_project_id\" {\n}\n");

    AssertLoadFails("project_request_id: the runner sets rp_project_id from the request's project");
  }

  [Fact]
  public void Load_RecordsTheModulesVariables()
  {
    var server = CatalogLoader.Load(ApiFactory.FixtureCatalogRoot).Single(e => e.Manifest.Id == "test-server");

    Assert.Contains("rp_project_id", server.ModuleVariables!);
    Assert.Contains("rp_tags", server.ModuleVariables!);
    Assert.Contains("name", server.ModuleVariables!);
  }

  [Theory]
  [InlineData("""{ "name": "v", "type": "string", "description": "d", "references": "sample" }""")]
  [InlineData("""
    { "name": "v", "type": "array", "description": "d", "references": "sample", "items": { "type": "string" } }
    """)]
  public void Load_Accepts_StringAndStringArrayReferences(string variable)
  {
    _catalog.AddTypeWithVariable(variable);

    var entry = Assert.Single(CatalogLoader.Load(_catalog.Root));
    Assert.Equal("sample", Assert.Single(entry.Manifest.Variables).References);
  }

  [Fact]
  public void Load_ParsesAtLeastOneOf_ForFixtureManifest()
  {
    var database = CatalogLoader.Load(ApiFactory.FixtureCatalogRoot)
      .Single(e => e.Manifest.Id == "test-database").Manifest;

    var group = Assert.Single(database.AtLeastOneOf!);
    Assert.Equal(["allowed_droplet_request_ids", "allowed_ip_addresses"], group);
  }

  [Fact]
  public void Load_ListsEveryProblem_WhenSeveralTypesAreInvalid()
  {
    _catalog.AddType("first", manifest: null);
    _catalog.AddType("second", manifest: "{");

    var ex = Assert.Throws<CatalogException>(() => CatalogLoader.Load(_catalog.Root));

    Assert.Contains(Path.Combine("first", "resource.json"), ex.Message);
    Assert.Contains(Path.Combine("second", "resource.json"), ex.Message);
  }

  private void AssertLoadFails(string expected)
  {
    var ex = Assert.Throws<CatalogException>(() => CatalogLoader.Load(_catalog.Root));
    Assert.Contains(expected, ex.Message);
  }

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "catalog")) &&
                                Directory.Exists(Path.Combine(dir.FullName, "api"))))
    {
      dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root above the test output.");
  }
}
