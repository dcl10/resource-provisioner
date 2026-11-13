using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "ProvisioningRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Environment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProjectRequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_ProvisioningRequests_ProjectRequestId",
                        column: x => x.ProjectRequestId,
                        principalTable: "ProvisioningRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningRequests_ProjectId",
                table: "ProvisioningRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId",
                table: "Projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectRequestId",
                table: "Projects",
                column: "ProjectRequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProvisioningRequests_Projects_ProjectId",
                table: "ProvisioningRequests",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing data is POC data, but may still back real resources, so it's carried over rather than reset.
            // Every project request that isn't gone gets its project, and requests that named one through the old
            // project_request_id variable go in it. Others keep no project: they can't be confirmed again, but they
            // can still be destroyed.
            migrationBuilder.Sql("""
                INSERT INTO "Projects"
                  ("Id", "OwnerId", "OwnerEmail", "Name", "Description", "Purpose", "Environment", "CreatedAt",
                   "ProjectRequestId")
                SELECT gen_random_uuid(), r."OwnerId", r."OwnerEmail", COALESCE(r."Variables"::jsonb ->> 'name', ''),
                  NULLIF(r."Variables"::jsonb ->> 'description', ''), r."Variables"::jsonb ->> 'purpose',
                  r."Variables"::jsonb ->> 'environment', r."CreatedAt", r."Id"
                FROM "ProvisioningRequests" r
                WHERE r."Type" = 'project' AND r."Status" NOT IN ('Cancelled', 'Destroyed');

                UPDATE "ProvisioningRequests" r
                SET "ProjectId" = p."Id"
                FROM "Projects" p
                WHERE r."Variables"::jsonb ->> 'project_request_id' = p."ProjectRequestId"::text;

                UPDATE "ProvisioningRequests"
                SET "Variables" = ("Variables"::jsonb - 'project_request_id')::text
                WHERE "Variables"::jsonb ? 'project_request_id';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProvisioningRequests_Projects_ProjectId",
                table: "ProvisioningRequests");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProvisioningRequests_ProjectId",
                table: "ProvisioningRequests");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ProvisioningRequests");
        }
    }
}
