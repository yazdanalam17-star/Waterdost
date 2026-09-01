using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WaterApp.Application.Interfaces;
using WaterApp.Infrastructure.Data;
using WaterApp.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Database ----
// Railway injects DATABASE_URL in URI form (postgresql://user:pass@host:port/db).
// Npgsql needs keyword=value form (Host=...;Username=...;Password=...;Database=...),
// so convert it if a URI-style value is present. Falls back to appsettings locally.
var rawConnectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

var connectionString = ConvertToNpgsqlConnectionString(rawConnectionString);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

static string? ConvertToNpgsqlConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
        return raw;

    // Already in keyword=value format (e.g. "Host=...;Username=...").
    if (!raw.Contains("://"))
        return raw;

    var uri = new Uri(raw);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port == -1 ? 5432 : uri.Port;

    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}

// ---- DI ----
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<ISellerService, SellerService>();
builder.Services.AddScoped<IBuyerService, BuyerService>();
builder.Services.AddScoped<IAccountService, AccountService>();
// Typed HttpClient: this alone registers NotificationService for
// INotificationService (as well as wiring up the HttpClient it takes in
// its constructor) — no separate AddScoped needed.
builder.Services.AddHttpClient<INotificationService, NotificationService>();

// SMS sender for the forgot-password OTP flow. Only wired to a real
// provider (Twilio) when it's actually configured; otherwise falls back to
// logging the code, so local/dev environments never need real credentials
// and a missing config doesn't 500 the forgot-password endpoint.
var smsConfigured = !string.IsNullOrEmpty(builder.Configuration["Sms:AccountSid"])
    && !string.IsNullOrEmpty(builder.Configuration["Sms:AuthToken"])
    && !string.IsNullOrEmpty(builder.Configuration["Sms:FromNumber"]);

if (smsConfigured)
{
    builder.Services.AddHttpClient<ISmsSender, TwilioSmsSender>();
}
else
{
    builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();
}

// ---- JWT Auth ----
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

builder.Services.AddAuthorization();

// ---- Controllers / Swagger ----
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- CORS (open for now; restrict to your app's origin in production) ----
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .WithOrigins(
                "https://waterdost.qmsofts.com",
                "http://localhost:8081", // Expo web dev server
                "http://localhost:19006" // legacy Expo web dev port
            )
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Railway (like most PaaS hosts) terminates TLS at its own edge and
// forwards requests to this app over plain HTTP with X-Forwarded-* headers
// describing the real client. Without this, HttpContext.Connection
// .RemoteIpAddress is just Railway's internal proxy IP for every request —
// which would make the rate limiter below key everything off one shared
// address instead of the actual caller.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Railway's edge isn't a fixed, listable proxy IP, so trust the
    // forwarded header unconditionally rather than restricting to a known
    // proxy allowlist — the standard approach for platforms like this
    // (Railway/Render/Heroku) where the app is only ever reached through
    // the platform's own edge, never directly.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- Rate limiting ----
// A generous global baseline on every request (mainly a backstop against
// runaway retry loops or scraping — not meant to affect normal use), plus
// a much stricter "auth" policy layered on top of it for the endpoints
// that matter most for brute-force/credential-stuffing/spam: see
// [EnableRateLimiting("auth")] on AuthController. Both policies are
// evaluated together for auth requests; a request must pass both.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"message":"Too many requests. Please wait a moment and try again."}""",
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

static string GetClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

var app = builder.Build();

// Swagger is normally dev-only. Set ENABLE_SWAGGER=true in Railway to turn it on
// in production temporarily (e.g. to seed data via Try It Out), then remove the
// env var afterward to lock it back down.
var swaggerEnabled = app.Environment.IsDevelopment()
    || Environment.GetEnvironmentVariable("ENABLE_SWAGGER") == "true";

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Manual schema patch: columns added to entities after the schema was created
// via EnsureCreated() (not migrations). Each uses ADD COLUMN IF NOT EXISTS so
// it's safe to run on every startup and on databases that already have it.
// Remove this block once proper EF Core migrations are set up.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "Sellers" ADD COLUMN IF NOT EXISTS "UpiId" text;
        ALTER TABLE "Sellers" ADD COLUMN IF NOT EXISTS "Category" text NOT NULL DEFAULT 'Water';
        ALTER TABLE "Products" ADD COLUMN IF NOT EXISTS "Category" text NOT NULL DEFAULT 'Water';
        CREATE TABLE IF NOT EXISTS "PushTokens" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "Token" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PushTokens_Token" ON "PushTokens" ("Token");
        CREATE INDEX IF NOT EXISTS "IX_PushTokens_UserId" ON "PushTokens" ("UserId");
        CREATE TABLE IF NOT EXISTS "PasswordResetOtps" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "CodeHash" text NOT NULL,
            "Attempts" integer NOT NULL DEFAULT 0,
            "ExpiresAt" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS "IX_PasswordResetOtps_UserId_CreatedAt" ON "PasswordResetOtps" ("UserId", "CreatedAt");
        CREATE TABLE IF NOT EXISTS "ProductImages" (
            "ProductId" uuid NOT NULL PRIMARY KEY REFERENCES "Products"("Id") ON DELETE CASCADE,
            "Data" bytea NOT NULL,
            "ContentType" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS "RefreshTokens" (
            "Id" uuid NOT NULL PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
            "TokenHash" text NOT NULL,
            "ExpiresAt" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "RevokedAt" timestamp with time zone
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");
        CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
        """);
}

// Applies pending EF Core migrations on startup (safe no-op if already up to date).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
