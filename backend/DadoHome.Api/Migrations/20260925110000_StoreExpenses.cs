using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace DadoHome.Api.Migrations;
[DbContext(typeof(DadoDbContext))]
[Migration("20260925110000_StoreExpenses")]
public class StoreExpenses : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "StoreExpenses" (
          "Id" uuid PRIMARY KEY, "Date" date NOT NULL,
          "Amount" numeric(18,2) NOT NULL CHECK ("Amount" > 0),
          "Category" text NOT NULL, "Description" text NOT NULL,
          "CreatedBy" text NOT NULL, "CreatedAt" timestamptz NOT NULL,
          "CancellationReason" text NULL, "CancelledAt" timestamptz NULL);
        CREATE INDEX "IX_StoreExpenses_Date" ON "StoreExpenses" ("Date");
        """);
    protected override void Down(MigrationBuilder m) => m.DropTable("StoreExpenses");
}
