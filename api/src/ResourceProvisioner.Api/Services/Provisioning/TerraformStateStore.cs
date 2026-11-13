namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// <see cref="ITerraformStateStore"/> on the app's Postgres. Terraform's <c>pg</c> backend creates the
/// <see cref="Schema"/> schema and its <c>states</c> table on first use, with one row per workspace (<c>name</c>,
/// <c>data</c>); EF Core migrations never touch it.
/// </summary>
/// <param name="db">The app database, whose connection string the backend uses too.</param>
public class TerraformStateStore(AppDbContext db) : ITerraformStateStore
{
  /// <summary>The schema Terraform keeps state in.</summary>
  public const string Schema = "terraform_state";

  /// <inheritdoc />
  public async Task<bool> HasStateAsync(Guid requestId, CancellationToken cancellationToken)
  {
    if (!await TableExists(cancellationToken))
    {
      return false;
    }

    var name = requestId.ToString();
    return await db.Database
      .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM terraform_state.states WHERE name = {name}")
      .AnyAsync(cancellationToken);
  }

  /// <inheritdoc />
  public async Task DeleteStateAsync(Guid requestId, CancellationToken cancellationToken)
  {
    if (!await TableExists(cancellationToken))
    {
      return;
    }

    // On the DbContext's connection, so it joins the transaction the request is deleted in.
    var name = requestId.ToString();
    await db.Database.ExecuteSqlAsync($"DELETE FROM terraform_state.states WHERE name = {name}", cancellationToken);
  }

  // The table only exists once Terraform has initialised the backend.
  private async Task<bool> TableExists(CancellationToken cancellationToken) =>
    await db.Database
      .SqlQuery<bool>($"SELECT to_regclass({Schema + ".states"}) IS NOT NULL AS \"Value\"")
      .SingleAsync(cancellationToken);

  /// <inheritdoc />
  public IReadOnlyDictionary<string, string> BackendEnvironment() =>
    new Dictionary<string, string>
    {
      ["PG_CONN_STR"] = ToLibpq(db.Database.GetConnectionString() ??
                                throw new InvalidOperationException("The app database has no connection string.")),
      ["PG_SCHEMA_NAME"] = Schema,
    };

  /// <summary>
  /// Turns an Npgsql connection string into the libpq key/value form the <c>pg</c> backend reads. Npgsql's default
  /// SSL mode, Prefer, has no libpq equivalent in the backend's driver, which would otherwise default to
  /// <c>require</c>; Disable, Allow and Prefer become <c>disable</c>, so a database that needs TLS must say
  /// <c>SslMode=Require</c> (or stricter).
  /// </summary>
  /// <param name="npgsqlConnectionString">An Npgsql connection string, such as the app's.</param>
  /// <returns>A libpq connection string.</returns>
  public static string ToLibpq(string npgsqlConnectionString)
  {
    var builder = new NpgsqlConnectionStringBuilder(npgsqlConnectionString);
    var parts = new List<(string Key, string? Value)>
    {
      ("host", builder.Host),
      ("port", builder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
      ("dbname", builder.Database),
      ("user", builder.Username),
      ("password", builder.Password),
      ("sslmode", builder.SslMode switch
      {
        SslMode.Require => "require",
        SslMode.VerifyCA => "verify-ca",
        SslMode.VerifyFull => "verify-full",
        _ => "disable",
      }),
    };

    var result = new StringBuilder();
    foreach (var (key, value) in parts.Where(p => !string.IsNullOrEmpty(p.Value)))
    {
      if (result.Length > 0)
      {
        result.Append(' ');
      }

      result.Append(key).Append("='").Append(value!.Replace(@"\", @"\\").Replace("'", @"\'")).Append('\'');
    }

    return result.ToString();
  }
}
