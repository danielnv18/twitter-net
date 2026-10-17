using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace api.Auth;

public sealed class SessionService(AppDbContext db, TimeProvider clock)
{
    public const string Scheme = "Session";
    public const string SessionClaim = "session";
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

    public async Task<string> CreateAsync(AppUser user, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow().UtcDateTime;
        db.AuthSessions.Add(new AuthSession
        {
            Id = Hash(token), User = user, UserId = user.Id, SecurityStamp = user.SecurityStamp!,
            CreatedAtUtc = now, LastSeenAtUtc = now, ExpiresAtUtc = now.Add(AbsoluteLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var token = principal.FindFirstValue(SessionClaim);
        if (token is not null)
            await db.AuthSessions.Where(x => x.Id == Hash(token)).ExecuteDeleteAsync(cancellationToken);
    }

    public Task SignInAsync(HttpContext context, AppUser user, string token) =>
        context.SignInAsync(Scheme, Principal(user, token), new AuthenticationProperties
        {
            IsPersistent = false,
            IssuedUtc = clock.GetUtcNow(), ExpiresUtc = clock.GetUtcNow().Add(AbsoluteLifetime),
            AllowRefresh = false
        });

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var token = context.Principal?.FindFirstValue(SessionClaim);
        var now = clock.GetUtcNow().UtcDateTime;
        var idleCutoff = now.Subtract(IdleLifetime);
        var session = token is { Length: 64 }
            ? await db.AuthSessions.AsNoTracking().Include(x => x.User)
                .SingleOrDefaultAsync(x => x.Id == Hash(token), context.HttpContext.RequestAborted)
            : null;
        if (session is null || session.ExpiresAtUtc <= now || session.LastSeenAtUtc <= idleCutoff ||
            session.SecurityStamp != session.User.SecurityStamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
            return;
        }
        var updated = await db.AuthSessions
            .Where(x => x.Id == session.Id && x.ExpiresAtUtc > now && x.LastSeenAtUtc > idleCutoff &&
                x.SecurityStamp == session.User.SecurityStamp)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LastSeenAtUtc, x => x.LastSeenAtUtc < now ? now : x.LastSeenAtUtc), context.HttpContext.RequestAborted);
        if (updated == 0)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
            return;
        }
        context.HttpContext.Items["account"] = AccountResponse.From(session.User);
        context.HttpContext.Items["sessionExpiresAt"] = DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc);
        context.HttpContext.Items["sessionIdleExpiresAt"] = DateTime.SpecifyKind((session.LastSeenAtUtc > now ? session.LastSeenAtUtc : now).Add(IdleLifetime), DateTimeKind.Utc);
        context.ReplacePrincipal(Principal(session.User, token!));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static ClaimsPrincipal Principal(AppUser user, string token) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.UserName!),
        new Claim(SessionClaim, token)
    ], Scheme));
}

public sealed class SessionCleanup(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SessionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var idleCutoff = now.Subtract(SessionService.IdleLifetime);
                var ids = await db.AuthSessions.Where(x => x.ExpiresAtUtc <= now || x.LastSeenAtUtc <= idleCutoff)
                    .OrderBy(x => x.ExpiresAtUtc).Select(x => x.Id).Take(500).ToListAsync(stoppingToken);
                await db.AuthSessions.Where(x => ids.Contains(x.Id) && (x.ExpiresAtUtc <= now || x.LastSeenAtUtc <= idleCutoff))
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Expired session cleanup failed."); }
        }
    }
}
