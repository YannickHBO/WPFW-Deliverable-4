using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Auth;
using PortfolioApi.Data;
using PortfolioApi.Models;
using PortfolioApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Data: EF Core DbContext against SQL Server (scoped: one instance per HTTP request).
builder.Services.AddDbContext<PortfolioDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PortfolioDb")));

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
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanEditContent, policy => policy.RequireRole(RoleNames.Editor, RoleNames.Admin));

// Application logic: services injected into the controllers via constructor injection.
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IBlogPostService, BlogPostService>();

// HTTP layer + Swagger (Swashbuckle.AspNetCore).
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply pending EF Core migrations on startup, so the database and tables exist.
using (var scope = app.Services.CreateScope())
{
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
