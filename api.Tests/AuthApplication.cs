using System.Data.Common;
using api.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySql.Data.MySqlClient;

namespace api.Tests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now = now.Add(amount);
}

public sealed class AuthApplication : WebApplicationFactory<Program>
{
    public readonly TestClock Clock = new();
    public readonly DatabaseFailure DatabaseFailure = new();
    public string DatabaseName { get; }
    private readonly string connectionString;
    private readonly bool ownsDatabase;
    private readonly bool secure;
    private readonly int permits;
    private readonly string? trustedProxy;

    public AuthApplication(bool secure = false, int permits = 1000, string? databaseName = null, string? trustedProxy = null)
    {
        this.secure = secure;
        this.permits = permits;
        this.trustedProxy = trustedProxy;
        ownsDatabase = databaseName is null;
        DatabaseName = databaseName ?? "auth_test_" + Guid.NewGuid().ToString("N");
        var connection = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_MYSQL") ??
            "Server=127.0.0.1;Port=3308;User=root;Password=auth_test_only;") { Database = DatabaseName };
        connectionString = connection.ConnectionString;
        if (ownsDatabase)
        {
            connection.Database = "";
            using var db = new MySqlConnection(connection.ConnectionString);
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin";
            command.ExecuteNonQuery();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(secure ? "Production" : "Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        if (!secure) builder.UseSetting("Auth:AllowInsecureHttp", "true");
        builder.UseSetting("Auth:RateLimitPermitLimit", permits.ToString());
        if (trustedProxy is not null) builder.UseSetting("Auth:KnownProxies:0", trustedProxy);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(DatabaseFailure));
        });
    }

    public async Task<HttpClient> BrowserAsync(bool https = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"), AllowAutoRedirect = false
        });
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        return client;
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!ownsDatabase) return;
        var connection = new MySqlConnectionStringBuilder(connectionString) { Database = "" };
        await using var db = new MySqlConnection(connection.ConnectionString);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = $"DROP DATABASE `{DatabaseName}`";
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class DatabaseFailure : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled) throw new UnavailableDatabaseException();
        return ValueTask.FromResult(result);
    }
}

public sealed class UnavailableDatabaseException : DbException;
