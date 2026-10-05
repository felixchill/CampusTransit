using CampusTransit.Components;
using CampusTransit.Data;
using CampusTransit.Endpoints;
using CampusTransit.Hubs;
using CampusTransit.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite locally; PostgreSQL when a hosting platform injects a DATABASE_URL. Hosts with an
// ephemeral filesystem cannot keep a SQLite file, so the provider follows the connection string.
var connectionString = DatabaseSetup.Resolve(builder.Configuration);
var usePostgres = DatabaseSetup.IsPostgres(connectionString);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (usePostgres)
    {
        options.UseNpgsql(DatabaseSetup.NormalizePostgres(connectionString));
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Data access, identity primitives and application services.
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<TransitPresence>();
builder.Services.AddScoped<TransitService>();
builder.Services.AddScoped<UserContext>();
builder.Services.AddHostedService<ShuttleSimulator>();

// Real-time transport for live shuttle telemetry.
builder.Services.AddSignalR();

// Cookie authentication with role claims for the four user groups.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "campustransit.auth";
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(10);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Hosting platforms terminate TLS and forward the original scheme in a header, so the
// app must trust that header for HTTPS redirection and cookie handling to behave.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Logged before touching the database so an unreachable host is still diagnosable.
app.Logger.LogInformation(
    "Database provider: {Provider} ({Target})",
    usePostgres ? "PostgreSQL" : "SQLite",
    DatabaseSetup.Describe(connectionString));

// Create the schema and load demonstration data on first run.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

    await DbSeeder.SeedAsync(db, hasher);

    // Write-ahead logging is a SQLite-only tuning knob.
    if (db.Database.IsSqlite())
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Serve physical files as well as the fingerprinted static-asset endpoints.
// MapStaticAssets only knows about assets present in its build manifest; if a
// framework asset such as _framework/blazor.web.js is missing from that manifest
// the app would 404 it and Blazor would never boot. UseStaticFiles covers that gap.
app.UseStaticFiles();
app.MapStaticAssets();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "campustransit" }));
app.MapTransitApi();
app.MapHub<TransitHub>("/hubs/transit");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
