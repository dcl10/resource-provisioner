namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Checks values against manifest specs and applies defaults. Used for request variables and, when the catalog
/// loads, for each manifest's own defaults.
/// </summary>
internal static class VariableValidator
{
  private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

  /// <summary>Checks an object's fields against <paramref name="specs"/>; returns them with defaults applied.</summary>
  public static JsonObject ValidateObject(
    IReadOnlyList<VariableSpec> specs,
    JsonObject? input,
    string pathPrefix,
    ValidationErrors errors)
  {
    var result = new JsonObject();

    if (input is not null)
    {
      foreach (var (key, _) in input)
      {
        if (!specs.Any(s => s.Name == key))
        {
          errors.Add(Join(pathPrefix, key), "Unknown field.");
        }
      }
    }

    foreach (var spec in specs)
    {
      var path = Join(pathPrefix, spec.Name);
      JsonNode? value = null;
      input?.TryGetPropertyValue(spec.Name, out value);

      // An explicit null counts as not given, so the default applies.
      if (value is null)
      {
        if (spec.Required)
        {
          errors.Add(path, "Required.");
        }
        else if (spec.Default is not null)
        {
          result[spec.Name] = spec.Default.DeepClone();
        }

        continue;
      }

      var normalized = ValidateValue(spec, value, path, errors);
      if (normalized is not null)
      {
        result[spec.Name] = normalized;
      }
    }

    return result;
  }

  /// <summary>Checks one value against <paramref name="spec"/>; returns a copy with nested defaults applied.</summary>
  public static JsonNode? ValidateValue(ValueSpec spec, JsonNode value, string path, ValidationErrors errors) =>
    spec.Type switch
    {
      VariableType.String => ValidateString(spec, value, path, errors),
      VariableType.Integer or VariableType.Number => ValidateNumber(spec, value, path, errors),
      VariableType.Boolean => ValidateBoolean(value, path, errors),
      VariableType.Array => ValidateArray(spec, value, path, errors),
      VariableType.Object => ValidateObjectValue(spec, value, path, errors),
      _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Type, "Unknown variable type."),
    };

  /// <summary>Reads a JSON number as a decimal, or null if it isn't one or is out of range.</summary>
  public static decimal? ReadNumber(JsonNode? value) =>
    value is JsonValue v && v.GetValueKind() == JsonValueKind.Number &&
    decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
      ? number
      : null;

  /// <summary>Reads a JSON string, or null if the value isn't one.</summary>
  public static string? ReadString(JsonNode? value) =>
    value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

  private static JsonValue? ValidateString(ValueSpec spec, JsonNode value, string path, ValidationErrors errors)
  {
    var text = ReadString(value);
    if (text is null)
    {
      errors.Add(path, "Must be a string.");
      return null;
    }

    var before = errors.Count;
    if (spec.AllowedValues is { } allowed && !allowed.Any(a => ReadString(a) == text))
    {
      errors.Add(path, $"Must be one of: {FormatAllowed(allowed)}.");
    }

    if (spec.MinLength is { } minLength && text.Length < minLength)
    {
      errors.Add(path, $"Must be at least {minLength} characters.");
    }

    if (spec.MaxLength is { } maxLength && text.Length > maxLength)
    {
      errors.Add(path, $"Must be at most {maxLength} characters.");
    }

    if (spec.Pattern is { } pattern && !MatchesPattern(text, pattern))
    {
      errors.Add(path, $"Must match the pattern {pattern}.");
    }

    return errors.Count == before ? JsonValue.Create(text) : null;
  }

  private static JsonNode? ValidateNumber(ValueSpec spec, JsonNode value, string path, ValidationErrors errors)
  {
    var number = ReadNumber(value);
    var isInteger = spec.Type == VariableType.Integer;
    if (number is not { } n || (isInteger && n != decimal.Truncate(n)))
    {
      errors.Add(path, isInteger ? "Must be a whole number." : "Must be a number.");
      return null;
    }

    var before = errors.Count;
    if (spec.AllowedValues is { } allowed && !allowed.Any(a => ReadNumber(a) == n))
    {
      errors.Add(path, $"Must be one of: {FormatAllowed(allowed)}.");
    }

    if (spec.Minimum is { } minimum && n < minimum)
    {
      errors.Add(path, $"Must be at least {minimum.ToString(CultureInfo.InvariantCulture)}.");
    }

    if (spec.Maximum is { } maximum && n > maximum)
    {
      errors.Add(path, $"Must be at most {maximum.ToString(CultureInfo.InvariantCulture)}.");
    }

    return errors.Count == before ? value.DeepClone() : null;
  }

  private static JsonNode? ValidateBoolean(JsonNode value, string path, ValidationErrors errors)
  {
    if (value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
    {
      return value.DeepClone();
    }

    errors.Add(path, "Must be true or false.");
    return null;
  }

  private static JsonArray? ValidateArray(ValueSpec spec, JsonNode value, string path, ValidationErrors errors)
  {
    if (value is not JsonArray array)
    {
      errors.Add(path, "Must be a list.");
      return null;
    }

    var before = errors.Count;
    if (spec.MinItems is { } minItems && array.Count < minItems)
    {
      errors.Add(path, $"Must have at least {minItems} items.");
    }

    if (spec.MaxItems is { } maxItems && array.Count > maxItems)
    {
      errors.Add(path, $"Must have at most {maxItems} items.");
    }

    var result = new JsonArray();
    for (var i = 0; i < array.Count; i++)
    {
      var itemPath = $"{path}[{i}]";
      if (array[i] is not { } item)
      {
        errors.Add(itemPath, "Must not be null.");
        continue;
      }

      // The loader guarantees arrays have an item spec.
      result.Add(ValidateValue(spec.Items!, item, itemPath, errors));
    }

    return errors.Count == before ? result : null;
  }

  private static JsonObject? ValidateObjectValue(ValueSpec spec, JsonNode value, string path, ValidationErrors errors)
  {
    if (value is not JsonObject obj)
    {
      errors.Add(path, "Must be an object.");
      return null;
    }

    var before = errors.Count;

    // The loader guarantees objects have properties.
    var result = ValidateObject(spec.Properties!, obj, path, errors);
    return errors.Count == before ? result : null;
  }

  private static bool MatchesPattern(string text, string pattern)
  {
    try
    {
      return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, RegexTimeout);
    }
    catch (RegexMatchTimeoutException)
    {
      return false;
    }
  }

  private static string FormatAllowed(IEnumerable<JsonNode> allowed) =>
    string.Join(", ", allowed.Select(a => ReadString(a) ?? a.ToJsonString()));

  private static string Join(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";
}

/// <summary>Validation problems keyed by field path, collected across a whole object.</summary>
internal sealed class ValidationErrors
{
  private readonly Dictionary<string, List<string>> _errors = [];

  /// <summary>The number of problems so far.</summary>
  public int Count { get; private set; }

  /// <summary>Records a problem with the field at <paramref name="path"/>.</summary>
  public void Add(string path, string message)
  {
    if (!_errors.TryGetValue(path, out var messages))
    {
      messages = [];
      _errors[path] = messages;
    }

    messages.Add(message);
    Count++;
  }

  /// <summary>The problems, one array of messages per field path.</summary>
  public IReadOnlyDictionary<string, string[]> ToDictionary() =>
    _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

  /// <summary>Each problem as <c>path: message</c>.</summary>
  public IEnumerable<string> Describe() =>
    _errors.SelectMany(e => e.Value.Select(m => e.Key.Length == 0 ? m : $"{e.Key}: {m}"));
}
