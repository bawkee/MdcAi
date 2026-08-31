using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class AgenticPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletionState",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinishReason",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdStep",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdTurn",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningDetailsJson",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningRawJson",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceInStep",
                table: "Messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallId",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallsJson",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolResultJson",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ToolsEnabled",
                table: "Conversations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WorkspacePath",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                table: "ChatSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ToolCalls",
                columns: table => new
                {
                    IdToolCall = table.Column<string>(type: "TEXT", nullable: false),
                    IdAssistantMessage = table.Column<string>(type: "TEXT", nullable: true),
                    IdTurn = table.Column<string>(type: "TEXT", nullable: true),
                    IdStep = table.Column<string>(type: "TEXT", nullable: true),
                    ToolCallId = table.Column<string>(type: "TEXT", nullable: true),
                    CallIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", nullable: true),
                    ArgumentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ArgumentsHash = table.Column<string>(type: "TEXT", nullable: true),
                    Risk = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    ProposedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    ResultMessageId = table.Column<string>(type: "TEXT", nullable: true),
                    CallPresentationJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultPresentationJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolCalls", x => x.IdToolCall);
                });

            migrationBuilder.CreateTable(
                name: "Turns",
                columns: table => new
                {
                    IdTurn = table.Column<string>(type: "TEXT", nullable: false),
                    IdConversation = table.Column<string>(type: "TEXT", nullable: true),
                    IdTriggerMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Origin = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Effort = table.Column<string>(type: "TEXT", nullable: true),
                    PromptSectionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    ToolsSchemaSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    IdGoal = table.Column<string>(type: "TEXT", nullable: true),
                    GoalRevision = table.Column<int>(type: "INTEGER", nullable: true),
                    GoalRound = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedTs = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StepCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Turns", x => x.IdTurn);
                    table.ForeignKey(
                        name: "FK_Turns_Conversations_IdConversation",
                        column: x => x.IdConversation,
                        principalTable: "Conversations",
                        principalColumn: "IdConversation");
                });

            migrationBuilder.CreateTable(
                name: "Steps",
                columns: table => new
                {
                    IdStep = table.Column<string>(type: "TEXT", nullable: false),
                    IdTurn = table.Column<string>(type: "TEXT", nullable: true),
                    StepNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstDeltaTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstOutputTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Effort = table.Column<string>(type: "TEXT", nullable: true),
                    FinishReason = table.Column<string>(type: "TEXT", nullable: true),
                    RequestId = table.Column<string>(type: "TEXT", nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    ReasoningTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    PromptCacheReadTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    PromptCacheWriteTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: true),
                    FirstTokenLatencyMs = table.Column<int>(type: "INTEGER", nullable: true),
                    DecodeDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    ModelDurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    ContextPlanJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Steps", x => x.IdStep);
                    table.ForeignKey(
                        name: "FK_Steps_Turns_IdTurn",
                        column: x => x.IdTurn,
                        principalTable: "Turns",
                        principalColumn: "IdTurn",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelRequestAttempts",
                columns: table => new
                {
                    IdAttempt = table.Column<string>(type: "TEXT", nullable: false),
                    IdTurn = table.Column<string>(type: "TEXT", nullable: true),
                    IdStep = table.Column<string>(type: "TEXT", nullable: true),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    RetryPolicyKey = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    RetryDisposition = table.Column<string>(type: "TEXT", nullable: true),
                    StartedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduledDelayMs = table.Column<int>(type: "INTEGER", nullable: true),
                    RetryDelayFromHeader = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetryStartedTs = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCategory = table.Column<string>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", nullable: true),
                    FailureDetail = table.Column<string>(type: "TEXT", nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelRequestAttempts", x => x.IdAttempt);
                    table.ForeignKey(
                        name: "FK_ModelRequestAttempts_Steps_IdStep",
                        column: x => x.IdStep,
                        principalTable: "Steps",
                        principalColumn: "IdStep",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ChatSettings",
                keyColumn: "IdSettings",
                keyValue: "general",
                column: "ProviderKey",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_IdStep",
                table: "Messages",
                column: "IdStep");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_IdTurn",
                table: "Messages",
                column: "IdTurn");

            migrationBuilder.CreateIndex(
                name: "IX_ModelRequestAttempts_IdStep_AttemptNumber",
                table: "ModelRequestAttempts",
                columns: new[] { "IdStep", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Steps_IdTurn_StepNumber",
                table: "Steps",
                columns: new[] { "IdTurn", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_IdAssistantMessage_CallIndex",
                table: "ToolCalls",
                columns: new[] { "IdAssistantMessage", "CallIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_IdAssistantMessage_ToolCallId",
                table: "ToolCalls",
                columns: new[] { "IdAssistantMessage", "ToolCallId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Turns_IdConversation",
                table: "Turns",
                column: "IdConversation");

            migrationBuilder.CreateIndex(
                name: "IX_Turns_IdTriggerMessage",
                table: "Turns",
                column: "IdTriggerMessage");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Steps_IdStep",
                table: "Messages",
                column: "IdStep",
                principalTable: "Steps",
                principalColumn: "IdStep");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Turns_IdTurn",
                table: "Messages",
                column: "IdTurn",
                principalTable: "Turns",
                principalColumn: "IdTurn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Steps_IdStep",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Turns_IdTurn",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "ModelRequestAttempts");

            migrationBuilder.DropTable(
                name: "ToolCalls");

            migrationBuilder.DropTable(
                name: "Steps");

            migrationBuilder.DropTable(
                name: "Turns");

            migrationBuilder.DropIndex(
                name: "IX_Messages_IdStep",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_IdTurn",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "CompletionState",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FinishReason",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IdStep",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IdTurn",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReasoningDetailsJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReasoningRawJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SequenceInStep",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolCallId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolCallsJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolResultJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ToolsEnabled",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WorkspacePath",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                table: "ChatSettings");
        }
    }
}
