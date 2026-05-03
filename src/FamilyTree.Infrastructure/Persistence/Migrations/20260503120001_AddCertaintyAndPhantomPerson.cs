using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertaintyAndPhantomPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Certainty",
                table: "Marriages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPhantom",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Certainty",
                table: "BiologicalParentChildLinks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Certainty",
                table: "AdoptiveParentChildLinks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Certainty",
                table: "Marriages");

            migrationBuilder.DropColumn(
                name: "IsPhantom",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Certainty",
                table: "BiologicalParentChildLinks");

            migrationBuilder.DropColumn(
                name: "Certainty",
                table: "AdoptiveParentChildLinks");
        }
    }
}
