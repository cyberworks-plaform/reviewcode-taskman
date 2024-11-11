using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Axe.TaskManagement.Data.Migrations
{
    public partial class AddTableInboxIntegrationEvent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExtendedInboxIntegrationEvents",
                schema: "public",
                columns: table => new
                {
                    IntergrationEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: true),
                    DocInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventBusIntergrationEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventBusIntergrationEventCreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntityName = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    EntityInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityIds = table.Column<string>(type: "text", nullable: true),
                    EntityInstanceIds = table.Column<string>(type: "text", nullable: true),
                    ExchangeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ServiceInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Data = table.Column<string>(type: "text", nullable: true),
                    TypeProcessing = table.Column<short>(type: "smallint", nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    RetryCount = table.Column<short>(type: "smallint", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    LastModificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtendedInboxIntegrationEvents", x => new { x.IntergrationEventId, x.ServiceCode });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtendedInboxIntegrationEvents",
                schema: "public");
        }
    }
}
