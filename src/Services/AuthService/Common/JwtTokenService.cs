using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Common;

public class JwtTokenService
{
    private readonly IConfiguration _config;

    // IConfiguration is injected by DI - reads values from appsettings.json
    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(User user)
    {
        // Step 1: Read JWT settings from appsettings.json
        var secretKey = _config["Jwt:SecretKey"]!;
        var issuer    = _config["Jwt:Issuer"]!;
        var audience  = _config["Jwt:Audience"]!;
        var expiryMinutes = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

        // Step 2: Create the signing key
        // SymmetricSecurityKey wraps your secret bytes
        // SecurityAlgorithms.HmacSha256 = sign with HMAC-SHA256
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Step 3: Define Claims - the "payload" of the JWT
        // Claims are key-value pairs that describe the user
        // JwtRegisteredClaimNames are standard claim names (sub, email, jti)
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),   // subject = user ID
            new Claim(JwtRegisteredClaimNames.Email, user.Email),         // email claim
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // unique token ID
            new Claim(ClaimTypes.Name, user.Username),                    // username
            new Claim(ClaimTypes.Role, user.Role),                        // role = "User"/"Admin"
        };

        // Step 4: Build the JWT token descriptor
        var token = new JwtSecurityToken(
            issuer: issuer,             // who issued the token (your app)
            audience: audience,         // who the token is for (your app / clients)
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes), // expiry time
            signingCredentials: credentials
        );

        // Step 5: Serialize the token to a string (the 3-part JWT string)
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
