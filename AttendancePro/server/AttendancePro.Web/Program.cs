using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
if (cfg.GetValue<double?>("Demo:ClockOffsetHours") is { } demoOffset && demoOffset != 0)
{
    Clock.Offset = TimeSpan.FromHours(demoOffset); // staging aid for demo data/screenshots ONLY
    Console.WriteLine($"WARNING: demo clock offset of {demoOffset} h is active. Never use this in production.");
}

// ---------------------------------------------------------------- data
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(cfg.GetConnectionString("Default") ?? "Data Source=App_Data/attendance.db"));

// ---------------------------------------------------------------- identity (cookie for the admin portal)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Password.RequireDigit = true;
    o.Password.RequireLowercase = true;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    o.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, OrgClaimsFactory>();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Denied";
    o.Cookie.Name = "ap.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(10);
    o.SlidingExpiration = true;
    // API calls must get a 401, never a redirect to the login page.
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 401; return Task.CompletedTask; }
        ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask;
    };
});

// ---------------------------------------------------------------- JWT (for the mobile app)
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Key.Length < 32 || (jwt.Key.StartsWith("CHANGE-ME") && !builder.Environment.IsDevelopment()))
    throw new InvalidOperationException("Set Jwt:Key to a random secret of 32+ characters (env var Jwt__Key).");
builder.Services.AddAuthentication().AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization(o =>
    o.AddPolicy("Admin", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireRole("Admin")));

// ---------------------------------------------------------------- app services
builder.Services.Configure<SmtpOptions>(cfg.GetSection("Smtp"));
builder.Services.AddScoped<IMailSender, SmtpMailSender>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<MonthlyReports>();
builder.Services.AddSingleton<SelfieStore>();
builder.Services.AddHostedService<MonthlyReportService>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear(); // trust the reverse proxy in front (IIS/nginx/Caddy)
});
builder.Services.AddHealthChecks();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    await Seeder.InitializeAsync(scope.ServiceProvider, cfg, app.Environment);
    if (cfg.GetValue<bool>("Seed:SampleData")) await DemoData.SeedAsync(scope.ServiceProvider);
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: https://tile.openstreetmap.org https://*.tile.openstreetmap.org; " +
                                   "style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' https://nominatim.openstreetmap.org; frame-ancestors 'none'";
    await next();
});
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();

/// <summary>Adds the tenant id to every signed-in principal (cookie sessions).</summary>
public class OrgClaimsFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public OrgClaimsFactory(UserManager<ApplicationUser> um, RoleManager<IdentityRole> rm, IOptions<IdentityOptions> o) : base(um, rm, o) { }
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(TenantProvider.OrgClaim, user.OrganizationId.ToString()));
        id.AddClaim(new Claim(ClaimTypes.Name, user.FullName)); // display name instead of email
        return id;
    }
}
