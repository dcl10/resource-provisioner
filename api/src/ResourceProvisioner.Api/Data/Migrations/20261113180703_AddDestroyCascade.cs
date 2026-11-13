using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <summary>
    /// Cascading destroy: the status a request had before it moved to Destroying, to go back to if the destroy stops,
    /// and the request whose failed destroy stopped it. Requests already Destroying get no status to go back to; one
    /// whose destroy stops goes to Failed.
    /// </summary>
    public partial class AddDestroyCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestroyStoppedById",
                table: "ProvisioningRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusBeforeDestroy",
                table: "ProvisioningRequests",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestroyStoppedById",
                table: "ProvisioningRequests");

            migrationBuilder.DropColumn(
                name: "StatusBeforeDestroy",
                table: "ProvisioningRequests");
        }
    }
}
