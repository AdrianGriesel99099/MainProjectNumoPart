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
    // snapshot. Two new tables with two FKs (one Restrict, to avoid the same multiple-cascade-path
    // problem DamageMark.PhotoId already hit -- see AppDbContext.cs), mirrored column-for-column
    // from the SQLite migration of the same name.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260909114132_SqlServerAddFeatureProposalQuestions")]
    /// <inheritdoc />
    public partial class SqlServerAddFeatureProposalQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureProposalQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeatureProposalQuestionId = table.Column<int>(type: "int", nullable: false),
                    OptionNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposalQuestionOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureProposalQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeatureProposalRoundId = table.Column<int>(type: "int", nullable: false),
                    QuestionNumber = table.Column<int>(type: "int", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposalQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureProposalQuestions_FeatureProposalQuestionOptions_SelectedOptionId",
                        column: x => x.SelectedOptionId,
                        principalTable: "FeatureProposalQuestionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeatureProposalQuestions_FeatureProposalRounds_FeatureProposalRoundId",
                        column: x => x.FeatureProposalRoundId,
                        principalTable: "FeatureProposalRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureProposalQuestionOptions_FeatureProposalQuestionId",
                table: "FeatureProposalQuestionOptions",
                column: "FeatureProposalQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureProposalQuestions_FeatureProposalRoundId",
                table: "FeatureProposalQuestions",
                column: "FeatureProposalRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureProposalQuestions_SelectedOptionId",
                table: "FeatureProposalQuestions",
                column: "SelectedOptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_FeatureProposalQuestionOptions_FeatureProposalQuestions_FeatureProposalQuestionId",
                table: "FeatureProposalQuestionOptions",
                column: "FeatureProposalQuestionId",
                principalTable: "FeatureProposalQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FeatureProposalQuestionOptions_FeatureProposalQuestions_FeatureProposalQuestionId",
                table: "FeatureProposalQuestionOptions");

            migrationBuilder.DropTable(
                name: "FeatureProposalQuestions");

            migrationBuilder.DropTable(
                name: "FeatureProposalQuestionOptions");
        }
    }
}
