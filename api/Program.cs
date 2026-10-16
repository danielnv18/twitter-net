using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using api.Auth;
using api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySql.Data.MySqlClient;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (builder.Configuration["Database:Server"] is { } databaseServer)
{
    connectionString = new MySqlConnectionStringBuilder
    {
        Server = databaseServer,
        Port = builder.Configuration.GetValue<uint>("Database:Port", 3306),
        Database = builder.Configuration["Database:Name"],
        UserID = builder.Configuration["Database:User"],
        Password = builder.Configuration["Database:Password"]
    }.ConnectionString;
}
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection or the Database settings.");

var allowInsecureHttp = builder.Configuration.GetValue("Auth:AllowInsecureHttp", builder.Environment.IsDevelopment());
var secureCookies = !allowInsecureHttp;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.MaxDepth = 16);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options => options.UseMySQL(connectionString)
    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.CommandError, CoreEventId.SaveChangesFailed, CoreEventId.QueryIterationFailed)));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: ["ready"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
    options.Password.RequiredLength = 15;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
builder.Services.AddSingleton<DummyPassword>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddHostedService<SessionCleanup>();
var protection = builder.Services.AddDataProtection().SetApplicationName("twitter-net-auth-v1")
    .PersistKeysToDbContext<AppDbContext>();
if (builder.Configuration["Auth:KeyCertificatePath"] is { Length: > 0 } certificatePath)
    protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
        builder.Configuration["Auth:KeyCertificatePassword"], X509KeyStorageFlags.EphemeralKeySet));
builder.Services.AddAuthentication(SessionService.Scheme).AddCookie(SessionService.Scheme, options =>
{
    options.Cookie.Name = secureCookies ? "__Host-twitter-session" : "twitter-session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = SessionService.AbsoluteLifetime;
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<SessionService>().ValidateAsync(context);
    options.Events.OnRedirectToLogin = context => Results.Problem(statusCode: 401, title: "Sign in to continue.").ExecuteAsync(context.HttpContext);
    options.Events.OnRedirectToAccessDenied = context => Results.Problem(statusCode: 403, title: "Access denied.").ExecuteAsync(context.HttpContext);
});
builder.Services.AddOptions<CookieAuthenticationOptions>(SessionService.Scheme)
    .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = secureCookies ? "__Host-twitter-csrf" : "twitter-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.None;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var address in builder.Configuration.GetSection("Auth:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(address));
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/auth") && HttpMethods.IsPost(context.Request.Method)
            ? RateLimitPartition.GetConcurrencyLimiter("auth", _ => new ConcurrencyLimiterOptions { PermitLimit = 32, QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Auth:RateLimitPermitLimit", 20),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Too many attempts. Try again shortly.",
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }).ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages(async context => await Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
    .ExecuteAsync(context.HttpContext));
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    if (context.Request.Path.StartsWithSegments("/api/auth"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (!allowInsecureHttp && !context.Request.IsHttps)
        {
            await Results.Problem(statusCode: 400, title: "HTTPS is required for authentication.",
                extensions: new Dictionary<string, object?> { ["code"] = "https_required" }).ExecuteAsync(context);
            return;
        }
        if (context.Request.ContentLength > 16 * 1024)
        {
            await Results.Problem(statusCode: 413, title: "The request is too large.").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") && HttpMethods.IsPost(context.Request.Method))
    {
        try
        {
            if (!context.Request.Headers.TryGetValue("X-CSRF-TOKEN", out var token) || token.Count != 1 || string.IsNullOrWhiteSpace(token[0]))
                throw new AntiforgeryValidationException("The CSRF header is required.");
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(statusCode: 400, title: "Refresh the session and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "csrf_invalid" }).ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapAuth();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

public partial class Program;
