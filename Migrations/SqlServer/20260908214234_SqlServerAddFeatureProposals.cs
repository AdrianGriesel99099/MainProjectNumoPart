using System;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations.SqlServer
{
    // Hand-written, matching the pattern established by every other SqlServer migration in this
    // folder: this assembly holds two migration sets sharing one model snapshot, so scaffolding
    // a second migration for this provider risks an empty diff or corrupting the SQLite
    // snapshot. Two new tables with one FK, mirrored column-for-column from the SQLite migration
    // of the same name.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260908214234_SqlServerAddFeatureProposals")]
    /// <inheritdoc />
    public partial class SqlServerAddFeatureProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedByEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QueuedForBuildAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureProposalRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeatureProposalId = table.Column<int>(type: "int", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    AiContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HumanDecision = table.Column<int>(type: "int", nullable: true),
                    HumanComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
