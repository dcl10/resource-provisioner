namespace ResourceProvisioner.Api.Tests.Fixtures;

/// <summary>
/// A throwaway catalog folder for loader tests, deleted on dispose. Each type gets a manifest and module files that
/// match it unless a test overrides them.
/// </summary>
public sealed class TempCatalog : IDisposable
{
  /// <summary>A valid manifest body for type <c>sample</c>, with one string variable and one output.</summary>
  public const string ValidManifest = """
    {
      "id": "sample",
      "displayName": "Sample",
      "description": "A sample type.",
      "costHint": "Free.",
      "variables": [
        { "name": "name", "type": "string", "description": "Name.", "required": true }
      ],
      "outputs": [
        { "name": "id", "description": "ID." }
      ]
    }
    """;

  /// <summary><c>variables.tf</c> matching <see cref="ValidManifest"/>.</summary>
  public const string ValidVariablesTf = """
    variable "name" {
      type = string
    }

    """;

  /// <summary><c>outputs.tf</c> matching <see cref="ValidManifest"/>.</summary>
  public const string ValidOutputsTf = """
    output "id" {
      value = "x"
    }

    """;

  /// <summary>Creates an empty catalog folder under the temp directory.</summary>
  public TempCatalog()
  {
    Root = Path.Combine(Path.GetTempPath(), $"rp-catalog-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Root);
  }

  /// <summary>The catalog folder.</summary>
  public string Root { get; }

  /// <summary>Adds a type folder. Pass null for a file to leave it out.</summary>
  /// <returns>The type's folder.</returns>
  public string AddType(
    string folder,
    string? manifest = ValidManifest,
    string? variablesTf = ValidVariablesTf,
    string? outputsTf = ValidOutputsTf,
    string? mainTf = "")
  {
    var dir = Path.Combine(Root, folder);
    Directory.CreateDirectory(dir);
    Write(dir, "resource.json", manifest);
    Write(dir, "variables.tf", variablesTf);
    Write(dir, "outputs.tf", outputsTf);
    Write(dir, "main.tf", mainTf);
    return dir;
  }

  /// <summary>
  /// Adds a <c>sample</c> type whose only variable is <paramref name="variableJson"/>; <c>variables.tf</c> declares
  /// <paramref name="tfName"/>.
  /// </summary>
  public void AddTypeWithVariable(string variableJson, string tfName = "v", bool tfSensitive = false) =>
    AddType(
      "sample",
      ValidManifest.Replace(
        """{ "name": "name", "type": "string", "description": "Name.", "required": true }""",
        variableJson,
        StringComparison.Ordinal),
      $"variable \"{tfName}\" {{\n{(tfSensitive ? "  sensitive = true\n" : "")}}}\n");

  /// <inheritdoc />
  public void Dispose()
  {
    try
    {
      Directory.Delete(Root, recursive: true);
    }
    catch (IOException)
    {
      // A leftover temp folder isn't worth failing a test over.
    }
  }

  private static void Write(string dir, string file, string? content)
  {
    if (content is not null)
    {
      File.WriteAllText(Path.Combine(dir, file), content);
    }
  }
}
