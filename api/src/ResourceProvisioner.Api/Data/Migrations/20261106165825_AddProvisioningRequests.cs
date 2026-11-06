using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceProvisioner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisioningRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProvisioningRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Variables = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outputs = table.Column<string>(type: "text", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestSensitiveOutputs",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Values = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestSensitiveOutputs", x => x.RequestId);
                    table.ForeignKey(
                        name: "FK_RequestSensitiveOutputs_ProvisioningRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ProvisioningRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningRequests_OwnerId",
                table: "ProvisioningRequests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningRequests_Status",
                table: "ProvisioningRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestSensitiveOutputs");

            migrationBuilder.DropTable(
                name: "ProvisioningRequests");
        }
    }
}
