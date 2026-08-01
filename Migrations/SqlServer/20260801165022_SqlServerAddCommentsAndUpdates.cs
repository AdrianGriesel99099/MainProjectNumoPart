using System;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations.SqlServer
{
    // Hand-written, matching Migrations/SqlServer/20260801080659_SqlServerAddPhotoPart.cs and the
    // reasoning documented there: this assembly holds two migration sets sharing one model
    // snapshot, so scaffolding a second migration for this provider risks either an empty diff
    // (if the snapshot already reflects the change) or corrupting the SQLite snapshot. Two new
    // tables with a straightforward FK each is simple enough to write out directly and mirror the
    // SQLite migration column-for-column instead.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801165022_SqlServerAddCommentsAndUpdates")]
    /// <inheritdoc />
    public partial class SqlServerAddCommentsAndUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhotoId = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoComments_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleUpdates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleUpdates_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoComments_PhotoId",
                table: "PhotoComments",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleUpdates_VehicleId",
                table: "VehicleUpdates",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoComments");

            migrationBuilder.DropTable(
                name: "VehicleUpdates");
        }
    }
}
