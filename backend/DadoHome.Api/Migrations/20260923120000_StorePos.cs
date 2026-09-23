using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DadoHome.Api.Migrations;

[DbContext(typeof(DadoDbContext))]
[Migration("20260923120000_StorePos")]
public class StorePos : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "PosReceipts" (
            "Id" uuid PRIMARY KEY, "CashierId" uuid NOT NULL,
            "Number" text NOT NULL, "Status" text NOT NULL CHECK ("Status" IN ('Held','Sold','Cancelled','PartiallyReturned','Returned')),
            "PaymentMethod" text NOT NULL CHECK ("PaymentMethod" IN ('Cash','Card','QR')),
            "Total" numeric(18,2) NOT NULL CHECK ("Total" >= 0), "Tendered" numeric(18,2) NOT NULL CHECK ("Tendered" >= 0),
            "CreatedAt" timestamptz NOT NULL, "SoldAt" timestamptz NULL);
        CREATE UNIQUE INDEX "IX_PosReceipts_Number" ON "PosReceipts" ("Number");
        CREATE INDEX "IX_PosReceipts_CashierId_CreatedAt" ON "PosReceipts" ("CashierId", "CreatedAt");
        CREATE TABLE "PosLines" (
            "Id" uuid PRIMARY KEY, "ReceiptId" uuid NOT NULL REFERENCES "PosReceipts"("Id") ON DELETE RESTRICT,
            "ProductId" uuid NOT NULL REFERENCES "Products"("Id") ON DELETE RESTRICT,
            "ProductName" text NOT NULL, "Quantity" integer NOT NULL CHECK ("Quantity" > 0),
            "ReturnedQuantity" integer NOT NULL CHECK ("ReturnedQuantity" >= 0 AND "ReturnedQuantity" <= "Quantity"),
            "UnitPrice" numeric(18,2) NOT NULL CHECK ("UnitPrice" >= 0));
        CREATE INDEX "IX_PosLines_ReceiptId" ON "PosLines" ("ReceiptId");
        CREATE INDEX "IX_PosLines_ProductId" ON "PosLines" ("ProductId");
        CREATE TABLE "PosReturns" (
            "Id" uuid PRIMARY KEY, "ReceiptId" uuid NOT NULL, "LineId" uuid NOT NULL REFERENCES "PosLines"("Id") ON DELETE RESTRICT,
            "ActorId" uuid NOT NULL, "Quantity" integer NOT NULL CHECK ("Quantity" > 0),
            "Amount" numeric(18,2) NOT NULL CHECK ("Amount" >= 0), "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_PosReturns_LineId" ON "PosReturns" ("LineId");
        CREATE TABLE "PosOperations" (
            "Id" uuid PRIMARY KEY, "ActorId" uuid NOT NULL, "Fingerprint" text NOT NULL,
            "ReceiptId" uuid NOT NULL REFERENCES "PosReceipts"("Id") ON DELETE RESTRICT);
        CREATE INDEX "IX_PosOperations_ReceiptId" ON "PosOperations" ("ReceiptId");
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TABLE "PosOperations";
        DROP TABLE "PosReturns";
        DROP TABLE "PosLines";
        DROP TABLE "PosReceipts";
        """);
}
