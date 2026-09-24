using DadoHome.Api.Auth;
using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DadoHome.Api.Pilot;

/// <summary>Opt-in demonstration data. Never enabled in Production or Development.</summary>
public static class PilotSeed
{
    public static async Task Initialize(DadoDbContext db, IConfiguration cfg, IHostEnvironment env, PasswordService passwords)
    {
        if (!env.IsEnvironment("Pilot") || !cfg.GetValue<bool>("Pilot:Enabled")) return;
        var roles = new[] { Roles.Administrator, Roles.Cashier, Roles.Finance, Roles.OrderManager, Roles.Support };
        var names = new[] { "Тест: Администратор", "Тест: Кассир", "Тест: Финансы", "Тест: Менеджер заказов", "Тест: Поддержка" };
        await using var transaction = await db.Database.BeginTransactionAsync();
        for (var i = 0; i < roles.Length; i++)
        {
            var id = Guid.Parse($"da000000-0000-4000-8000-{i + 1:000000000000}");
            if (await db.StaffUsers.AnyAsync(x => x.Id == id)) continue;
            var password = cfg[$"Pilot:Passwords:{roles[i]}"];
            if (string.IsNullOrEmpty(password) || password.Length < 16)
                throw new InvalidOperationException($"Pilot password missing for {roles[i]}; run the pilot launcher.");
            db.StaffUsers.Add(new StaffUser
            {
                Id = id, Name = names[i], Phone = $"+99290000000{i + 1}",
                Role = roles[i], PasswordHash = passwords.Hash(password), IsActive = true
            });
        }
        var categoryId = Guid.Parse("db000000-0000-4000-8000-000000000001");
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId))
            db.Categories.Add(new Category { Id = categoryId, Name = "Тестовый ассортимент" });
        var catalog = new (string Name, decimal Price, int Stock)[]
        {
            ("Полотенце хлопковое", 45m, 20), ("Набор кухонных салфеток", 25m, 30),
            ("Кружка керамическая", 35m, 15), ("Плед домашний", 150m, 8),
            ("Органайзер для хранения", 80m, 12), ("Подушка декоративная", 65m, 10),
            ("Последняя ваза — проверка остатка", 50m, 1), ("Нет в наличии — тест", 20m, 0)
        };
        for (var i = 0; i < catalog.Length; i++)
        {
            var id = Guid.Parse($"dc000000-0000-4000-8000-{i + 1:000000000000}");
            if (await db.Products.AnyAsync(x => x.Id == id)) continue;
            var (name, price, stock) = catalog[i];
            db.Products.Add(new Product { Id = id, Name = name, CategoryId = categoryId,
                Category = "Тестовый ассортимент", Price = price, Stock = stock,
                Description = "Демонстрационный товар для проверки кассы. Не реальный склад." });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
