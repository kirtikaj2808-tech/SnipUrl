using AuthService.Common;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuthService.EndPoints;

public static class LoginEndpoint
{
    public static void MapLoginEndpoint(this WebApplication app)
    {
        app.MapPost("/auth/login", async (LoginRequest request, AuthDbContext db, JwtTokenService tokenService) =>
        {
            // Step 1: Basic validation
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest("Email and password are required.");

            // Step 2: Find the user by email (case-insensitive)
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

            // Step 3: Return 401 if user not found
            // IMPORTANT: Use the same generic error message whether user not found OR password wrong
            // This prevents "user enumeration" attacks (attacker finds out if email exists)
            if (user is null)
                return Results.Unauthorized();

            // Step 4: Verify the password against the stored BCrypt hash
            // BCrypt.Verify hashes the input with the same salt stored in the hash and compares
            var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

            if (!passwordValid)
                return Results.Unauthorized();

            // Step 5: Generate JWT token for the authenticated user
            var token = tokenService.GenerateToken(user);

            // Step 6: Return the token and user info
            return Results.Ok(new LoginResponse(
                Token: token,
                Email: user.Email,
                Username: user.Username,
                Role: user.Role
            ));
        })
        .WithName("Login")
        .WithSummary("Login and get a JWT token")
        .WithDescription("Validates credentials and returns a signed JWT token")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);
    }
}

// What the client sends
public record LoginRequest(string Email, string Password);

// What we send back - includes the JWT token
public record LoginResponse(string Token, string Email, string Username, string Role);
