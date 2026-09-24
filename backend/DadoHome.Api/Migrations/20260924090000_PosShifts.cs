using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DadoHome.Api.Migrations;
[DbContext(typeof(DadoDbContext))]
[Migration("20260924090000_PosShifts")]
public class PosShifts : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "PosShifts" (
            "Id" uuid PRIMARY KEY, "CashierId" uuid NOT NULL,
            "OpenedAt" timestamptz NOT NULL, "ClosedAt" timestamptz NULL, "ClosedBy" uuid NULL,
            "OpeningCash" numeric(18,2) NOT NULL CHECK ("OpeningCash" >= 0),
            "CountedCash" numeric(18,2) NULL CHECK ("CountedCash" >= 0),
            "ExpectedCash" numeric(18,2) NULL, "Difference" numeric(18,2) NULL);
        CREATE UNIQUE INDEX "IX_PosShifts_CashierId" ON "PosShifts" ("CashierId") WHERE "ClosedAt" IS NULL;
        ALTER TABLE "PosReceipts" ADD COLUMN "ShiftId" uuid NULL REFERENCES "PosShifts"("Id") ON DELETE RESTRICT;
        ALTER TABLE "PosReturns" ADD COLUMN "ShiftId" uuid NULL REFERENCES "PosShifts"("Id") ON DELETE RESTRICT;
        CREATE INDEX "IX_PosReceipts_ShiftId" ON "PosReceipts" ("ShiftId");
        CREATE INDEX "IX_PosReturns_ShiftId" ON "PosReturns" ("ShiftId");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "PosReturns" DROP COLUMN "ShiftId";
        ALTER TABLE "PosReceipts" DROP COLUMN "ShiftId";
        DROP TABLE "PosShifts";
        """);
}
