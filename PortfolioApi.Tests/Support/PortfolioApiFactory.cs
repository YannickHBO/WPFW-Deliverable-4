using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PortfolioApi.Auth;
using PortfolioApi.Data;
using PortfolioApi.GitHub;
using PortfolioApi.Models;
using System.Net.Http.Json;

namespace PortfolioApi.Tests.Support;

/// <summary>
/// Starts the real API in memory (same Program.cs, controllers, Identity, cookie and policies) for
/// HTTP tests. Only the outside world is replaced:
/// <list type="bullet">
/// <item>SQL Server → a SQLite database in memory, created from the model with the seed data. Every
/// factory has its own, so test data never touches PortfolioDb and test classes can't see each other's data.</item>
/// <item>GitHub → <see cref="FakeGitHubCommitClient"/>; the background sync is switched off.</item>
/// <item>The cookie encryption keys → temporary keys that only exist during the test run.</item>
/// </list>
/// </summary>
public class PortfolioApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Password for every test user. Only exists in this in-memory test database.</summary>
    public const string TestPassword = "Test-Wachtwoord1";

    // An in-memory SQLite database lives as long as its connection is open, so the factory keeps one open.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeGitHubCommitClient GitHub { get; } = new();

    public PortfolioApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": no user-secrets and no Swagger, only what the API itself needs.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("GitHub:SyncEnabled", "false");

        builder.ConfigureTestServices(services =>
        {
            // Replace the SQL Server registration (both its options and the configuration that builds them).
            services.RemoveAll<DbContextOptions<PortfolioDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PortfolioDbContext>>();
            services.AddDbContext<PortfolioDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IGitHubCommitClient>();
            services.AddSingleton<IGitHubCommitClient>(GitHub);

            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Tables, roles and seed data (projects, blog posts) from the model, like the migrations would.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    /// <summary>
    /// A client like a browser: it keeps cookies and talks HTTPS (the authentication cookie is Secure,
    /// so it is only sent over HTTPS). Redirects are not followed, so a redirect would show up as one.
    /// </summary>
    public HttpClient CreateBrowserClient(bool handleCookies = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = handleCookies
    });

    /// <summary>
    /// A new account with the role Gast (like registration) plus the given extra roles (like the SQL
    /// script). Every call makes a different e-mail address.
    /// </summary>
    public async Task<TestUser> CreateUserAsync(params string[] extraRoles)
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = email, Email = email };

        var created = await userManager.CreateAsync(user, TestPassword);
        if (!created.Succeeded) throw new InvalidOperationException("Test user could not be created.");
        await userManager.AddToRolesAsync(user, [RoleNames.Guest, .. extraRoles]);

        return new TestUser(user.Id, email, TestPassword);
    }

    /// <summary>
    /// A browser client for the given role: null = not signed in; Gast = a registered user;
    /// Editor/Admin = a user who also got that role. Signs in via POST /api/auth/login.
    /// </summary>
    public async Task<HttpClient> CreateClientAsync(string? role)
    {
        var client = CreateBrowserClient();
        if (role is null) return client;

        var user = role == RoleNames.Guest ? await CreateUserAsync() : await CreateUserAsync(role);
        await SignInAsync(client, user);
        return client;
    }

    public static async Task SignInAsync(HttpClient client, TestUser user)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Runs code against the test database in its own scope: to arrange data or to check the result.</summary>
    public async Task<T> WithDbAsync<T>(Func<PortfolioDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<PortfolioDbContext>());
    }

    /// <summary>Runs code with a UserManager, e.g. to check roles or change a security stamp.</summary>
    public async Task<T> WithUserManagerAsync<T>(Func<UserManager<User>, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<User>>());
    }
}

/// <summary>A test account; the password only exists in the in-memory test database.</summary>
public record TestUser(string Id, string Email, string Password);
