using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CsvUploadSample.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RowNumber",
                table: "TempCsvMasters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubCreateAt",
                table: "TempCsvMasters",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SubDescription",
                table: "TempCsvMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubInternetId",
                table: "TempCsvMasters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubName",
                table: "TempCsvMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubType",
                table: "TempCsvMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubMasters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubInternetId = table.Column<int>(type: "int", nullable: false),
                    SubCreateAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubMasters", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubMasters");

            migrationBuilder.DropColumn(
                name: "RowNumber",
                table: "TempCsvMasters");

            migrationBuilder.DropColumn(
                name: "SubCreateAt",
                table: "TempCsvMasters");

            migrationBuilder.DropColumn(
                name: "SubDescription",
                table: "TempCsvMasters");

            migrationBuilder.DropColumn(
                name: "SubInternetId",
                table: "TempCsvMasters");

            migrationBuilder.DropColumn(
                name: "SubName",
                table: "TempCsvMasters");

            migrationBuilder.DropColumn(
                name: "SubType",
                table: "TempCsvMasters");
        }
    }
}
