using System.Security.Claims;
using System.Text.Json.Serialization;
using DadoHome.Api.Auth;
using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DadoHome.Api.Pos;

public class PosShift
{
    public Guid Id { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string CashierName { get; set; } = "";
    public Guid CashierId { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? Difference { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record OpenShift(Guid Id, decimal OpeningCash);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CloseShift(decimal CountedCash);
public record ShiftTotals(PosShift Shift, int Receipts, decimal Cash, decimal Card, decimal QR,
    decimal CashReturns, decimal CardReturns, decimal QRReturns, decimal ExpectedCash);

public static class PosShifts
{
    static Guid Actor(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static bool Money(decimal value) => value >= 0 && value <= 999999999999m && decimal.Round(value, 2) == value;
    static IQueryable<PosShift> Visible(DadoDbContext db, ClaimsPrincipal u) => u.IsInRole(Roles.Cashier)
        ? db.Set<PosShift>().Where(x => x.CashierId == Actor(u)) : db.Set<PosShift>();
    public static async Task LockCashier(DadoDbContext db, Guid cashier) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"pos-shift:" + cashier}, 0))");
    // The caller's transaction retains this lock until its sale/return is committed.
    public static async Task<PosShift> RequireOpen(DadoDbContext db, ClaimsPrincipal u)
    {
        var actor = Actor(u);
        await LockCashier(db, actor);
        return await db.Set<PosShift>().SingleOrDefaultAsync(x => x.CashierId == actor && x.ClosedAt == null)
            ?? throw new PosException("Сначала откройте кассовую смену");
    }
    public static async Task<ShiftTotals> Totals(DadoDbContext db, PosShift shift)
    {
        shift.CashierName = await db.StaffUsers.Where(x => x.Id == shift.CashierId).Select(x => x.Name).SingleOrDefaultAsync() ?? "Сотрудник";
        var sales = await db.Set<PosReceipt>().Where(x => x.ShiftId == shift.Id && x.SoldAt != null)
            .GroupBy(x => x.PaymentMethod).Select(g => new { Method = g.Key, Sum = g.Sum(x => x.Total), Count = g.Count() }).ToListAsync();
        var returns = await (from r in db.Set<PosReturn>() join receipt in db.Set<PosReceipt>() on r.ReceiptId equals receipt.Id
            where r.ShiftId == shift.Id group r by receipt.PaymentMethod into g select new { Method = g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync();
        decimal Sold(string method) => sales.Where(x => x.Method == method).Sum(x => x.Sum);
        decimal Returned(string method) => returns.Where(x => x.Method == method).Sum(x => x.Sum);
        return new ShiftTotals(shift, sales.Sum(x => x.Count), Sold("Cash"), Sold("Card"), Sold("QR"),
            Returned("Cash"), Returned("Card"), Returned("QR"), shift.OpeningCash + Sold("Cash") - Returned("Cash"));
    }
    static void Audit(DadoDbContext db, ClaimsPrincipal u, Guid id, string action) =>
        db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = Actor(u).ToString(), Role = u.FindFirstValue(ClaimTypes.Role)!, Action = action, Entity = "PosShift", EntityId = id.ToString() });
    public static void MapShifts(this RouteGroupBuilder group)
    {
        group.MapGet("/shifts/current", async (DadoDbContext db, ClaimsPrincipal u) =>
        {
            var actor = Actor(u);
            var shift = await db.Set<PosShift>().AsNoTracking().SingleOrDefaultAsync(x => x.CashierId == actor && x.ClosedAt == null);
            return Results.Ok(new { shift });
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
        group.MapGet("/shifts", async (DadoDbContext db, ClaimsPrincipal u, int? page) =>
        {
            var shifts = await Visible(db, u).AsNoTracking().OrderByDescending(x => x.OpenedAt)
                .Skip((Math.Clamp(page ?? 1, 1, 10000) - 1) * 50).Take(50).ToListAsync();
            var ids = shifts.Select(x => x.CashierId).Distinct().ToArray();
            var names = await db.StaffUsers.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            foreach (var shift in shifts) shift.CashierName = names.GetValueOrDefault(shift.CashierId, "Сотрудник");
            return Results.Ok(shifts);
        });
        group.MapGet("/shifts/{id:guid}", async (Guid id, DadoDbContext db, ClaimsPrincipal u) =>
        {
            // One consistent report snapshot, even while the current shift is selling.
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var shift = await Visible(db, u).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return shift is null ? Results.NotFound() : Results.Ok(await Totals(db, shift));
        });
        group.MapPost("/shifts", async (OpenShift r, DadoDbContext db, ClaimsPrincipal u) =>
        {
            if (r.Id == Guid.Empty || !Money(r.OpeningCash)) return Results.BadRequest(new { error = "Проверьте начальную сумму и идентификатор смены" });
            await using var tx = await db.Database.BeginTransactionAsync();
            var actor = Actor(u);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"pos-shift-opening:" + r.Id}, 0))");
            await LockCashier(db, actor);
            var existing = await db.Set<PosShift>().FindAsync(r.Id);
            if (existing is not null)
                return existing.CashierId == actor && existing.OpeningCash == r.OpeningCash
                    ? Results.Ok(existing) : Results.Conflict(new { error = "Идентификатор смены уже использован" });
            if (await db.Set<PosShift>().AnyAsync(x => x.CashierId == actor && x.ClosedAt == null))
                return Results.Conflict(new { error = "У вас уже открыта смена" });
            var shift = new PosShift { Id = r.Id, CashierId = actor, OpeningCash = r.OpeningCash };
            db.Add(shift); Audit(db, u, shift.Id, "POS_SHIFT_OPENED");
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Ok(shift);
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
        group.MapPost("/shifts/{id:guid}/close", async (Guid id, CloseShift r, DadoDbContext db, ClaimsPrincipal u) =>
        {
            if (!Money(r.CountedCash)) return Results.BadRequest(new { error = "Проверьте фактическую сумму наличных" });
            await using var tx = await db.Database.BeginTransactionAsync();
            var owner = await Visible(db, u).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (owner is null) return Results.NotFound();
            await LockCashier(db, owner.CashierId);
            var shift = await db.Set<PosShift>().SingleAsync(x => x.Id == id);
            if (shift.ClosedAt != null && shift.CountedCash != r.CountedCash)
                return Results.Conflict(new { error = "Смена уже закрыта с другой фактической суммой" });
            if (shift.ClosedAt == null)
            {
                var totals = await Totals(db, shift);
                shift.ExpectedCash = totals.ExpectedCash; shift.CountedCash = r.CountedCash;
                shift.Difference = r.CountedCash - totals.ExpectedCash;
                shift.ClosedAt = DateTime.UtcNow; shift.ClosedBy = Actor(u);
                Audit(db, u, shift.Id, "POS_SHIFT_CLOSED");
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
            return Results.Ok(shift);
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
    }
}
