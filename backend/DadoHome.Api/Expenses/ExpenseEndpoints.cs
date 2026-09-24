using System.Security.Claims;
using DadoHome.Api.Auth;
using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DadoHome.Api.Expenses;

public class StoreExpense
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public static class ExpenseEndpoints
{
    public static readonly string[] Categories = ["Аренда", "Зарплата", "Коммунальные услуги", "Закупка товаров", "Транспорт", "Реклама", "Ремонт и оборудование", "Прочее"];
    public static void MapExpenses(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/expenses").RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Finance));
        group.MapGet("/categories", () => Categories);
        group.MapGet("", async (DateOnly? from, DateOnly? to, string? category, int? page, DadoDbContext db) =>
        {
            if (from > to || page < 1 || (category != null && !Categories.Contains(category))) return Results.BadRequest(new { error = "Проверьте период и категорию" });
            var query = db.Set<StoreExpense>().AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(x => x.Date >= from.Value);
            if (to.HasValue) query = query.Where(x => x.Date <= to.Value);
            if (category != null) query = query.Where(x => x.Category == category);
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var count = await query.CountAsync();
            var total = await query.Where(x => x.CancelledAt == null).SumAsync(x => x.Amount);
            var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(((page ?? 1) - 1) * 50).Take(50).ToListAsync();
            await tx.CommitAsync();
            return Results.Ok(new { items, count, total, page = page ?? 1, pageSize = 50 });
        });
        group.MapPost("", async (ExpenseWrite r, ClaimsPrincipal user, DadoDbContext db) =>
        {
            if (r.Id == Guid.Empty || r.Date == default || r.Amount <= 0 || r.Amount > 999999999999m || decimal.Round(r.Amount, 2) != r.Amount || !Categories.Contains(r.Category) || string.IsNullOrWhiteSpace(r.Description) || r.Description.Length > 1000)
                return Results.BadRequest(new { error = "Укажите дату, категорию, описание и положительную сумму с точностью до дирамов" });
            var actor = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({r.Id.ToString()}, 0))");
            var old = await db.Set<StoreExpense>().FindAsync(r.Id);
            if (old != null) return old.Date == r.Date && old.Amount == r.Amount && old.Category == r.Category && old.Description == r.Description.Trim() && old.CreatedBy == actor
                ? Results.Ok(old) : Results.Conflict(new { error = "Этот идентификатор уже использован для другого расхода" });
            var expense = new StoreExpense { Id = r.Id, Date = r.Date, Amount = r.Amount, Category = r.Category, Description = r.Description.Trim(), CreatedBy = actor };
            db.Add(expense);
            Audit(db, user, expense.Id, "EXPENSE_CREATED");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/admin/expenses/{expense.Id}", expense);
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator));
        group.MapPost("/{id:guid}/cancel", async (Guid id, ExpenseCancel r, ClaimsPrincipal user, DadoDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) return Results.BadRequest(new { error = "Укажите причину отмены (до 500 символов)" });
            await using var tx = await db.Database.BeginTransactionAsync();
            var expense = await db.Set<StoreExpense>().FromSqlInterpolated($"SELECT * FROM \"StoreExpenses\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync();
            if (expense == null) return Results.NotFound();
            if (expense.CancelledAt != null) return Results.Ok(expense);
            expense.CancelledAt = DateTime.UtcNow;
            expense.CancellationReason = r.Reason.Trim();
            Audit(db, user, id, "EXPENSE_CANCELLED");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(expense);
        }).RequireAuthorization(p => p.RequireRole(Roles.Administrator));
    }
    static void Audit(DadoDbContext db, ClaimsPrincipal user, Guid id, string action) => db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = user.FindFirstValue(ClaimTypes.NameIdentifier)!, Role = user.FindFirstValue(ClaimTypes.Role)!, Entity = "StoreExpense", EntityId = id.ToString(), Action = action });
}
public record ExpenseWrite(Guid Id, DateOnly Date, decimal Amount, string Category, string Description);
public record ExpenseCancel(string Reason);
