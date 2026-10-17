using System.Net.Mail;
using System.Text.RegularExpressions;
using api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace api.Auth;

public static partial class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");
        group.MapGet("/session", (HttpContext context, IAntiforgery csrf, TimeProvider clock) =>
        {
            return TypedResults.Ok(new SessionResponse(context.Items["account"] as AccountResponse,
                csrf.GetAndStoreTokens(context).RequestToken!,
                context.Items["sessionExpiresAt"] as DateTime?, context.Items["sessionIdleExpiresAt"] as DateTime?, clock.GetUtcNow().UtcDateTime));
        }).WithSummary("Get the current session and a CSRF token");
        group.MapGet("/me", (HttpContext context) =>
            TypedResults.Ok((AccountResponse)context.Items["account"]!))
            .RequireAuthorization().WithSummary("Get the signed-in account");
        group.MapPost("/signup", SignupAsync).RequireRateLimiting("auth")
            .WithSummary("Create an account and sign in");
        group.MapPost("/login", LoginAsync).RequireRateLimiting("auth")
            .WithSummary("Sign in with username or email");
        group.MapPost("/logout", async (HttpContext context, SessionService sessions, CancellationToken cancellationToken) =>
        {
            await sessions.RevokeAsync(context.User, cancellationToken);
            await context.SignOutAsync(SessionService.Scheme);
            return TypedResults.NoContent();
        }).RequireRateLimiting("auth").WithSummary("Revoke the current session");
    }

    private static async Task<IResult> SignupAsync(SignupRequest request, HttpContext context, AppDbContext db,
        UserManager<AppUser> users, SessionService sessions, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? "";
        var username = request.Username?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
            errors["name"] = ["Enter a name between 1 and 100 characters without control characters."];
        if (!UsernamePattern().IsMatch(username))
            errors["username"] = ["Use 3 to 30 letters, numbers, or underscores."];
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            errors["email"] = ["Enter a valid email address of at most 254 characters."];
        if (request.Password is not { Length: >= 15 and <= 128 })
            errors["password"] = ["Use between 15 and 128 characters."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new AppUser { Id = Guid.NewGuid(), Name = name, UserName = username, Email = email };
            var result = await users.CreateAsync(user, request.Password!);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(x => x.Code is "DuplicateUserName" or "DuplicateEmail")) return Conflict();
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["The account details are invalid."] });
            }
            await sessions.RevokeAsync(context.User, cancellationToken);
            var token = await sessions.CreateAsync(user, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await sessions.SignInAsync(context, user, token);
            return TypedResults.Created("/api/auth/me", AccountResponse.From(user));
        }
        catch (DbUpdateException exception) when (IsAccountConflict(exception)) { return Conflict(); }
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, AppDbContext db,
        UserManager<AppUser> users, SessionService sessions, DummyPassword dummy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier) || request.Identifier.Length > 254 ||
            request.Password is not { Length: >= 1 and <= 128 })
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["identifier"] = ["Enter your username or email and password."] });

        var identifier = request.Identifier.Trim();
        var normalized = identifier.Contains('@') ? users.NormalizeEmail(identifier) : users.NormalizeName(identifier);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the account row so simultaneous password failures cannot lose lockout increments.
        var matches = identifier.Contains('@')
            ? await db.Users.FromSqlInterpolated($"SELECT * FROM `AspNetUsers` WHERE `NormalizedEmail` = {normalized} FOR UPDATE").ToListAsync(cancellationToken)
            : await db.Users.FromSqlInterpolated($"SELECT * FROM `AspNetUsers` WHERE `NormalizedUserName` = {normalized} FOR UPDATE").ToListAsync(cancellationToken);
        var user = matches.SingleOrDefault();
        if (user is null || await users.IsLockedOutAsync(user))
        {
            dummy.Verify(request.Password);
            return InvalidLogin();
        }
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            await transaction.CommitAsync(cancellationToken);
            return InvalidLogin();
        }
        await users.ResetAccessFailedCountAsync(user);
        await sessions.RevokeAsync(context.User, cancellationToken);
        var token = await sessions.CreateAsync(user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await sessions.SignInAsync(context, user, token);
        return TypedResults.Ok(AccountResponse.From(user));
    }

    private static IResult Conflict() => TypedResults.Problem(statusCode: 409,
        title: "An account with those details already exists.", extensions: new Dictionary<string, object?> { ["code"] = "account_conflict" });
    private static IResult InvalidLogin() => TypedResults.Problem(statusCode: 401,
        title: "The username, email, or password is incorrect.", extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });
    private static bool IsAccountConflict(DbUpdateException exception) => exception.InnerException is MySqlException { Number: 1062 } mysql &&
        (mysql.Message.Contains("UserNameIndex", StringComparison.Ordinal) || mysql.Message.Contains("EmailIndex", StringComparison.Ordinal));

    [GeneratedRegex("\\A[A-Za-z0-9_]{3,30}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}

public sealed class DummyPassword
{
    private readonly AppUser user = new() { Name = "" };
    private readonly PasswordHasher<AppUser> hasher;
    private readonly string hash;

    public DummyPassword(Microsoft.Extensions.Options.IOptions<PasswordHasherOptions> options)
    {
        hasher = new PasswordHasher<AppUser>(options);
        hash = hasher.HashPassword(user, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    }

    public void Verify(string password) => hasher.VerifyHashedPassword(user, hash, password);
}
