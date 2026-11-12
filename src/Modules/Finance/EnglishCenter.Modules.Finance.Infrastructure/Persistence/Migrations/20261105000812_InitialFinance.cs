using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_no = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,0)", precision: 12, scale: 0, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
                    table.CheckConstraint("ck_invoice_amount", "amount >= 0");
                    table.CheckConstraint("ck_invoice_payment", "(status = 'Unpaid' AND paid_at IS NULL AND confirmed_by IS NULL) OR (status = 'Paid' AND paid_at IS NOT NULL AND confirmed_by IS NOT NULL)");
                    table.CheckConstraint("ck_invoice_status", "status IN ('Unpaid', 'Paid')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_enrollment_id",
                schema: "finance",
                table: "invoices",
                column: "enrollment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoice_no",
                schema: "finance",
                table: "invoices",
                column: "invoice_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_student_id",
                schema: "finance",
                table: "invoices",
                column: "student_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoices",
                schema: "finance");
        }
    }
}
