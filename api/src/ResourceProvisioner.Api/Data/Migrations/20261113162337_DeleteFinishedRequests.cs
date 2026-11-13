using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <summary>
    /// Cancelled and destroyed requests are deleted rather than kept, and those statuses are gone, so this deletes the
    /// requests left in them, with everything stored for them. No schema change.
    /// </summary>
    public partial class DeleteFinishedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // In the order the app deletes one: requests that stay lose their links to it, then its Terraform state
            // goes, then the request, whose cascades take its sensitive outputs, log lines, own dependencies and
            // project. A destroyed request's state holds nothing any more, and a cancelled one never had any; the
            // states table only exists once Terraform has run.
            migrationBuilder.Sql("""
                DELETE FROM "RequestDependencies" d
                USING "ProvisioningRequests" r
                WHERE d."DependsOnId" = r."Id" AND r."Status" IN ('Cancelled', 'Destroyed');

                UPDATE "ProvisioningRequests" m
                SET "ProjectId" = NULL
                FROM "Projects" p
                JOIN "ProvisioningRequests" r ON r."Id" = p."ProjectRequestId"
                WHERE m."ProjectId" = p."Id" AND r."Status" IN ('Cancelled', 'Destroyed');

                DO $$
                BEGIN
                  IF to_regclass('terraform_state.states') IS NOT NULL THEN
                    DELETE FROM terraform_state.states s
                    USING "ProvisioningRequests" r
                    WHERE s.name = r."Id"::text AND r."Status" = 'Destroyed';
                  END IF;
                END
                $$;

                DELETE FROM "ProvisioningRequests" WHERE "Status" IN ('Cancelled', 'Destroyed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted requests can't be brought back, and nothing in the schema changed.
        }
    }
}
