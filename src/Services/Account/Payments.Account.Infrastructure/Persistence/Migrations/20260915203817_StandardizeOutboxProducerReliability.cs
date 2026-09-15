using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Account.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeOutboxProducerReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AggregateId",
                schema: "account",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AggregateType",
                schema: "account",
                table: "outbox_messages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "account",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "account",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                schema: "account",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<int>(
                name: "EventVersion",
                schema: "account",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Headers",
                schema: "account",
                table: "outbox_messages",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAtUtc",
                schema: "account",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                schema: "account",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartitionKey",
                schema: "account",
                table: "outbox_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "account",
                table: "outbox_messages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_EventId",
                schema: "account",
                table: "outbox_messages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Status_NextAttemptAtUtc",
                schema: "account",
                table: "outbox_messages",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Topic_PartitionKey",
                schema: "account",
                table: "outbox_messages",
                columns: new[] { "Topic", "PartitionKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_EventId",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_Status_NextAttemptAtUtc",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_Topic_PartitionKey",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "AggregateId",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "AggregateType",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "EventId",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "EventVersion",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Headers",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LastAttemptAtUtc",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "PartitionKey",
                schema: "account",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "account",
                table: "outbox_messages");
        }
    }
}
