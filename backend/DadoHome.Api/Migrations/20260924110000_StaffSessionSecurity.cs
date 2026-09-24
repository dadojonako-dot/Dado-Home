using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace DadoHome.Api.Migrations;
[DbContext(typeof(DadoDbContext))]
[Migration("20260924110000_StaffSessionSecurity")]
public class StaffSessionSecurity:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE "StaffUsers" ADD COLUMN "AuthVersion" bigint NOT NULL DEFAULT 0;
        ALTER TABLE "StaffUsers" ADD COLUMN "FailedLoginCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "StaffUsers" ADD COLUMN "LockedUntil" timestamptz NULL;
        """);
    protected override void Down(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE "StaffUsers" DROP COLUMN "AuthVersion", DROP COLUMN "FailedLoginCount", DROP COLUMN "LockedUntil";
        """);
}
