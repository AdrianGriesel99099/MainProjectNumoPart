using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations.SqlServer
{
    // Hand-written, matching the pattern established by every other SqlServer migration in this
    // folder (see the comment on SqlServerAddPhotoPart for why). A single non-nullable column
    // with a default, mirrored from Migrations/20260916154719_AddFeatureProposalArchive.cs.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260916154719_SqlServerAddFeatureProposalArchive")]
    /// <inheritdoc />
    public partial class SqlServerAddFeatureProposalArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "FeatureProposals",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "FeatureProposals");
        }
    }
}
