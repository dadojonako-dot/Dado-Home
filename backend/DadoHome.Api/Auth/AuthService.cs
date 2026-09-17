using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
namespace DadoHome.Api.Auth;
public static class Roles{public const string Administrator="Administrator";public const string OrderManager="OrderManager";public const string Finance="Finance";public const string Support="Support";public const string Customer="Customer";}
public record OtpRequest(string Phone);
public record OtpVerify(string Phone,string Code);
public class OtpService{private readonly Dictionary<string,(string Hash,DateTime Expires,int Attempts)> _codes=new();public string Issue(string phone){var code=RandomNumberGenerator.GetInt32(100000,1000000).ToString();_codes[phone]=(Hash(code),DateTime.UtcNow.AddMinutes(5),0);return code;}public bool Verify(string phone,string code){if(!_codes.TryGetValue(phone,out var x)||x.Expires<DateTime.UtcNow||x.Attempts>=5)return false;_codes[phone]=(x.Hash,x.Expires,x.Attempts+1);var ok=CryptographicOperations.FixedTimeEquals(Convert.FromHexString(x.Hash),Convert.FromHexString(Hash(code)));if(ok)_codes.Remove(phone);return ok;}private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));}
public class JwtService(IConfiguration cfg){public string Create(Guid id,string phone,string role){var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]??throw new InvalidOperationException("Jwt:Key missing")));var claims=new[]{new Claim(JwtRegisteredClaimNames.Sub,id.ToString()),new Claim(ClaimTypes.MobilePhone,phone),new Claim(ClaimTypes.Role,role),new Claim(JwtRegisteredClaimNames.Jti,Guid.NewGuid().ToString())};var token=new JwtSecurityToken(cfg["Jwt:Issuer"],cfg["Jwt:Audience"],claims,expires:DateTime.UtcNow.AddMinutes(30),signingCredentials:new SigningCredentials(key,SecurityAlgorithms.HmacSha256));return new JwtSecurityTokenHandler().WriteToken(token);}}
