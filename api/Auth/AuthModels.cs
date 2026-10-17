using Microsoft.AspNetCore.Identity;

namespace api.Auth;

public sealed class AppUser : IdentityUser<Guid>
{
    public required string Name { get; set; }
}

public sealed class AuthSession
{
    public required string Id { get; set; }
    public Guid UserId { get; set; }
    public required AppUser User { get; set; }
    public required string SecurityStamp { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

/// <summary>Public account details returned to the browser.</summary>
public sealed record AccountResponse(string Id, string Name, string Username)
{
    public static AccountResponse From(AppUser user) => new(user.Id.ToString(), user.Name, user.UserName!);
}

/// <summary>Account details and a token for the next authentication mutation.</summary>
public sealed record SessionResponse(AccountResponse? User, string CsrfToken, DateTime? ExpiresAtUtc, DateTime? IdleExpiresAtUtc, DateTime ServerNowUtc);

/// <summary>Details required to create an account.</summary>
public sealed record SignupRequest(string? Name, string? Username, string? Email, string? Password);

/// <summary>Username or email and password required to sign in.</summary>
public sealed record LoginRequest(string? Identifier, string? Password);
