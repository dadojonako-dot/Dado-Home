using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DadoHome.Api.Auth;
using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DadoHome.Api.Pos;

public static class PosEndpoints
{
    public static void MapPos(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/pos").RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier, Roles.Finance));
        // A disabled user or an old JWT with a previous role must not retain POS privileges.
        group.AddEndpointFilter(async (context, next) =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<DadoDbContext>();
            var user = context.HttpContext.User;
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Results.Unauthorized();
            var staff = await db.StaffUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive);
            if (staff is null || staff.Role != user.FindFirstValue(ClaimTypes.Role)) return Results.Forbid();
            try { return await next(context); }
            catch (PosException e) { return Results.Conflict(new { error = e.Message }); }
            catch (PostgresException e) when (e.SqlState is "40001" or "40P01") { return Results.Conflict(new { error = "Повторите операцию с тем же идентификатором" }); }
        });
        group.MapGet("/receipts", async (DadoDbContext db, ClaimsPrincipal user, string? status, int? page) =>
            Results.Ok(await Visible(db, user).Where(x => status == null || x.Status == status)
                .OrderByDescending(x => x.CreatedAt).Skip((Math.Clamp(page ?? 1, 1, 10000) - 1) * 50).Take(50).ToListAsync()));
        group.MapGet("/receipts/{id:guid}", async (Guid id, DadoDbContext db, ClaimsPrincipal user) =>
        {
            var receipt = await Visible(db, user).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id);
            return receipt is null ? Results.NotFound() : Results.Ok(new { receipt, returns = await db.Set<PosReturn>().Where(x => x.ReceiptId == id).OrderBy(x => x.CreatedAt).ToListAsync() });
        });
        group.MapGet("/report", async (DadoDbContext db, DateTime? from, DateTime? to) =>
        {
            var start = (from ?? DateTime.UtcNow.Date).ToUniversalTime();
            var end = (to ?? start.AddDays(1)).ToUniversalTime();
            if (end <= start || end - start > TimeSpan.FromDays(366)) return Results.BadRequest(new { error = "Неверный период (максимум 366 дней)" });
            var sold = db.Set<PosReceipt>().Where(x => x.SoldAt >= start && x.SoldAt < end);
            var gross = await sold.SumAsync(x => (decimal?)x.Total) ?? 0;
            var returned = await db.Set<PosReturn>().Where(x => x.CreatedAt >= start && x.CreatedAt < end).SumAsync(x => (decimal?)x.Amount) ?? 0;
            return Results.Ok(new { from = start, to = end, count = await sold.CountAsync(), gross, returned, net = gross - returned, currency = "TJS" });
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Finance));
        group.MapPost("/sales", (PosWrite r, DadoDbContext db, ClaimsPrincipal u) => Write(db, u, r, false)).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
        group.MapPost("/held", (PosWrite r, DadoDbContext db, ClaimsPrincipal u) => Write(db, u, r, true)).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
        group.MapPost("/receipts/{id:guid}/returns", Return).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
        group.MapDelete("/held/{id:guid}", async (Guid id, DadoDbContext db, ClaimsPrincipal u) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await Lock(db, id);
            var receipt = await Visible(db, u).SingleOrDefaultAsync(x => x.Id == id);
            if (receipt is null) return Results.NotFound();
            if (receipt.Status == "Cancelled") return Results.NoContent();
            if (receipt.Status != "Held") return Results.Conflict(new { error = "Чек уже проведен" });
            receipt.Status = "Cancelled";
            Audit(db, u, id, "POS_HELD_CANCELLED");
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Cashier));
    }

    static Guid Actor(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static IQueryable<PosReceipt> Visible(DadoDbContext db, ClaimsPrincipal u) => u.IsInRole(Roles.Cashier)
        ? db.Set<PosReceipt>().Where(x => x.CashierId == Actor(u))
        : db.Set<PosReceipt>();
    static async Task Lock(DadoDbContext db, Guid id) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))");
    static string Fingerprint(object request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    static void Audit(DadoDbContext db, ClaimsPrincipal u, Guid id, string action) => db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = Actor(u).ToString(), Role = u.FindFirstValue(ClaimTypes.Role)!, Action = action, Entity = "PosReceipt", EntityId = id.ToString() });
    static async Task<PosReceipt?> Replay(DadoDbContext db, ClaimsPrincipal u, Guid operation, string fingerprint)
    {
        if (operation == Guid.Empty) throw new PosException("Требуется идентификатор операции");
        await Lock(db, operation);
        var previous = await db.Set<PosOperation>().FindAsync(operation);
        if (previous is null) return null;
        if (previous.ActorId != Actor(u) || previous.Fingerprint != fingerprint) throw new PosException("Идентификатор уже использован для другой операции");
        return await db.Set<PosReceipt>().Include(x => x.Lines).SingleAsync(x => x.Id == previous.ReceiptId);
    }
    static async Task<IResult> Write(DadoDbContext db, ClaimsPrincipal u, PosWrite r, bool held)
    {
        if (r.Items is null || r.Items.Count is < 1 or > 100 || r.Items.Any(x => x is null || x.ProductId == Guid.Empty || x.Quantity is < 1 or > 1000)
            || r.Items.Select(x => x.ProductId).Distinct().Count() != r.Items.Count || r.PaymentMethod is not ("Cash" or "Card" or "QR")
            || r.Tendered < 0 || r.Tendered > 999999999999m || decimal.Round(r.Tendered, 2) != r.Tendered)
            return Results.BadRequest(new { error = "Неверные товары или оплата" });
        await using var tx = await db.Database.BeginTransactionAsync();
        var fingerprint = Fingerprint(new { held, request = r });
        var replay = await Replay(db, u, r.OperationId, fingerprint);
        if (replay is not null) return Results.Ok(replay);
        PosReceipt receipt;
        if (r.HeldReceiptId is Guid existing)
        {
            await Lock(db, existing);
            receipt = await Visible(db, u).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == existing) ?? throw new PosException("Чек недоступен");
            if (receipt.Status != "Held") throw new PosException("Чек уже проведен или отменен");
            db.Set<PosLine>().RemoveRange(receipt.Lines); receipt.Lines = [];
        }
        else
        {
            receipt = new PosReceipt { Id = Guid.NewGuid(), CashierId = Actor(u), Number = $"POS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}" };
            db.Add(receipt);
        }
        receipt.Total = 0;
        // Lock products in a stable order, shared with mobile orders through PostgreSQL row locks.
        foreach (var item in r.Items.OrderBy(x => x.ProductId))
        {
            var p = await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {item.ProductId} FOR UPDATE").SingleOrDefaultAsync();
            if (p is null || !p.IsActive || p.Price < 0) throw new PosException("Товар недоступен");
            if (!held)
            {
                if (p.Stock < item.Quantity) throw new PosException($"Недостаточно товара: {p.Name}");
                p.Stock -= item.Quantity;
            }
            var line = new PosLine { Id = Guid.NewGuid(), ReceiptId = receipt.Id, ProductId = p.Id, ProductName = p.Name, Quantity = item.Quantity, UnitPrice = p.Price };
            receipt.Lines.Add(line);
            db.Add(line);
            receipt.Total += item.Quantity * p.Price;
        }
        if (!held && (r.Tendered < receipt.Total || (r.PaymentMethod != "Cash" && r.Tendered != receipt.Total))) throw new PosException("Проверьте сумму оплаты: цена могла измениться");
        receipt.PaymentMethod = r.PaymentMethod;
        receipt.Tendered = held ? 0 : r.Tendered;
        receipt.Status = held ? "Held" : "Sold";
        receipt.SoldAt = held ? null : DateTime.UtcNow;
        db.Add(new PosOperation { Id = r.OperationId, ActorId = Actor(u), Fingerprint = fingerprint, ReceiptId = receipt.Id });
        Audit(db, u, receipt.Id, held ? "POS_HELD_SAVED" : "POS_SALE_COMPLETED");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(receipt);
    }
    static async Task<IResult> Return(Guid id, PosReturnWrite r, DadoDbContext db, ClaimsPrincipal u)
    {
        if (r.Quantity is < 1 or > 1000) return Results.BadRequest(new { error = "Неверное количество" });
        await using var tx = await db.Database.BeginTransactionAsync();
        var fingerprint = Fingerprint(new { returnReceipt = id, request = r });
        var replay = await Replay(db, u, r.OperationId, fingerprint);
        if (replay is not null) return Results.Ok(replay);
        await Lock(db, id);
        var receipt = await Visible(db, u).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id);
        if (receipt is null) return Results.NotFound();
        if (receipt.Status is not ("Sold" or "PartiallyReturned")) throw new PosException("Возврат недоступен");
        var line = receipt.Lines.SingleOrDefault(x => x.Id == r.LineId);
        if (line is null || r.Quantity > line.Quantity - line.ReturnedQuantity) throw new PosException("Превышено доступное количество возврата");
        await db.Products.Where(x => x.Id == line.ProductId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Stock, x => x.Stock + r.Quantity));
        line.ReturnedQuantity += r.Quantity;
        receipt.Status = receipt.Lines.All(x => x.ReturnedQuantity == x.Quantity) ? "Returned" : "PartiallyReturned";
        db.Add(new PosReturn { Id = Guid.NewGuid(), ReceiptId = id, LineId = line.Id, ActorId = Actor(u), Quantity = r.Quantity, Amount = r.Quantity * line.UnitPrice });
        db.Add(new PosOperation { Id = r.OperationId, ActorId = Actor(u), Fingerprint = fingerprint, ReceiptId = id });
        Audit(db, u, id, "POS_RETURN_COMPLETED");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(receipt);
    }
}
public sealed class PosException(string message) : Exception(message);
