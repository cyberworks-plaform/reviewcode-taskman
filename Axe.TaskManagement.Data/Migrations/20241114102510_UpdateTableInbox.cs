using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axe.TaskManagement.Data.Migrations
{
    public partial class UpdateTableInbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TypeProcessing",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents");

            migrationBuilder.AddColumn<string>(
                name: "ServiceInstanceIdProcessed",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VirtualHost",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServiceInstanceIdProcessed",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents");

            migrationBuilder.DropColumn(
                name: "VirtualHost",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents");

            migrationBuilder.AddColumn<short>(
                name: "TypeProcessing",
                schema: "public",
                table: "ExtendedInboxIntegrationEvents",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }
    }
}
