using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestDependencies",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestDependencies", x => new { x.RequestId, x.DependsOnId });
                    table.ForeignKey(
                        name: "FK_RequestDependencies_ProvisioningRequests_DependsOnId",
                        column: x => x.DependsOnId,
                        principalTable: "ProvisioningRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestDependencies_ProvisioningRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ProvisioningRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestDependencies_DependsOnId",
                table: "RequestDependencies",
                column: "DependsOnId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestDependencies");
        }
    }
}
