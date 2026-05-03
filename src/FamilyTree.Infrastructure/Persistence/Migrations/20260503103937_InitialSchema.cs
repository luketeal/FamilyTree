using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTree.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BirthSurname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BirthDate_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    BirthDate_Month = table.Column<int>(type: "INTEGER", nullable: true),
                    BirthDate_Day = table.Column<int>(type: "INTEGER", nullable: true),
                    BirthDate_IsApproximate = table.Column<bool>(type: "INTEGER", nullable: true),
                    BirthPlace = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DeathDate_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    DeathDate_Month = table.Column<int>(type: "INTEGER", nullable: true),
                    DeathDate_Day = table.Column<int>(type: "INTEGER", nullable: true),
                    DeathDate_IsApproximate = table.Column<bool>(type: "INTEGER", nullable: true),
                    DeathPlace = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdoptiveParentChildLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdoptionDate_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    AdoptionDate_Month = table.Column<int>(type: "INTEGER", nullable: true),
                    AdoptionDate_Day = table.Column<int>(type: "INTEGER", nullable: true),
                    AdoptionDate_IsApproximate = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdoptiveParentChildLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdoptiveParentChildLinks_People_ChildId",
                        column: x => x.ChildId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdoptiveParentChildLinks_People_ParentId",
                        column: x => x.ParentId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BiologicalParentChildLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChildId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiologicalParentChildLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BiologicalParentChildLinks_People_ChildId",
                        column: x => x.ChildId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BiologicalParentChildLinks_People_ParentId",
                        column: x => x.ParentId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Marriages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Spouse1Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Spouse2Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartDate_Year = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate_Month = table.Column<int>(type: "INTEGER", nullable: true),
                    StartDate_Day = table.Column<int>(type: "INTEGER", nullable: true),
                    StartDate_IsApproximate = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartPlace = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EndDate_Year = table.Column<int>(type: "INTEGER", nullable: true),
                    EndDate_Month = table.Column<int>(type: "INTEGER", nullable: true),
                    EndDate_Day = table.Column<int>(type: "INTEGER", nullable: true),
                    EndDate_IsApproximate = table.Column<bool>(type: "INTEGER", nullable: true),
                    EndReason = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Marriages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Marriages_People_Spouse1Id",
                        column: x => x.Spouse1Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Marriages_People_Spouse2Id",
                        column: x => x.Spouse2Id,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StepparentRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepparentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepchildId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MarriageId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepparentRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StepparentRelationships_Marriages_MarriageId",
                        column: x => x.MarriageId,
                        principalTable: "Marriages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepparentRelationships_People_StepchildId",
                        column: x => x.StepchildId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepparentRelationships_People_StepparentId",
                        column: x => x.StepparentId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdoptiveParentChildLinks_ChildId",
                table: "AdoptiveParentChildLinks",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_AdoptiveParentChildLinks_ParentId_ChildId",
                table: "AdoptiveParentChildLinks",
                columns: new[] { "ParentId", "ChildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BiologicalParentChildLinks_ChildId",
                table: "BiologicalParentChildLinks",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_BiologicalParentChildLinks_ParentId_ChildId",
                table: "BiologicalParentChildLinks",
                columns: new[] { "ParentId", "ChildId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Marriages_Spouse1Id",
                table: "Marriages",
                column: "Spouse1Id");

            migrationBuilder.CreateIndex(
                name: "IX_Marriages_Spouse2Id",
                table: "Marriages",
                column: "Spouse2Id");

            migrationBuilder.CreateIndex(
                name: "IX_StepparentRelationships_MarriageId",
                table: "StepparentRelationships",
                column: "MarriageId");

            migrationBuilder.CreateIndex(
                name: "IX_StepparentRelationships_StepchildId",
                table: "StepparentRelationships",
                column: "StepchildId");

            migrationBuilder.CreateIndex(
                name: "IX_StepparentRelationships_StepparentId_StepchildId_MarriageId",
                table: "StepparentRelationships",
                columns: new[] { "StepparentId", "StepchildId", "MarriageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdoptiveParentChildLinks");

            migrationBuilder.DropTable(
                name: "BiologicalParentChildLinks");

            migrationBuilder.DropTable(
                name: "StepparentRelationships");

            migrationBuilder.DropTable(
                name: "Marriages");

            migrationBuilder.DropTable(
                name: "People");
        }
    }
}
