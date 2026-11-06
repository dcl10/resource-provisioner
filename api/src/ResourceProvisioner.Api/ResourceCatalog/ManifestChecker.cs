namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Checks a parsed manifest for problems the JSON shape alone can't catch: bad names, keywords on the wrong type,
/// defaults that fail their own spec, and sensitive variables that the agent would be asked for.
/// </summary>
internal static partial class ManifestChecker
{
  /// <summary>Returns every problem with <paramref name="manifest"/>; empty when it's valid.</summary>
  /// <param name="manifest">The parsed manifest.</param>
  /// <param name="folderName">The name of the folder it was read from, which must equal its ID.</param>
  public static List<string> Check(ResourceManifest manifest, string folderName)
  {
    var problems = new List<string>();

    if (!IdRegex().IsMatch(manifest.Id))
    {
      problems.Add($"id '{manifest.Id}' must be kebab-case (lowercase letters, digits and single hyphens).");
    }
    else if (manifest.Id != folderName)
    {
      problems.Add($"id '{manifest.Id}' must equal the folder name '{folderName}'.");
    }

    RequireText(manifest.DisplayName, "displayName", problems);
    RequireText(manifest.Description, "description", problems);
    RequireText(manifest.CostHint, "costHint", problems);

    CheckFields(manifest.Variables, "variables", "", topLevel: true, problems);
    CheckOutputs(manifest.Outputs, problems);
    CheckDropletReferences(manifest.Variables, problems);
    CheckAtLeastOneOf(manifest, problems);

    return problems;
  }

  /// <summary>
  /// Whether a variable references the user's own droplets by request ID, by its name: <c>droplet_request_ids</c>
  /// or <c>&lt;purpose&gt;_droplet_request_ids</c>. The API checks every ID in such a variable belongs to a
  /// provisioned droplet of the requesting user's.
  /// </summary>
  /// <param name="name">The variable's name.</param>
  /// <returns>True for a droplet-reference variable.</returns>
  public static bool IsDropletReference(string name) =>
    name == DropletReferenceName || name.EndsWith($"_{DropletReferenceName}", StringComparison.Ordinal);

  private const string DropletReferenceName = "droplet_request_ids";

  // The ownership check reads top-level arrays of strings only, so a reference anywhere else would go unchecked.
  private static void CheckDropletReferences(IReadOnlyList<VariableSpec> variables, List<string> problems)
  {
    foreach (var variable in variables.OfType<VariableSpec>())
    {
      if (IsDropletReference(variable.Name) &&
          (variable.Type != VariableType.Array || variable.Items?.Type != VariableType.String))
      {
        problems.Add($"{variable.Name}: a droplet reference must be an array of strings (request IDs).");
      }

      foreach (var field in NestedFields(variable).Where(f => IsDropletReference(f.Name)))
      {
        problems.Add($"{variable.Name}: droplet references ({field.Name}) must be top-level variables.");
      }
    }
  }

  private static IEnumerable<VariableSpec> NestedFields(ValueSpec spec)
  {
    var properties = spec.Properties ?? spec.Items?.Properties ?? [];
    foreach (var field in properties.OfType<VariableSpec>())
    {
      yield return field;
      foreach (var nested in NestedFields(field))
      {
        yield return nested;
      }
    }
  }

  private static void CheckAtLeastOneOf(ResourceManifest manifest, List<string> problems)
  {
    var groups = manifest.AtLeastOneOf ?? [];
    for (var i = 0; i < groups.Count; i++)
    {
      var path = $"atLeastOneOf[{i}]";
      if (groups[i] is not { } group || group.Count < 2)
      {
        problems.Add($"{path}: name at least two variables.");
        continue;
      }

      if (group.Distinct().Count() != group.Count)
      {
        problems.Add($"{path}: a variable is listed more than once.");
      }

      foreach (var name in group)
      {
        var variable = manifest.Variables.OfType<VariableSpec>().FirstOrDefault(v => v.Name == name);
        if (variable is null)
        {
          problems.Add($"{path}: '{name}' is not a variable.");
        }
        else if (variable.Required)
        {
          problems.Add($"{path}: '{name}' is required, so the rule always holds.");
        }
        else if (variable.Sensitive)
        {
          problems.Add($"{path}: '{name}' is sensitive, so the agent can't set it.");
        }
      }
    }
  }

  private static void CheckFields(
    IReadOnlyList<VariableSpec> fields,
    string listName,
    string pathPrefix,
    bool topLevel,
    List<string> problems)
  {
    var seen = new HashSet<string>();
    for (var i = 0; i < fields.Count; i++)
    {
      if (fields[i] is not { } field)
      {
        problems.Add($"{Join(pathPrefix, listName)}[{i}] must not be null.");
        continue;
      }

      var path = Join(pathPrefix, field.Name);
      if (!NameRegex().IsMatch(field.Name))
      {
        problems.Add($"{path}: name must be snake_case (lowercase letters, digits and underscores).");
      }

      if (!seen.Add(field.Name))
      {
        problems.Add($"{path}: name is used more than once.");
      }

      CheckField(field, path, topLevel, problems);
    }
  }

  private static void CheckField(VariableSpec field, string path, bool topLevel, List<string> problems)
  {
    RequireText(field.Description, $"{path}: description", problems);

    if (field.Required && field.Default is not null)
    {
      problems.Add($"{path}: a required variable can't have a default.");
    }

    if (field.Sensitive)
    {
      if (!topLevel)
      {
        problems.Add($"{path}: only top-level variables can be sensitive.");
      }

      // The agent never sees a sensitive variable, so it couldn't collect a required one, and a default would be a
      // secret committed to the repo.
      if (field.Required)
      {
        problems.Add($"{path}: a sensitive variable can't be required.");
      }

      if (field.Default is not null)
      {
        problems.Add($"{path}: a sensitive variable can't have a default.");
      }
    }

    var before = problems.Count;
    CheckSpec(field, path, problems);

    // Only check the default once the spec itself is sound, or the validator could trip over it.
    if (field.Default is not null && problems.Count == before)
    {
      var errors = new ValidationErrors();
      VariableValidator.ValidateValue(field, field.Default, path, errors);
      problems.AddRange(errors.Describe().Select(e => $"default of {e}"));
    }
  }

  private static void CheckSpec(ValueSpec spec, string path, List<string> problems)
  {
    var type = spec.Type;
    var isString = type == VariableType.String;
    var isNumber = type is VariableType.Integer or VariableType.Number;
    var isArray = type == VariableType.Array;
    var isObject = type == VariableType.Object;

    if (spec.AllowedValues is { } allowed)
    {
      CheckAllowedValues(allowed, type, path, problems, isString, isNumber);
    }

    if (spec.Pattern is { } pattern)
    {
      OnlyFor(isString, "pattern", "strings", path, problems);
      if (!IsValidRegex(pattern))
      {
        problems.Add($"{path}: pattern '{pattern}' is not a valid regular expression.");
      }
    }

    if (spec.MinLength is not null || spec.MaxLength is not null)
    {
      OnlyFor(isString, "minLength/maxLength", "strings", path, problems);
      CheckRange(spec.MinLength, spec.MaxLength, "minLength", "maxLength", path, problems);
    }

    if (spec.Minimum is not null || spec.Maximum is not null)
    {
      OnlyFor(isNumber, "minimum/maximum", "integers and numbers", path, problems);
      if (spec.Minimum > spec.Maximum)
      {
        problems.Add($"{path}: minimum must not be greater than maximum.");
      }
    }

    if (spec.MinItems is not null || spec.MaxItems is not null)
    {
      OnlyFor(isArray, "minItems/maxItems", "arrays", path, problems);
      CheckRange(spec.MinItems, spec.MaxItems, "minItems", "maxItems", path, problems);
    }

    if (isArray && spec.Items is null)
    {
      problems.Add($"{path}: an array needs items.");
    }
    else if (spec.Items is { } items)
    {
      OnlyFor(isArray, "items", "arrays", path, problems);
      CheckSpec(items, $"{path}[]", problems);
    }

    if (isObject && (spec.Properties is null || spec.Properties.Count == 0))
    {
      problems.Add($"{path}: an object needs properties.");
    }
    else if (spec.Properties is { } properties)
    {
      OnlyFor(isObject, "properties", "objects", path, problems);
      CheckFields(properties, "properties", path, topLevel: false, problems);
    }
  }

  private static void CheckAllowedValues(
    IReadOnlyList<JsonNode> allowed,
    VariableType type,
    string path,
    List<string> problems,
    bool isString,
    bool isNumber)
  {
    OnlyFor(isString || isNumber, "allowedValues", "strings, integers and numbers", path, problems);
    if (allowed.Count == 0)
    {
      problems.Add($"{path}: allowedValues must not be empty.");
      return;
    }

    var seen = new HashSet<string>();
    foreach (var value in allowed)
    {
      var matchesType = type switch
      {
        VariableType.String => VariableValidator.ReadString(value) is not null,
        VariableType.Integer => VariableValidator.ReadNumber(value) is { } n && n == decimal.Truncate(n),
        VariableType.Number => VariableValidator.ReadNumber(value) is not null,
        _ => true,
      };
      var text = value?.ToJsonString() ?? "null";
      if (!matchesType)
      {
        problems.Add($"{path}: allowed value {text} is not of type {type.ToString().ToLowerInvariant()}.");
      }

      if (!seen.Add(text))
      {
        problems.Add($"{path}: allowed value {text} is listed more than once.");
      }
    }
  }

  private static void CheckOutputs(IReadOnlyList<OutputSpec> outputs, List<string> problems)
  {
    var seen = new HashSet<string>();
    for (var i = 0; i < outputs.Count; i++)
    {
      if (outputs[i] is not { } output)
      {
        problems.Add($"outputs[{i}] must not be null.");
        continue;
      }

      var path = $"outputs.{output.Name}";
      if (!NameRegex().IsMatch(output.Name))
      {
        problems.Add($"{path}: name must be snake_case (lowercase letters, digits and underscores).");
      }

      if (!seen.Add(output.Name))
      {
        problems.Add($"{path}: name is used more than once.");
      }

      RequireText(output.Description, $"{path}: description", problems);
    }
  }

  private static void CheckRange(int? min, int? max, string minName, string maxName, string path, List<string> problems)
  {
    if (min < 0 || max < 0)
    {
      problems.Add($"{path}: {minName} and {maxName} must not be negative.");
    }

    if (min > max)
    {
      problems.Add($"{path}: {minName} must not be greater than {maxName}.");
    }
  }

  private static void OnlyFor(bool applies, string keyword, string types, string path, List<string> problems)
  {
    if (!applies)
    {
      problems.Add($"{path}: {keyword} only applies to {types}.");
    }
  }

  private static void RequireText(string? value, string what, List<string> problems)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      problems.Add($"{what} must not be empty.");
    }
  }

  private static bool IsValidRegex(string pattern)
  {
    try
    {
      _ = new Regex(pattern);
      return true;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  private static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

  [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
  private static partial Regex IdRegex();

  [GeneratedRegex("^[a-z][a-z0-9_]*$")]
  private static partial Regex NameRegex();
}
