using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedByEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QueuedForBuildAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureProposalRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureProposalId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AiContent = table.Column<string>(type: "TEXT", nullable: true),
                    HumanDecision = table.Column<int>(type: "INTEGER", nullable: true),
                    HumanComment = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposalRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureProposalRounds_FeatureProposals_FeatureProposalId",
                        column: x => x.FeatureProposalId,
                        principalTable: "FeatureProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureProposalRounds_FeatureProposalId",
                table: "FeatureProposalRounds",
                column: "FeatureProposalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureProposalRounds");

            migrationBuilder.DropTable(
                name: "FeatureProposals");
        }
    }
}
