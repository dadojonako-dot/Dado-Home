using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.IdentityModel.Tokens;
namespace DadoHome.Api.Auth;
public static class Roles
{
    public const string Administrator="Administrator", OrderManager="OrderManager", Finance="Finance", Support="Support", Cashier="Cashier", Customer="Customer";
    public static readonly string[] Staff=[Administrator,OrderManager,Finance,Support,Cashier];
}
public record OtpRequest(string Phone);
public record OtpVerify(string Phone,string Code);
public record StaffLoginRequest(string Phone,string Password);
// Development-only. Production OTP remains unavailable until an SMS integration is installed.
public sealed class OtpService
{
    private readonly object gate=new();
    private readonly Dictionary<string,(string Hash,DateTime Expires,DateTime Sent,int Attempts)> codes=new();
    public string? Issue(string phone)
    {
        lock(gate)
        {
            var now=DateTime.UtcNow;
            foreach(var key in codes.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())codes.Remove(key);
            if(codes.Count>=10000||codes.TryGetValue(phone,out var old)&&old.Sent.AddMinutes(1)>now)return null;
            var code=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");
            codes[phone]=(Hash(code),now.AddMinutes(5),now,0);return code;
        }
    }
    public bool Verify(string phone,string code)
    {
        lock(gate)
        {
            if(!codes.TryGetValue(phone,out var entry)||entry.Expires<=DateTime.UtcNow||entry.Attempts>=5)return false;
            codes[phone]=(entry.Hash,entry.Expires,entry.Sent,entry.Attempts+1);
            if(code is null||code.Length!=6||!code.All(char.IsAsciiDigit))return false;
            var valid=CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.Hash),Convert.FromHexString(Hash(code)));
            if(valid)codes.Remove(phone);return valid;
        }
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public sealed class PasswordService
{
    public string DummyHash {get;}
    public PasswordService(){DummyHash=Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));}
    public string Hash(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=KeyDerivation.Pbkdf2(password,salt,KeyDerivationPrf.HMACSHA256,600000,32);
        return $"v2.600000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public bool Verify(string? password,string? stored)
    {
        if(string.IsNullOrEmpty(password)||password.Length>1024||string.IsNullOrEmpty(stored))return false;
        try
        {
            var parts=stored.Split('.');var modern=parts.Length==4&&parts[0]=="v2"&&parts[1]=="600000";
            if(!modern&&parts.Length!=2)return false;
            var salt=Convert.FromBase64String(parts[modern?2:0]);var expected=Convert.FromBase64String(parts[modern?3:1]);
            if(salt.Length!=16||expected.Length!=32)return false;
            var actual=KeyDerivation.Pbkdf2(password,salt,KeyDerivationPrf.HMACSHA256,modern?600000:120000,32);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        }catch(FormatException){return false;}
    }
}
public class JwtService(IConfiguration cfg)
{
    public string Create(Guid id,string phone,string role,long? authVersion=null)
    {
        var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var claims=new List<Claim>{new(JwtRegisteredClaimNames.Sub,id.ToString()),new(ClaimTypes.MobilePhone,phone),new(ClaimTypes.Role,role),new(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString())};
        if(authVersion.HasValue)claims.Add(new Claim("auth_version",authVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(cfg["Jwt:Issuer"],cfg["Jwt:Audience"],claims,expires:DateTime.UtcNow.AddMinutes(30),signingCredentials:new SigningCredentials(key,SecurityAlgorithms.HmacSha256)));
    }
}
