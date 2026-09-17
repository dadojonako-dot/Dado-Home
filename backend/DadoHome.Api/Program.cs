using DadoHome.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<DadoDbContext>(o=>o.UseNpgsql(builder.Configuration.GetConnectionString("DadoDb")));
builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen();
var app=builder.Build();app.UseSwagger();app.UseSwaggerUI();app.UseHttpsRedirection();

app.MapGet("/health",()=>Results.Ok(new{service="DadoHome.Api",status="ok"}));
app.MapGet("/api/products",async(DadoDbContext db)=>await db.Products.Where(x=>x.IsActive).ToListAsync());
app.MapPost("/api/products",async(Product p,DadoDbContext db)=>{p.Id=Guid.NewGuid();p.CreatedAt=DateTime.UtcNow;db.Products.Add(p);await db.SaveChangesAsync();return Results.Created($"/api/products/{p.Id}",p);});
app.MapGet("/api/orders",async(DadoDbContext db)=>await db.Orders.OrderByDescending(x=>x.CreatedAt).ToListAsync());
app.MapGet("/api/customers",async(DadoDbContext db)=>await db.Customers.ToListAsync());
app.MapGet("/api/wallets/{customerId:guid}",async(Guid customerId,DadoDbContext db)=>await db.Wallets.FirstOrDefaultAsync(x=>x.CustomerId==customerId) is {} w?Results.Ok(w):Results.NotFound());
app.MapGet("/api/audit",async(DadoDbContext db)=>await db.AuditLogs.OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync());
app.Run();
