using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTerraformVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerraformVariables",
                table: "ProvisioningRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerraformVariables",
                table: "ProvisioningRequests");
        }
    }
}
