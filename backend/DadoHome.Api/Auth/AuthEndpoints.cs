using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using DadoHome.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace DadoHome.Api.Auth;

public static class AuthEndpoints
{
    public static void ConfigureSecureAuth(this WebApplicationBuilder builder)
    {
        var cfg=builder.Configuration;var secret=cfg["Jwt:Key"]??"";
        if(Encoding.UTF8.GetByteCount(secret)<32||secret.Contains("CHANGE",StringComparison.OrdinalIgnoreCase)||secret.Contains("DEV_ONLY",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set Jwt__Key to a unique random secret of at least 32 bytes. Demo keys are rejected.");
        if(string.IsNullOrWhiteSpace(cfg["Jwt:Issuer"])||string.IsNullOrWhiteSpace(cfg["Jwt:Audience"]))throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required.");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options=>
        {
            options.TokenValidationParameters=new TokenValidationParameters
            {
                ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,
                ValidIssuer=cfg["Jwt:Issuer"],ValidAudience=cfg["Jwt:Audience"],ClockSkew=TimeSpan.FromSeconds(30),
                IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),ValidAlgorithms=[SecurityAlgorithms.HmacSha256]
            };
            options.Events=new JwtBearerEvents{OnTokenValidated=async context=>
            {
                var user=context.Principal!;
                if(!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier),out var id)){context.Fail("Invalid subject");return;}
                var db=context.HttpContext.RequestServices.GetRequiredService<DadoDbContext>();var role=user.FindFirstValue(ClaimTypes.Role);
                if(role==Roles.Customer){if(!await db.Customers.AnyAsync(x=>x.Id==id))context.Fail("Account unavailable");return;}
                if(!Roles.Staff.Contains(role)||!long.TryParse(user.FindFirstValue("auth_version"),out var version)){context.Fail("Session expired");return;}
                if(!await db.StaffUsers.AnyAsync(x=>x.Id==id&&x.IsActive&&x.Role==role&&x.AuthVersion==version))context.Fail("Session revoked");
            }};
        });
        builder.Services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=StatusCodes.Status429TooManyRequests;
            options.OnRejected=async(context,ct)=>
            {
                if(context.Lease.TryGetMetadata(MetadataName.RetryAfter,out var retry))context.HttpContext.Response.Headers.RetryAfter=Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new{error="Слишком много попыток. Повторите позже."},ct);
            };
            options.AddPolicy("StaffLogin",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=20,Window=TimeSpan.FromMinutes(5),QueueLimit=0}));
            options.AddPolicy("OtpRequest",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromHours(1),QueueLimit=0}));
            options.AddPolicy("OtpVerify",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(5),QueueLimit=0}));
        });
    }
    static string? Phone(string? input)
    {
        if(input is null||input.Length>32)return null;
        var normalized=Regex.Replace(input,"[\\s()\\-]","");
        return Regex.IsMatch(normalized,@"^\+[1-9][0-9]{7,14}$")?normalized:null;
    }
    public static void MapSecureAuth(this WebApplication app)
    {
        app.MapPost("/api/admin/auth/login",async(StaffLoginRequest r,DadoDbContext db,PasswordService passwords,JwtService jwt)=>
        {
            if(string.IsNullOrWhiteSpace(r.Phone)||r.Phone.Length>64||string.IsNullOrEmpty(r.Password)||r.Password.Length>1024)return Results.Unauthorized();
            await using var tx=await db.Database.BeginTransactionAsync();var phone=r.Phone.Trim();
            var staff=await db.StaffUsers.FromSqlInterpolated($"SELECT * FROM \"StaffUsers\" WHERE \"Phone\"={phone} FOR UPDATE").SingleOrDefaultAsync();
            if(staff is null||!staff.IsActive||!Roles.Staff.Contains(staff.Role)||staff.LockedUntil>DateTime.UtcNow){passwords.Verify(r.Password,passwords.DummyHash);return Results.Unauthorized();}
            if(staff.LockedUntil!=null){staff.FailedLoginCount=0;staff.LockedUntil=null;}
            if(!passwords.Verify(r.Password,staff.PasswordHash))
            {
                staff.FailedLoginCount++;if(staff.FailedLoginCount>=5)staff.LockedUntil=DateTime.UtcNow.AddMinutes(15);
                await db.SaveChangesAsync();await tx.CommitAsync();return Results.Unauthorized();
            }
            staff.FailedLoginCount=0;staff.LockedUntil=null;
            if(!staff.PasswordHash.StartsWith("v2."))staff.PasswordHash=passwords.Hash(r.Password);
            db.AuditLogs.Add(new AuditLog{Id=Guid.NewGuid(),ActorId=staff.Id.ToString(),Role=staff.Role,Action="STAFF_LOGIN",Entity="StaffUser",EntityId=staff.Id.ToString()});
            await db.SaveChangesAsync();await tx.CommitAsync();
            return Results.Ok(new{accessToken=jwt.Create(staff.Id,staff.Phone,staff.Role,staff.AuthVersion),expiresIn=1800,user=new{staff.Id,staff.Name,staff.Phone,staff.Role}});
        }).RequireRateLimiting("StaffLogin");
        app.MapPost("/api/auth/otp/request",(OtpRequest r,OtpService otp,IHostEnvironment env)=>
        {
            if(!env.IsDevelopment())return Results.Json(new{error="SmsProviderNotConfigured"},statusCode:503);
            var phone=Phone(r.Phone);if(phone is null)return Results.BadRequest(new{error="InvalidPhone"});var code=otp.Issue(phone);
            return code is null?Results.Json(new{error="Повторный код доступен через минуту"},statusCode:429):Results.Ok(new{sent=true,devCode=code});
        }).RequireRateLimiting("OtpRequest");
        app.MapPost("/api/auth/otp/verify",async(OtpVerify r,OtpService otp,JwtService jwt,DadoDbContext db,IHostEnvironment env)=>
        {
            if(!env.IsDevelopment())return Results.Json(new{error="SmsProviderNotConfigured"},statusCode:503);
            var phone=Phone(r.Phone);if(phone is null||!otp.Verify(phone,r.Code))return Results.Unauthorized();
            await using var tx=await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"customer:"+phone},0))");
            var user=await db.Customers.SingleOrDefaultAsync(x=>x.Phone==phone);
            if(user is null){user=new Customer{Id=Guid.NewGuid(),Phone=phone};db.Customers.Add(user);db.Wallets.Add(new Wallet{Id=Guid.NewGuid(),CustomerId=user.Id});}
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{accessToken=jwt.Create(user.Id,user.Phone,Roles.Customer),expiresIn=1800});
        }).RequireRateLimiting("OtpVerify");
    }
}
