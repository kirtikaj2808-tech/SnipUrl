using System.Text;
using AuthService.Common;
using AuthService.EndPoints;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── 1. OpenAPI (Scalar UI) ────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── 2. PostgreSQL via EF Core ─────────────────────────────────────────────
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── 3. Register JwtTokenService in DI ────────────────────────────────────
// Scoped = one instance per HTTP request
builder.Services.AddScoped<JwtTokenService>();

// ── 4. Configure JWT Authentication ──────────────────────────────────────
// This tells ASP.NET Core HOW to validate incoming JWT tokens
var jwtKey = builder.Configuration["Jwt:SecretKey"]!;

builder.Services.AddAuthentication(options =>
{
    // Set JWT as the default authentication scheme
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,           // Check the "iss" claim matches our issuer
        ValidateAudience = true,         // Check the "aud" claim matches our audience
        ValidateLifetime = true,         // Reject expired tokens
        ValidateIssuerSigningKey = true, // Verify the signature with our secret key

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

        // Remove default 5-minute clock skew tolerance (tokens expire exactly on time)
        ClockSkew = TimeSpan.Zero
    };
});

// ── 5. Authorization ──────────────────────────────────────────────────────
builder.Services.AddAuthorization();

var app = builder.Build();

// ── 6. Auto-run migrations on startup ────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.Migrate();
}

// ── 7. Scalar API docs in development ────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ── 8. Auth middleware ORDER MATTERS ─────────────────────────────────────
// UseAuthentication must come BEFORE UseAuthorization
// Authentication = "who are you?" (reads + validates JWT)
// Authorization  = "are you allowed?" (enforces [Authorize] rules)
app.UseAuthentication();
app.UseAuthorization();

// ── 9. Map endpoints ──────────────────────────────────────────────────────
app.MapRegisterEndpoint();
app.MapLoginEndpoint();

app.Run();
