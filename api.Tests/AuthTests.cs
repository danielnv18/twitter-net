using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace api.Tests;

public sealed class AuthTests
{
    private const string Password = "a very long passphrase";
    private static object Signup(string username = "ada_lovelace", string email = "ada@example.test") =>
        new { name = "  Ada Lovelace  ", username, email, password = Password };

    private static async Task<string> Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? csrf = null)
    {
        csrf ??= await Csrf(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Signup_TrimsAndNormalizesAccount_AndAuthorizesMeWithoutEmail()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var response = await Post(client, "signup", Signup(" Ada_Lovelace ", " Ada@Example.test "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/auth/me", response.Headers.Location?.ToString());
        var account = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ada Lovelace", account.GetProperty("name").GetString());
        Assert.Equal("Ada_Lovelace", account.GetProperty("username").GetString());
        Assert.False(account.TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await app.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync();
            Assert.Equal("ADA_LOVELACE", user.NormalizedUserName);
            Assert.Equal("ADA@EXAMPLE.TEST", user.NormalizedEmail);
            Assert.NotEqual(Password, user.PasswordHash);
            var session = await db.AuthSessions.SingleAsync();
            Assert.Equal(64, session.Id.Length);
            var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("twitter-session=")).Split(';')[0].Split('=', 2)[1];
            var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SessionService.Scheme);
            var raw = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie))!.Principal.FindFirstValue(SessionService.SessionClaim)!;
            Assert.NotEqual(raw, session.Id);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), session.Id);
        });
    }

    [Theory]
    [InlineData("signup")]
    [InlineData("login")]
    [InlineData("logout")]
    public async Task EveryMutation_RejectsMissingOrInvalidCsrf(string endpoint)
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        foreach (var token in new string?[] { null, "invalid" })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + endpoint) { Content = JsonContent.Create(Signup()) };
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("csrf_invalid", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        await app.WithDbAsync(async db => Assert.Empty(await db.Users.ToListAsync()));
    }

    [Fact]
    public async Task Logout_RequiresCsrfHeader_EvenWhenAValidFormTokenIsPresent()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        var token = await Csrf(client);
        var response = await client.PostAsync("/api/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task CsrfTokenFromAnonymousIdentity_IsRejectedAfterSignup()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var token = await Csrf(client);
        Assert.Equal(HttpStatusCode.Created, (await Post(client, "signup", Signup(), token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "logout", new { }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("control-name")]
    [InlineData("empty-name")]
    [InlineData("short-username")]
    [InlineData("punctuation-username")]
    [InlineData("unicode-username")]
    [InlineData("invalid-email")]
    [InlineData("short-password")]
    public async Task Signup_InvalidFields_ReturnCamelCaseValidation(string caseName)
    {
        var (field, value) = caseName switch
        {
            "control-name" => ("name", "\n"),
            "empty-name" => ("name", ""),
            "short-username" => ("username", "ad"),
            "punctuation-username" => ("username", "ada-name"),
            "unicode-username" => ("username", "ádalovelace"),
            "invalid-email" => ("email", "invalid"),
            "short-password" => ("password", "fourteen_chars"),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var body = new Dictionary<string, string> { ["name"] = "Ada", ["username"] = "ada", ["email"] = "ada@example.test", ["password"] = Password };
        body[field] = value;
        var response = await Post(client, "signup", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Signup_EnforcesUpperBounds_WithoutTrimmingPassword()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var response = await Post(client, "signup", new { name = new string('n', 101), username = new string('u', 31), email = new string('a', 255), password = new string('p', 129) });
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(4, errors.EnumerateObject().Count());
        var password = "              x";
        Assert.Equal(HttpStatusCode.Created, (await Post(client, "signup", new { name = "N", username = "abc", email = "n@example.test", password })).StatusCode);
        await Post(client, "logout", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new { identifier = "abc", password = password.Trim() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "login", new { identifier = "abc", password })).StatusCode);
    }

    [Theory]
    [InlineData("ADA_LOVELACE", "other@example.test")]
    [InlineData("other_user", "ADA@EXAMPLE.TEST")]
    public async Task Signup_NormalizedUsernameAndEmailConflicts_AreGeneric(string username, string email)
    {
        await using var app = new AuthApplication();
        using var one = await app.BrowserAsync();
        using var two = await app.BrowserAsync();
        Assert.Equal(HttpStatusCode.Created, (await Post(one, "signup", Signup())).StatusCode);
        var response = await Post(two, "signup", Signup(username, email));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account_conflict", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await app.WithDbAsync(async db => Assert.Single(await db.Users.ToListAsync()));
    }

    [Fact]
    public async Task ConcurrentSignup_AllowsExactlyOneNormalizedEmail()
    {
        await using var app = new AuthApplication();
        using var one = await app.BrowserAsync();
        using var two = await app.BrowserAsync();
        var tokenOne = await Csrf(one);
        var tokenTwo = await Csrf(two);
        var responses = await Task.WhenAll(Post(one, "signup", Signup("user_one"), tokenOne), Post(two, "signup", Signup("user_two", "ADA@EXAMPLE.TEST"), tokenTwo));
        Assert.Equal(new[] { 201, 409 }, responses.Select(x => (int)x.StatusCode).Order().ToArray());
        await app.WithDbAsync(async db => { Assert.Single(await db.Users.ToListAsync()); Assert.Single(await db.AuthSessions.ToListAsync()); });
    }

    [Fact]
    public async Task Logout_RevokesCookieReplay_AndCanRepeatWithFreshCsrf()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var signup = await Post(client, "signup", Signup());
        var cookie = signup.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("twitter-session=")).Split(';')[0];
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        using var replay = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { })).StatusCode);
        await app.WithDbAsync(async db => Assert.Empty(await db.AuthSessions.ToListAsync()));
    }

    [Theory]
    [InlineData(" ADA_LOVELACE ")]
    [InlineData(" ADA@EXAMPLE.TEST ")]
    public async Task Login_AcceptsNormalizedUsernameOrEmail_AndRotatesSession(string identifier)
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        string? oldId = null;
        await app.WithDbAsync(async db => oldId = (await db.AuthSessions.SingleAsync()).Id);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "login", new { identifier, password = Password })).StatusCode);
        await app.WithDbAsync(async db => Assert.NotEqual(oldId, (await db.AuthSessions.SingleAsync()).Id));
    }

    [Fact]
    public async Task Login_UnknownWrongAndLockedAccounts_ReturnSameGeneric401()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        await Post(client, "logout", new { });
        var unknown = await Post(client, "login", new { identifier = "unknown", password = Password });
        var expected = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await Post(client, "login", new { identifier = "ada_lovelace", password = "a different passphrase" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expected.GetProperty("title").GetString(), wrongBody.GetProperty("title").GetString());
            Assert.Equal(expected.GetProperty("code").GetString(), wrongBody.GetProperty("code").GetString());
        }
        var locked = await Post(client, "login", new { identifier = "ada_lovelace", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        var lockedBody = await locked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected.GetProperty("title").GetString(), lockedBody.GetProperty("title").GetString());
        Assert.Equal(expected.GetProperty("code").GetString(), lockedBody.GetProperty("code").GetString());
        await app.WithDbAsync(async db => Assert.True((await db.Users.SingleAsync()).LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14)));
    }

    [Fact]
    public async Task Session_IdleExpiryRejectsAtBoundary_AndDeadlineIsPublic()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("Ada Lovelace", session.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(app.Clock.GetUtcNow().UtcDateTime.AddMinutes(30), session.GetProperty("idleExpiresAtUtc").GetDateTime());
        app.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Session_ActivityExtendsIdle_ButNeverAbsoluteLifetime()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        for (var i = 0; i < 16; i++)
        {
            app.Clock.Advance(TimeSpan.FromMinutes(29));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Session_SecurityStampChangeRevokesExistingCookie()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByNameAsync("ada_lovelace"))!);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AnonymousSession_IsNoStore_AndMeReturnsJson401()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var response = await client.GetAsync("/api/auth/session");
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(JsonValueKind.Null, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").ValueKind);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal("application/problem+json", me.Content.Headers.ContentType?.MediaType);
        Assert.Null(me.Headers.Location);
    }

    [Fact]
    public async Task ProductionCookies_AreHostSecureHttpOnlyLax_AndKeysSurviveNewHost()
    {
        await using var app = new AuthApplication(secure: true);
        using var client = await app.BrowserAsync(https: true);
        var session = await client.GetAsync("/api/auth/session");
        var csrf = session.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-twitter-csrf=", csrf);
        Assert.Contains("secure", csrf); Assert.Contains("httponly", csrf); Assert.Contains("samesite=lax", csrf); Assert.Contains("path=/", csrf);
        var response = await Post(client, "signup", Signup());
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-twitter-session="));
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=lax", cookie); Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie); Assert.DoesNotContain("expires=", cookie);
        await using var second = new AuthApplication(secure: true, databaseName: app.DatabaseName);
        using var replay = await second.BrowserAsync(https: true);
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/me")).StatusCode);
        await app.WithDbAsync(async db => Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync()));
    }

    [Fact]
    public async Task ProductionHttp_RejectsSpoofedForwardedProto_WithoutTrustedProxy()
    {
        await using var app = new AuthApplication(secure: true);
        using var client = await app.BrowserAsync();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.2");
        var response = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }


    [Fact]
    public async Task DatabaseFailure_Returns503_WithoutRevokingValidSession()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        await Post(client, "signup", Signup());
        app.DatabaseFailure.Enabled = true;
        var failure = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Equal("service_unavailable", (await failure.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(failure.Headers.TryGetValues("Set-Cookie", out _));
        app.DatabaseFailure.Enabled = false;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task OversizedBody_IsRejectedBeforeCreatingAccount()
    {
        await using var app = new AuthApplication();
        using var client = await app.BrowserAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { name = new string('a', 17000) }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await app.WithDbAsync(async db => Assert.Empty(await db.Users.ToListAsync()));
    }

    [Fact]
    public async Task Mutations_ThrottleWithRetryAfter_DespiteSpoofedClientIp()
    {
        await using var app = new AuthApplication(permits: 2);
        using var client = await app.BrowserAsync();
        var token = await Csrf(client);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new { identifier = "unknown", password = Password }, token)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.5");
        var limited = await Post(client, "logout", new { }, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter?.ToString());
    }
}
