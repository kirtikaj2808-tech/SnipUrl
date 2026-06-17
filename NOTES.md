# SnipUrl – URL Shortener: Build-From-Scratch Notes

> Written: June 16, 2026  
> Stack: .NET 10 · ASP.NET Core Minimal API · EF Core · PostgreSQL · Docker

---

## What Is This Project?

A URL shortener (like bit.ly / TinyURL) that:
- Takes a long URL and returns a short code (e.g. `http://localhost:5132/dnh`)
- Redirects anyone who visits the short link to the original URL
- Optionally lets you set a custom alias (e.g. `/my-link`) and expiry date
- Tracks how many times each link was clicked

---

## Project Structure (Folder Layout)

```
SnipUrl/
├── docker-compose.yml               ← spins up PostgreSQL + Redis
├── SnipUrl.slnx                     ← solution file
└── src/
    └── Services/
        └── UrlService/
            ├── Program.cs           ← app entry point, DI setup
            ├── UrlService.csproj    ← NuGet packages
            ├── appsettings.json     ← connection string + base URL
            ├── Common/
            │   └── Base62Encoder.cs ← algorithm: number → short code
            ├── Domain/
            │   └── Entities/
            │       └── ShortUrl.cs  ← the data model / entity
            ├── Infrastructure/
            │   └── Persistence/
            │       └── UrlDbContext.cs ← EF Core DbContext
            ├── EndPoints/
            │   ├── ShortenEndpoint.cs  ← POST /shorten
            │   └── RedirectEndpoint.cs ← GET /{code}
            └── Migrations/
                └── 20260616194537_InitialCreate.cs ← auto-generated DB migration
```

**Architecture pattern used:** Feature-sliced / layered inside a single service:
- `Domain` → pure C# models, no dependencies
- `Infrastructure` → database stuff (EF Core)
- `Common` → shared utilities
- `EndPoints` → HTTP layer (routes/handlers)

---

## Concepts Used & How They Work

### 1. ASP.NET Core Minimal API

Instead of Controllers, Minimal APIs register routes directly on `WebApplication`:

```csharp
app.MapPost("/shorten", async (ShortenUrlRequest request, UrlDbContext db) => { ... });
app.MapGet("/{code}",   async (string code, UrlDbContext db) => { ... });
```

- **Extension method pattern** – each endpoint lives in its own static class with a `Map___Endpoint(this WebApplication app)` method. Keeps `Program.cs` clean.
- `Results.Ok()`, `Results.BadRequest()`, `Results.NotFound()`, `Results.Redirect()`, `Results.Conflict()` – built-in result helpers.

---

### 2. Entity / Domain Model (`ShortUrl.cs`)

```csharp
public class ShortUrl
{
    public int Id { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public string? CustomAlias { get; set; }    // nullable = optional
    public int ClickCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }    // nullable = never expires if null
    public bool IsActive { get; set; } = true;
}
```

Key ideas:
- Use `string.Empty` as default (not null) for required strings
- Use `?` (nullable types) for optional fields
- `DateTime.UtcNow` – always store dates in UTC

---

### 3. EF Core + PostgreSQL (`UrlDbContext.cs`)

**DbContext** = the bridge between C# and the database.

```csharp
public class UrlDbContext : DbContext
{
    public UrlDbContext(DbContextOptions<UrlDbContext> options) : base(options) { }

    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();  // = the ShortUrls table

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrl>(entity =>
        {
            entity.HasKey(e => e.Id);                              // primary key
            entity.HasIndex(e => e.ShortCode).IsUnique();          // unique index
            entity.HasIndex(e => e.CustomAlias).IsUnique();        // unique index
            entity.Property(e => e.OriginalUrl).IsRequired().HasMaxLength(2048);
        });
    }
}
```

- **Fluent API** in `OnModelCreating` to configure constraints (unique indexes, max lengths, required fields)
- **Unique index** = DB-level guarantee no two rows share the same ShortCode

Register it in `Program.cs`:
```csharp
builder.Services.AddDbContext<UrlDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
```

---

### 4. Migrations

Migrations = version-controlled snapshots of your DB schema.

```bash
# Generate a migration (run once after adding/changing entities)
dotnet ef migrations add InitialCreate

# Apply migrations to the database
dotnet ef database update
```

We also **auto-run migrations on startup** so the app self-deploys:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
    db.Database.Migrate();  // creates tables if they don't exist
}
```

`using (var scope = ...)` – needed because `DbContext` is scoped, but `Program.cs` runs at root scope.

---

### 5. Base62 Encoding Algorithm (`Base62Encoder.cs`)

**The core algorithm** that makes URL shortening work:

```
Characters = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
             (62 characters total)
```

Works like converting a number from base 10 to base 62:
- Take the DB auto-generated `Id` (e.g. 12345)
- Repeatedly divide by 62, collect remainders as characters
- Stack them → short string (e.g. `"dnh"`)

Why use the DB `Id`? Because every row gets a unique, auto-incrementing integer — guaranteed unique short codes without collision checking.

**Two-save pattern** (important):
```csharp
db.ShortUrls.Add(shortUrl);
await db.SaveChangesAsync();          // Save #1: get the auto-generated Id

shortUrl.ShortCode = Base62Encoder.Encode(shortUrl.Id);
await db.SaveChangesAsync();          // Save #2: store the computed short code
```

---

### 6. POST /shorten Endpoint Logic (`ShortenEndpoint.cs`)

Step-by-step what happens when a user shortens a URL:

1. **Validate** – is the URL empty? Is it a valid absolute URI?
2. **Custom alias check** – if provided, query DB to see if it's already taken → `409 Conflict`
3. **Create entity** – populate `ShortUrl` object
4. **Save to DB** → get auto-generated `Id`
5. **Generate short code** – Base62 encode the `Id`, OR use the custom alias
6. **Save again** – store the short code
7. **Return response** – build `http://localhost:5132/{shortCode}` and return it

C# Records used for request/response (immutable DTOs):
```csharp
public record ShortenUrlRequest(string Url, string? CustomAlias, DateTime? ExpiresAt);
public record ShortenUrlResponse(string ShortUrl, string ShortCode, string OriginalUrl, DateTime CreatedAt, DateTime? ExpiresAt);
```

---

### 7. GET /{code} Redirect Endpoint Logic (`RedirectEndpoint.cs`)

Step-by-step when someone visits a short link:

1. **Look up** – query DB by `ShortCode` OR `CustomAlias`
2. **404** if not found
3. **Check IsActive** – return `400` if deactivated
4. **Check expiry** – if `ExpiresAt` < now → return `400`
5. **Increment click count** – `shortUrl.ClickCount++` then save
6. **Redirect** – `Results.Redirect(originalUrl)` → sends HTTP 302 to the browser

---

### 8. Dependency Injection (DI)

ASP.NET Core has DI built-in. You register services once, then the framework injects them automatically:

```csharp
// Register (Program.cs)
builder.Services.AddDbContext<UrlDbContext>(...);

// Inject (endpoint parameter – framework handles this automatically)
app.MapPost("/shorten", async (ShortenUrlRequest request, UrlDbContext db) => { ... });
```

- `UrlDbContext` is **scoped** (one instance per HTTP request) by default with `AddDbContext`

---

### 9. OpenAPI + Scalar UI

```csharp
builder.Services.AddOpenApi();           // generate OpenAPI spec
app.MapOpenApi();                        // serve it at /openapi/v1.json
app.MapScalarApiReference();             // serve interactive UI at /scalar/v1
```

Endpoint metadata (shows up in Scalar docs):
```csharp
.WithName("ShortenUrl")
.WithSummary("Shorten a long URL")
.WithDescription("Takes a long URL and returns a shortened version")
.Produces<ShortenUrlResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
```

---

### 10. Docker Compose

`docker-compose.yml` spins up infrastructure locally:

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: snipurl
      POSTGRES_PASSWORD: snipurl123
      POSTGRES_DB: snipurldb
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data   # data persists between restarts

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
```

Start with: `docker-compose up -d`  
Redis is provisioned but not yet wired into the app (future use: caching).

---

### 11. Configuration (`appsettings.json`)

```json
{
  "BaseUrl": "http://localhost:5132",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=snipurldb;Username=snipurl;Password=snipurl123"
  }
}
```

Read in code via:
```csharp
builder.Configuration.GetConnectionString("DefaultConnection")
app.Configuration["BaseUrl"]
```

---

## NuGet Packages Used

| Package | Purpose |
|---|---|
| `Microsoft.AspNetCore.OpenApi` | Generate OpenAPI spec from Minimal API |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core provider for PostgreSQL |
| `Microsoft.EntityFrameworkCore.Design` | Enables `dotnet ef` CLI commands (migrations) |
| `Scalar.AspNetCore` | Interactive API docs UI (alternative to Swagger UI) |

---

## How to Rebuild From Scratch (Step by Step)

1. `dotnet new web -n UrlService` – create Minimal API project
2. Add NuGet packages (see above)
3. Create `Domain/Entities/ShortUrl.cs` – the entity with all fields
4. Create `Infrastructure/Persistence/UrlDbContext.cs` – configure EF Core mappings
5. Create `Common/Base62Encoder.cs` – the encoding algorithm
6. Create `EndPoints/ShortenEndpoint.cs` – POST /shorten logic
7. Create `EndPoints/RedirectEndpoint.cs` – GET /{code} logic
8. Wire everything in `Program.cs` – DI registration, middleware, endpoint mapping, auto-migration
9. Add `appsettings.json` – connection string and base URL
10. Create `docker-compose.yml` – PostgreSQL container
11. Run `docker-compose up -d` to start Postgres
12. Run `dotnet ef migrations add InitialCreate` to generate migration
13. `dotnet run` – app starts, auto-migrates, and is ready

---

## Key Interview / Revision Points

- **Why Base62?** URL-safe, compact, case-sensitive 62-char alphabet → 6 chars can represent ~57 billion combinations
- **Why save twice?** You need the DB-generated `Id` before you can encode it into a short code
- **Why unique indexes?** Enforce uniqueness at DB level, not just app level — safer
- **Why `DateTime.UtcNow`?** Avoid timezone bugs; always store UTC, convert to local only when displaying
- **Why extension methods for endpoints?** Keeps `Program.cs` minimal; each endpoint is self-contained
- **What is a scoped service?** Created once per HTTP request, disposed after. Perfect for `DbContext`
- **What does `db.Database.Migrate()` do?** Applies any pending migrations automatically on startup — useful for containerized deployments

---
---

# AuthService – JWT Auth: Build-From-Scratch Notes

> Written: June 17, 2026  
> Stack: .NET 10 · ASP.NET Core Minimal API · EF Core · PostgreSQL · BCrypt · JWT

---

## What Is This Service?

Handles user identity for the SnipUrl system:
- `POST /auth/register` — create a new account (hashed password stored in DB)
- `POST /auth/login` — validate credentials and return a signed JWT token
- Other services can validate that JWT to know who the caller is

---

## Project Structure

```
AuthService/
├── Domain/Entities/
│   └── User.cs                          ← user data model
├── Infrastructure/Persistence/
│   └── AuthDbContext.cs                 ← EF Core DbContext + schema config
├── Common/
│   └── JwtTokenService.cs              ← builds & signs JWT tokens
├── EndPoints/
│   ├── RegisterEndpoint.cs             ← POST /auth/register
│   └── LoginEndpoint.cs                ← POST /auth/login
├── Migrations/
│   └── InitialCreate.cs                ← auto-generated Users table migration
├── appsettings.json                     ← JWT config + DB connection string
└── Program.cs                           ← DI wiring + middleware pipeline
```

---

## Concepts Used & How They Work

### 1. User Entity (`Domain/Entities/User.cs`)

```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty; // NEVER store plain text
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "User";               // for authorization later
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- `Role` = `"User"` or `"Admin"` — used later to restrict certain endpoints
- `PasswordHash` stores the BCrypt output, never the real password

---

### 2. BCrypt Password Hashing

**Why not MD5/SHA256?** Those are fast — a GPU can try billions per second. BCrypt is intentionally slow (work factor controls cost).

```csharp
// On REGISTER — hash the password before saving
var hash = BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 11);
// Output looks like: "$2a$11$abc123randomsalt...hashedresult"
//                         ↑ version  ↑ work factor  ↑ salt + hash combined

// On LOGIN — verify the input against the stored hash
bool valid = BCrypt.Net.BCrypt.Verify(plainPassword, storedHash);
// BCrypt extracts the salt from storedHash, re-hashes the input, and compares
```

Key facts:
- **Salt is embedded** in the hash string — no separate salt column needed
- `workFactor: 11` = 2^11 = 2048 hashing rounds
- Same password hashed twice → **different outputs** (because random salt each time)
- `Verify()` always returns `true` or `false` — you never decrypt a hash

---

### 3. What is a JWT (JSON Web Token)?

A JWT is a **self-contained, signed token** — 3 parts separated by dots:

```
eyJhbGciOiJIUzI1NiJ9  .  eyJzdWIiOiIxIn0  .  SflKxwRJSMeKKF2QT4fwpMeJ
      HEADER                   PAYLOAD            SIGNATURE
```

- **Header** — algorithm: `{ "alg": "HS256" }`
- **Payload** — claims: `{ "sub": "1", "email": "a@b.com", "role": "User", "exp": 1234567890 }`
- **Signature** — `HMACSHA256(base64(header) + "." + base64(payload), secretKey)`

Anyone can decode the header + payload (it's just Base64). Only the server can **verify** the signature — if the payload was tampered with, the signature won't match.

**Why JWT is stateless:** The server does not store tokens. It just validates the signature on every request using its secret key — no DB lookup needed.

---

### 4. JwtTokenService (`Common/JwtTokenService.cs`)

```csharp
public string GenerateToken(User user)
{
    // 1. Build the signing key from the secret
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    // 2. Define claims (the payload)
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // unique token ID
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
    };

    // 3. Build the token
    var token = new JwtSecurityToken(
        issuer: issuer, audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(60),
        signingCredentials: credentials
    );

    // 4. Serialize to string
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

- **`Jti` claim** = unique ID per token, useful for token revocation later
- **`IConfiguration` injected** via constructor — reads `Jwt:SecretKey` etc. from `appsettings.json`
- Registered as `AddScoped<JwtTokenService>()` → injected into login endpoint

---

### 5. Register Endpoint — Step by Step

```
POST /auth/register
Body: { "email": "a@b.com", "username": "alice", "password": "secret123" }
```

1. Validate email + password not empty, password ≥ 6 chars
2. `db.Users.AnyAsync(u => u.Email == request.Email)` — check email taken → `409 Conflict`
3. `BCrypt.HashPassword(request.Password, 11)` — hash the password
4. Create `User` entity, save to DB
5. Return `201 Created` with user info — **never include `PasswordHash` in the response**

---

### 6. Login Endpoint — Step by Step

```
POST /auth/login
Body: { "email": "a@b.com", "password": "secret123" }
Response: { "token": "eyJ...", "email": "...", "username": "...", "role": "User" }
```

1. Validate inputs not empty
2. `db.Users.FirstOrDefaultAsync(u => u.Email == email)` — find user
3. If user not found → `401 Unauthorized` (same error as wrong password — prevents user enumeration)
4. `BCrypt.Verify(request.Password, user.PasswordHash)` — verify password
5. If invalid → `401 Unauthorized`
6. `tokenService.GenerateToken(user)` → build JWT
7. Return `200 OK` with token + user info

**User enumeration attack:** Never say "email not found" vs "wrong password" — both return the same `401` so an attacker can't probe which emails are registered.

---

### 7. Configuring JWT Authentication in Program.cs

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,  // check "iss" claim
        ValidateAudience         = true,  // check "aud" claim
        ValidateLifetime         = true,  // reject expired tokens
        ValidateIssuerSigningKey = true,  // verify signature
        ValidIssuer              = "SnipUrl.AuthService",
        ValidAudience            = "SnipUrl.Clients",
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew                = TimeSpan.Zero  // no grace period on expiry
    };
});

builder.Services.AddAuthorization();
```

To protect an endpoint, chain `.RequireAuthorization()`:
```csharp
app.MapPost("/shorten", handler).RequireAuthorization();
// Client must send: Authorization: Bearer eyJ...
```

---

### 8. Middleware Order — CRITICAL

```csharp
app.UseAuthentication();  // MUST be first — reads JWT, sets HttpContext.User
app.UseAuthorization();   // MUST be second — checks if user is allowed
```

If you put `UseAuthorization` before `UseAuthentication`, the user will never be authenticated and all protected endpoints will return `401`.

---

### 9. DI Lifetimes (Singleton vs Scoped vs Transient)

| Lifetime | Created | Disposed | Use for |
|---|---|---|---|
| `AddSingleton` | Once at app start | App shutdown | Config, caches |
| `AddScoped` | Once per HTTP request | End of request | `DbContext`, services that use it |
| `AddTransient` | Every time it's injected | After each use | Lightweight stateless utilities |

`JwtTokenService` uses `AddScoped` because it depends on `IConfiguration` (which is singleton — fine to inject into scoped). `DbContext` is always scoped.

---

### 10. appsettings.json — JWT Section

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=snipurldb;Username=snipurl;Password=snipurl123"
  },
  "Jwt": {
    "SecretKey": "SnipUrl-SuperSecret-JWT-Key-AtLeast32Chars!",
    "Issuer": "SnipUrl.AuthService",
    "Audience": "SnipUrl.Clients",
    "ExpiryMinutes": "60"
  }
}
```

- `SecretKey` must be **≥ 32 characters** for HS256 (256-bit key)
- In production, move `SecretKey` to environment variables or a secrets vault — never commit it to git

---

## NuGet Packages (AuthService)

| Package | Purpose |
|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Validates JWT tokens on incoming requests |
| `System.IdentityModel.Tokens.Jwt` | Build and serialize JWT tokens (`JwtSecurityToken`) |
| `BCrypt.Net-Next` | Password hashing and verification |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core provider for PostgreSQL |
| `Microsoft.EntityFrameworkCore.Design` | Enables `dotnet ef` CLI (migrations) |
| `Scalar.AspNetCore` | Interactive API docs UI |

---

## How to Rebuild AuthService From Scratch

1. `dotnet new webapi -n AuthService --use-minimal-apis`
2. Install all 6 packages above
3. Create `Domain/Entities/User.cs` — entity with Id, Email, PasswordHash, Username, Role, CreatedAt
4. Create `Infrastructure/Persistence/AuthDbContext.cs` — DbContext with unique index on Email
5. Create `Common/JwtTokenService.cs` — inject `IConfiguration`, build claims, sign token
6. Create `EndPoints/RegisterEndpoint.cs` — validate → check duplicate → BCrypt hash → save → 201
7. Create `EndPoints/LoginEndpoint.cs` — find user → BCrypt.Verify → GenerateToken → 200
8. Update `appsettings.json` — add `Jwt` section + `ConnectionStrings`
9. Rewrite `Program.cs` — AddDbContext → AddScoped\<JwtTokenService\> → AddAuthentication/AddJwtBearer → AddAuthorization → Migrate → UseAuthentication → UseAuthorization → MapEndpoints
10. `dotnet ef migrations add InitialCreate`
11. `dotnet run` — auto-migrates, `Users` table created, ready

---

## Key Interview / Revision Points (AuthService)

- **Why BCrypt over SHA256?** BCrypt is slow by design — work factor makes brute-force impractical
- **Why is salt embedded in the BCrypt hash?** So you don't need a separate DB column; `Verify()` extracts it automatically
- **Why does login return the same 401 for wrong email AND wrong password?** Prevents user enumeration attacks
- **What are JWT Claims?** Key-value pairs in the token payload describing the user (id, email, role, expiry)
- **Why is the JWT signature important?** Proves the payload wasn't tampered with — only the server with the secret key can produce a valid signature
- **Why must `UseAuthentication()` come before `UseAuthorization()`?** Authentication populates `HttpContext.User`; authorization reads it
- **What does `ClockSkew = TimeSpan.Zero` do?** Removes the default 5-minute grace period — tokens expire exactly when `exp` says
- **What is `Jti` claim?** A unique ID per token — useful later for token blacklisting/revocation
- **Scoped vs Singleton?** DbContext = Scoped (per request). JwtTokenService = Scoped. Config = Singleton.
