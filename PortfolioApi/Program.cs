using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Data;
using PortfolioApi.GitHub;
using PortfolioApi.Models;
using PortfolioApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Data: EF Core DbContext against SQL Server (scoped: one instance per HTTP request).
// EnableRetryOnFailure: Azure SQL can briefly be unavailable (maintenance, failover, throttling);
// EF Core then retries the query a few times instead of failing the request straight away.
builder.Services.AddDbContext<PortfolioDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PortfolioDb"),
        sqlServer => sqlServer.EnableRetryOnFailure()));

// Identity: user accounts and roles stored in PortfolioDbContext. AddIdentityCore (not AddIdentity)
// adds no UI-oriented defaults; AddSignInManager adds password sign-in and the security stamp check.
builder.Services.AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Brute-force protection: 3 wrong passwords in a row lock the account for 5 minutes.
        options.Lockout.MaxFailedAccessAttempts = 3;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<PortfolioDbContext>()
    .AddSignInManager();

// Authentication: Identity's cookie is the only way to sign in.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "PortfolioAuth";
    options.Cookie.HttpOnly = true;                           // JavaScript can't read it (XSS can't steal it).
    options.Cookie.SameSite = SameSiteMode.Lax;               // Not sent with cross-site POST/PUT/DELETE (CSRF).
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only (browsers also accept it on http://localhost).
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // This is an API: answer with a status code instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Check the cookie's security stamp against the database on every request. A role change updates
// the stamp, so old cookies stop working immediately and the user has to sign in again.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// Authorization: who may write. Admin is named explicitly; it doesn't inherit Editor's rights.
// User management is for Admins only.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanEditContent, policy => policy.RequireRole(RoleNames.Editor, RoleNames.Admin))
    .AddPolicy(Policies.IsAdmin, policy => policy.RequireRole(RoleNames.Admin));

// Application logic: services injected into the controllers via constructor injection.
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IBlogPostService, BlogPostService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

// Commit sync: commits on GitHub become blog posts (background service + POST /api/admin/sync-commits).
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
builder.Services.AddHttpClient<IGitHubCommitClient, GitHubCommitClient>(client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PortfolioApi");        // GitHub refuses requests without one.
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    client.Timeout = TimeSpan.FromSeconds(15);                              // Don't wait long for a slow GitHub.
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CommitSyncState>();
builder.Services.AddScoped<ICommitSyncService, CommitSyncService>();
builder.Services.AddHostedService<CommitSyncBackgroundService>();

// HTTP layer + Swagger (Swashbuckle.AspNetCore). Enums as text in JSON ("Synced" instead of 0).
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply pending EF Core migrations on startup, so the database and tables exist. On by default;
// the tests switch it off (Database:MigrateOnStartup = false): the migrations are written for
// SQL Server, and the tests create their own SQLite database instead.
if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Order matters: first find out who is calling (cookie), then check what they may do.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
