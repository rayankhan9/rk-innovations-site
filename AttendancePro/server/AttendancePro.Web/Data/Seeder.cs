using System.Security.Cryptography;
using AttendancePro.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Data;

public static class Seeder
{
    public static async Task InitializeAsync(IServiceProvider sp, IConfiguration cfg, IWebHostEnvironment env)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));

        if (cfg.GetValue<bool>("Seed:DemoOrganization") && !await db.Organizations.AnyAsync())
            await SeedDemo(sp, cfg, db);
    }

    public static string NewOrgCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no look-alike characters
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    private static async Task SeedDemo(IServiceProvider sp, IConfiguration cfg, AppDbContext db)
    {
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var org = new Organization { Name = "Demo Campus", Code = "DEMO01", Type = OrgType.Education, TimeZoneId = cfg["Seed:TimeZone"] ?? "UTC", ContactEmail = "admin@demo.local", RequireApproval = false, RequireDeviceBinding = false, MaxAccuracyMeters = 100 };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var shift = new Shift { OrganizationId = org.Id, Name = "General (9-5)" };
        db.Shifts.Add(shift);
        db.Sites.Add(new Site { OrganizationId = org.Id, Name = "Main Campus", Address = "Demo location - edit it to your real campus", Shape = SiteShape.Circle,
            CenterLat = cfg.GetValue("Seed:Lat", 28.6139), CenterLng = cfg.GetValue("Seed:Lng", 77.2090), RadiusMeters = 150 });
        await db.SaveChangesAsync();
        org.DefaultShiftId = shift.Id;
        await db.SaveChangesAsync();

        var admin = new ApplicationUser
        {
            UserName = "admin@demo.local", Email = "admin@demo.local", EmailConfirmed = true, FullName = "Demo Admin",
            OrganizationId = org.Id, IsAdmin = true, Status = UserStatus.Active
        };
        var pwd = cfg["Seed:AdminPassword"] ?? "Admin#12345";
        var res = await users.CreateAsync(admin, pwd);
        if (res.Succeeded) await users.AddToRoleAsync(admin, "Admin");
    }
}
