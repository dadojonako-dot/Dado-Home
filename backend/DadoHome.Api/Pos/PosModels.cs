using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace DadoHome.Api.Pos;

public class PosReceipt
{
    public Guid Id { get; set; }
    public Guid CashierId { get; set; }
    public string Number { get; set; } = "";
    public string Status { get; set; } = "Held";
    public string PaymentMethod { get; set; } = "Cash";
    public decimal Total { get; set; }
    public decimal Tendered { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SoldAt { get; set; }
    public List<PosLine> Lines { get; set; } = [];
}
public class PosLine
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
}
public class PosReturn
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid LineId { get; set; }
    public Guid ActorId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class PosOperation
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid ReceiptId { get; set; }
}
// Reject unrecognized fields (including card credentials), rather than storing arbitrary payment data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PosItem(Guid ProductId, int Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PosWrite(Guid OperationId, List<PosItem> Items, string PaymentMethod, decimal Tendered, Guid? HeldReceiptId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PosReturnWrite(Guid OperationId, Guid LineId, int Quantity);

public static class PosModelConfiguration
{
    public static void ConfigurePos(this ModelBuilder b)
    {
        b.Entity<PosReceipt>().ToTable("PosReceipts");
        b.Entity<PosReceipt>().HasIndex(x => x.Number).IsUnique();
        b.Entity<PosReceipt>().HasIndex(x => new { x.CashierId, x.CreatedAt });
        b.Entity<PosReceipt>().Property(x => x.Total).HasPrecision(18, 2);
        b.Entity<PosReceipt>().Property(x => x.Tendered).HasPrecision(18, 2);
        b.Entity<PosReceipt>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PosLine>().ToTable("PosLines");
        b.Entity<PosLine>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Entity<PosLine>().HasOne<Data.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PosReturn>().ToTable("PosReturns");
        b.Entity<PosReturn>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<PosReturn>().HasOne<PosLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PosOperation>().ToTable("PosOperations");
        b.Entity<PosOperation>().HasOne<PosReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}
