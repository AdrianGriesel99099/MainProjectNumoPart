using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureProposalQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureProposalQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureProposalQuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    OptionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureProposalQuestionOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureProposalQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeatureProposalRoundId = table.Column<int>(type: "INTEGER", nullable: false),
                    QuestionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "INTEGER", nullable: true)
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
