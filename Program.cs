using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
// Database provider is chosen in appsettings.json: "Database:Provider" = "Sqlite" (default, file nexus.db) or "SqlServer".
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connStr = builder.Configuration["Database:ConnectionString"];
builder.Services.AddDbContext<AppDb>(o =>
{
    if (provider == "SqlServer") o.UseSqlServer(connStr);
    else o.UseSqlite(connStr ?? "Data Source=nexus.db");
});
builder.Services.AddScoped<IdService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied";
        // On every request make sure the account behind the login cookie still exists (and is active).
        // Without this a cookie that outlived a database reset or a deleted/deactivated account would
        // "work" for browsing but silently fail to save anything.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var uname = ctx.Principal.FindFirst("uname")?.Value;
            int.TryParse(ctx.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id);
            bool ok = !string.IsNullOrEmpty(uname) && (ctx.Principal.IsInRole("Customer")
                ? await db.Customers.AnyAsync(c => c.Id == id && c.Username == uname)
                : await db.Employees.AnyAsync(e => e.Id == id && e.Username == uname && e.Active));
            if (!ok) { ctx.RejectPrincipal(); await ctx.HttpContext.SignOutAsync(); }
        };
    });

// "dotnet run -- --script <folder>" writes the CREATE TABLE scripts for SQLite and SQL Server, then exits.
var scriptIdx = Array.IndexOf(args, "--script");
if (scriptIdx >= 0)
{
    var dir = args.Length > scriptIdx + 1 ? args[scriptIdx + 1] : ".";
    Directory.CreateDirectory(dir);
    using (var lite = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite("Data Source=:memory:").Options))
        File.WriteAllText(Path.Combine(dir, "nexus_schema_sqlite.sql"), lite.Database.GenerateCreateScript());
    using (var sql = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlServer("Server=.;Database=Nexus;Trusted_Connection=True;TrustServerCertificate=True").Options))
        File.WriteAllText(Path.Combine(dir, "nexus_schema_sqlserver.sql"), sql.Database.GenerateCreateScript());
    Console.WriteLine("Schema scripts written to " + Path.GetFullPath(dir));
    return;
}

var app = builder.Build();
using (var scope = app.Services.CreateScope()) Seed.Run(scope.ServiceProvider.GetRequiredService<AppDb>());

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
