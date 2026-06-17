using AuthService.Domain.Entities;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuthService.EndPoints;

public static class RegisterEndpoint
{
    public static void MapRegisterEndpoint(this WebApplication app)
    {
        app.MapPost("/auth/register", async (RegisterRequest request, AuthDbContext db) =>
        {
            // Step 1: Basic validation
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest("Email and password are required.");

            if (request.Password.Length < 6)
                return Results.BadRequest("Password must be at least 6 characters.");

            // Step 2: Check if email already exists
            // AnyAsync = SQL: SELECT COUNT(*) > 0 WHERE Email = '...'
            var emailExists = await db.Users.AnyAsync(u => u.Email == request.Email.ToLower());
            if (emailExists)
                return Results.Conflict("An account with this email already exists.");

            // Step 3: Hash the password using BCrypt
            // BCrypt.HashPassword automatically generates a random salt + hashes
            // WorkFactor 11 = 2^11 = 2048 iterations (slow by design - harder to brute-force)
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 11);

            // Step 4: Create the user entity
            var user = new User
            {
                Email = request.Email.ToLower().Trim(),
                Username = request.Username.Trim(),
                PasswordHash = passwordHash,
                Role = "User",
                CreatedAt = DateTime.UtcNow
            };

            // Step 5: Save to database
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Step 6: Return 201 Created with user info (never return the password hash)
            return Results.Created($"/auth/users/{user.Id}", new RegisterResponse(
                user.Id,
                user.Email,
                user.Username,
                user.CreatedAt
            ));
        })
        .WithName("Register")
        .WithSummary("Register a new user")
        .WithDescription("Creates a new user account with a hashed password")
        .Produces<RegisterResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
    }
}

// What the client sends
public record RegisterRequest(string Email, string Username, string Password);

// What we send back - notice NO PasswordHash in the response
public record RegisterResponse(int Id, string Email, string Username, DateTime CreatedAt);
