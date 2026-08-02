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
    // snapshot. One new table with two FKs is simple enough to mirror column-for-column instead.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801190000_SqlServerAddDamageMarks")]
    /// <inheritdoc />
    public partial class SqlServerAddDamageMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DamageMarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Part = table.Column<int>(type: "int", nullable: false),
                    PhotoId = table.Column<int>(type: "int", nullable: true),
                    XPercent = table.Column<double>(type: "float", nullable: false),
                    YPercent = table.Column<double>(type: "float", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorEmail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DamageMarks", x => x.Id);
                    // Restrict, not Cascade: Vehicle->Photo and Vehicle->DamageMark are both
                    // already cascade paths, so a second one via Photo->DamageMark is a multiple
                    // cascade path SQL Server refuses at CREATE TABLE time ("may cause cycles or
                    // multiple cascade paths") — this is exactly what surfaced when this migration
                    // was first applied against production and rolled back untouched. A
                    // photo-anchored mark is still cleaned up when its photo is deleted; that now
                    // happens in PhotoEndpoints' delete handler instead of via this FK.
                    table.ForeignKey(
                        name: "FK_DamageMarks_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DamageMarks_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DamageMarks_PhotoId",
                table: "DamageMarks",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_DamageMarks_VehicleId",
                table: "DamageMarks",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DamageMarks");
        }
    }
}
