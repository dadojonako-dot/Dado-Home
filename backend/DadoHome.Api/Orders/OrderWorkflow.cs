using System.Security.Claims;using DadoHome.Api.Auth;using DadoHome.Api.Data;using Microsoft.EntityFrameworkCore;
namespace DadoHome.Api.Orders;
public record OrderStatusRequest(string Status);
public static class OrderWorkflow{
 static readonly Dictionary<string,string[]> Allowed=new(StringComparer.OrdinalIgnoreCase){
  ["Created"]=["Confirmed","Cancelled"],
  ["Paid"]=["Confirmed","Cancelled"],
  ["Confirmed"]=["Picking","Cancelled"],
  ["Picking"]=["Ready","Cancelled"],
  ["Ready"]=["Courier","PickupReady","Cancelled"],
  ["Courier"]=["Completed"],
  ["PickupReady"]=["Completed"],
  ["Completed"]=[],["Cancelled"]=[]};
 public static void MapOrderWorkflow(this WebApplication app){
  app.MapPut("/api/admin/orders/{id:guid}/status",async(Guid id,OrderStatusRequest r,ClaimsPrincipal user,DadoDbContext db)=>{
   var order=await db.Orders.SingleOrDefaultAsync(x=>x.Id==id);if(order is null)return Results.NotFound();
   if(!Allowed.TryGetValue(order.Status,out var next)||!next.Contains(r.Status,StringComparer.OrdinalIgnoreCase))return Results.Conflict(new{error="InvalidStatusTransition",current=order.Status,allowed=next??[]});
   var old=order.Status;order.Status=r.Status;
   db.AuditLogs.Add(new AuditLog{Id=Guid.NewGuid(),ActorId=user.FindFirstValue(ClaimTypes.NameIdentifier)??user.FindFirstValue("sub")??"staff",Role=user.FindFirstValue(ClaimTypes.Role)??"",Action=$"ORDER_STATUS_{old}_TO_{r.Status}",Entity="Order",EntityId=order.Id.ToString()});
   await db.SaveChangesAsync();return Results.Ok(order);
  }).RequireAuthorization(p=>p.RequireRole(Roles.Administrator,Roles.OrderManager));
 }
 public static string[] Next(string status)=>Allowed.TryGetValue(status,out var next)?next:[];
}
