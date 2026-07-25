using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainProjectNumoPart.Migrations
{
    /// <inheritdoc />
    public partial class MakePhotoSequenceUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_VehicleId_Stage_SequenceNumber",
                table: "Photos");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_VehicleId_Stage_SequenceNumber",
                table: "Photos",
                columns: new[] { "VehicleId", "Stage", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photos_VehicleId_Stage_SequenceNumber",
                table: "Photos");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_VehicleId_Stage_SequenceNumber",
                table: "Photos",
                columns: new[] { "VehicleId", "Stage", "SequenceNumber" });
        }
    }
}
