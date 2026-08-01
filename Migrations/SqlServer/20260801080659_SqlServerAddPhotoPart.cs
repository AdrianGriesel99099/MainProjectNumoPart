using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations.SqlServer
{
    // Hand-written rather than scaffolded, deliberately. This assembly holds two migration sets
    // (SQLite for dev, SQL Server for prod) sharing one model snapshot, so running
    // `dotnet ef migrations add` a second time to produce the SQL Server variant would diff
    // against a snapshot that already contains this change and generate an empty migration —
    // or, if the snapshot were shuffled to avoid that, risk corrupting the SQLite one.
    // The change is a single nullable column, so writing it out is both safer and trivially
    // reviewable. Mirrors Migrations/20260801080658_AddPhotoPart.cs.
    // The [Migration] id normally lives in a generated .Designer.cs alongside a full
    // BuildTargetModel snapshot. Declared inline here instead: the designer's snapshot only
    // exists to diff the NEXT scaffolded migration against, and migrations in this set are
    // written by hand for the reason above, so a 380-line generated model would be dead weight
    // that silently rots as the model changes.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801080659_SqlServerAddPhotoPart")]
    /// <inheritdoc />
    public partial class SqlServerAddPhotoPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Part",
                table: "Photos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Part",
                table: "Photos");
        }
    }
}
