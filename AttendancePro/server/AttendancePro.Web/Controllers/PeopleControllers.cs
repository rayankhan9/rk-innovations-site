using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers;

public class UserEditVm
{
    public string? Id { get; set; }
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [StringLength(40), Display(Name = "Employee / roll no.")] public string? EmployeeCode { get; set; }
    [StringLength(80)] public string? Department { get; set; }
    [StringLength(60)] public string? Designation { get; set; }
    [Display(Name = "Shift")] public int? ShiftId { get; set; }
    [Display(Name = "Portal admin")] public bool IsAdmin { get; set; }
    [Display(Name = "Email monthly report")] public bool ReceiveMonthlyReport { get; set; } = true;
    public UserStatus Status { get; set; } = UserStatus.Active;
    [DataType(DataType.Password), Display(Name = "Temporary password (new users)")] public string? Password { get; set; }
    public List<int> SiteIds { get; set; } = new();
    public List<SelectListItem> Shifts { get; set; } = new();
    public List<Site> AllSites { get; set; } = new();
    public string? DeviceModel { get; set; }
    public bool HasDevice { get; set; }
}

public class UsersController : AdminController
{
    private readonly UserManager<ApplicationUser> _um;
    private readonly StatsService _stats;
    private readonly TokenService _tokens;
    public UsersController(AppDbContext db, AuditService audit, UserManager<ApplicationUser> um, StatsService stats, TokenService tokens) : base(db, audit) { _um = um; _stats = stats; _tokens = tokens; }

    public async Task<IActionResult> Index(string? q, UserStatus? status, string? dept)
    {
        var query = Db.Users.Include(u => u.Shift).Where(u => u.OrganizationId == OrgId);
        if (status != null) query = query.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(dept)) query = query.Where(u => u.Department == dept);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.FullName.Contains(q) || u.Email!.Contains(q) || (u.EmployeeCode != null && u.EmployeeCode.Contains(q)));
        ViewBag.Q = q; ViewBag.Status = status; ViewBag.Dept = dept;
        ViewBag.Departments = await Db.Users.Where(u => u.OrganizationId == OrgId && u.Department != null).Select(u => u.Department!).Distinct().OrderBy(d => d).ToListAsync();
        return View(await query.OrderBy(u => u.Status).ThenBy(u => u.FullName).AsNoTracking().ToListAsync());
    }

    public async Task<IActionResult> Details(string id, int? year, int? month)
    {
        var u = await Db.Users.Include(x => x.Shift).FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == OrgId);
        if (u is null) return NotFound();
        var org = await GetOrg();
        var t = OrgTime.Today(org);
        ViewBag.Summary = await _stats.GetUserMonthAsync(u, org, year ?? t.Year, month ?? t.Month);
        ViewBag.Recent = await Db.Attendance.Include(a => a.Site).AsNoTracking().Where(a => a.UserId == id).OrderByDescending(a => a.CheckInUtc).Take(15).ToListAsync();
        ViewBag.Sites = await Db.UserSites.Include(s => s.Site).Where(s => s.UserId == id).Select(s => s.Site!.Name).ToListAsync();
        return View(u);
    }

    private async Task Fill(UserEditVm vm)
    {
        vm.Shifts = (await Db.Shifts.OrderBy(s => s.Name).ToListAsync()).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
        vm.AllSites = await Db.Sites.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new UserEditVm { ShiftId = (await GetOrg()).DefaultShiftId };
        await Fill(vm); return View("Edit", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var u = await Db.Users.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == OrgId);
        if (u is null) return NotFound();
        var vm = new UserEditVm
        {
            Id = u.Id, FullName = u.FullName, Email = u.Email!, EmployeeCode = u.EmployeeCode, Department = u.Department, Designation = u.Designation,
            ShiftId = u.ShiftId, IsAdmin = u.IsAdmin, ReceiveMonthlyReport = u.ReceiveMonthlyReport, Status = u.Status,
            SiteIds = await Db.UserSites.Where(s => s.UserId == id).Select(s => s.SiteId).ToListAsync(),
            DeviceModel = u.DeviceModel, HasDevice = !string.IsNullOrEmpty(u.DeviceId)
        };
        await Fill(vm); return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserEditVm vm)
    {
        bool isNew = string.IsNullOrEmpty(vm.Id);
        if (isNew && string.IsNullOrWhiteSpace(vm.Password)) ModelState.AddModelError(nameof(vm.Password), "Set a temporary password for new users.");
        if (!ModelState.IsValid) { await Fill(vm); return View(vm); }

        if (!string.IsNullOrWhiteSpace(vm.EmployeeCode) &&
            await Db.Users.AnyAsync(x => x.OrganizationId == OrgId && x.EmployeeCode == vm.EmployeeCode && x.Id != vm.Id))
        { ModelState.AddModelError(nameof(vm.EmployeeCode), "Already used by someone else."); await Fill(vm); return View(vm); }

        ApplicationUser u;
        if (isNew)
        {
            u = new ApplicationUser { UserName = vm.Email.Trim(), Email = vm.Email.Trim(), OrganizationId = OrgId, EmailConfirmed = true };
            Apply(u, vm);
            var r = await _um.CreateAsync(u, vm.Password!);
            if (!r.Succeeded) { foreach (var e in r.Errors) ModelState.AddModelError("", e.Description); await Fill(vm); return View(vm); }
        }
        else
        {
            u = (await Db.Users.FirstOrDefaultAsync(x => x.Id == vm.Id && x.OrganizationId == OrgId))!;
            if (u is null) return NotFound();
            bool wasAdmin = u.IsAdmin;
            if (wasAdmin && !vm.IsAdmin && await Db.Users.CountAsync(x => x.OrganizationId == OrgId && x.IsAdmin && x.Id != u.Id && x.Status == UserStatus.Active) == 0)
            { ModelState.AddModelError("", "At least one active admin is required."); await Fill(vm); return View(vm); }
            if (!string.Equals(u.Email, vm.Email, StringComparison.OrdinalIgnoreCase)) { await _um.SetEmailAsync(u, vm.Email.Trim()); await _um.SetUserNameAsync(u, vm.Email.Trim()); }
            Apply(u, vm);
            await _um.UpdateAsync(u);
        }
        if (vm.IsAdmin) { if (!await _um.IsInRoleAsync(u, "Admin")) await _um.AddToRoleAsync(u, "Admin"); }
        else if (await _um.IsInRoleAsync(u, "Admin")) await _um.RemoveFromRoleAsync(u, "Admin");

        // Site assignments
        var existing = await Db.UserSites.Where(s => s.UserId == u.Id).ToListAsync();
        Db.UserSites.RemoveRange(existing.Where(e => !vm.SiteIds.Contains(e.SiteId)));
        var validSites = await Db.Sites.Where(s => vm.SiteIds.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        foreach (var sid in validSites.Where(s => existing.All(e => e.SiteId != s)))
            Db.UserSites.Add(new UserSite { OrganizationId = OrgId, UserId = u.Id, SiteId = sid });
        await Db.SaveChangesAsync();

        await Audit.LogAsync(isNew ? "user.create" : "user.update", $"{u.FullName} ({u.Email})");
        Success("User saved.");
        return RedirectToAction(nameof(Details), new { id = u.Id });
    }

    private static void Apply(ApplicationUser u, UserEditVm vm)
    {
        u.FullName = vm.FullName.Trim(); u.EmployeeCode = vm.EmployeeCode?.Trim(); u.Department = vm.Department?.Trim();
        u.Designation = vm.Designation?.Trim(); u.ShiftId = vm.ShiftId; u.IsAdmin = vm.IsAdmin;
        u.ReceiveMonthlyReport = vm.ReceiveMonthlyReport; u.Status = vm.Status;
    }

    private async Task<ApplicationUser?> Find(string id) => await Db.Users.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == OrgId);

    [HttpPost]
    public async Task<IActionResult> SetStatus(string id, UserStatus status, string? back)
    {
        var u = await Find(id); if (u is null) return NotFound();
        if (u.Id == _um.GetUserId(User)) { Fail("You can't change your own status."); return RedirectToAction(nameof(Index)); }
        u.Status = status; await Db.SaveChangesAsync();
        if (status == UserStatus.Disabled) await _tokens.RevokeAllAsync(u.Id);
        await Audit.LogAsync("user.status", $"{u.FullName} → {status}");
        Success($"{u.FullName} is now {status}.");
        return back == "details" ? RedirectToAction(nameof(Details), new { id }) : RedirectToAction(nameof(Index), new { status = back == "pending" ? UserStatus.Pending : (UserStatus?)null });
    }

    [HttpPost]
    public async Task<IActionResult> ResetDevice(string id)
    {
        var u = await Find(id); if (u is null) return NotFound();
        u.DeviceId = null; u.DeviceModel = null; await Db.SaveChangesAsync();
        await Audit.LogAsync("user.device_reset", u.FullName);
        Success("Device binding cleared. The next phone they sign in with will be linked.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string id, string newPassword)
    {
        var u = await Find(id); if (u is null) return NotFound();
        var token = await _um.GeneratePasswordResetTokenAsync(u);
        var r = await _um.ResetPasswordAsync(u, token, newPassword ?? "");
        if (!r.Succeeded) Fail(string.Join(" ", r.Errors.Select(e => e.Description)));
        else { await Audit.LogAsync("user.password_reset", u.FullName); Success("Password reset. Share it with the user securely."); }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        var u = await Find(id); if (u is null) return NotFound();
        if (u.Id == _um.GetUserId(User)) { Fail("You can't delete yourself."); return RedirectToAction(nameof(Index)); }
        if (u.IsAdmin && await Db.Users.CountAsync(x => x.OrganizationId == OrgId && x.IsAdmin && x.Id != u.Id) == 0) { Fail("Can't delete the last admin."); return RedirectToAction(nameof(Index)); }
        await Audit.LogAsync("user.delete", $"{u.FullName} ({u.Email}) and all their records");
        await _um.DeleteAsync(u);
        Success("User and their attendance data permanently deleted.");
        return RedirectToAction(nameof(Index));
    }
}

// ================================================================= Sites
public class SitesController : AdminController
{
    public SitesController(AppDbContext db, AuditService audit) : base(db, audit) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Counts = await Db.UserSites.GroupBy(u => u.SiteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return View(await Db.Sites.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync());
    }

    [HttpGet] public IActionResult Create() => View("Edit", new Site { CenterLat = 20.5937, CenterLng = 78.9629, RadiusMeters = 150 });

    [HttpGet]
    public async Task<IActionResult> Edit(int id) => await Db.Sites.FindAsync(id) is { } s ? View(s) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Edit(Site model)
    {
        if (model.Shape == SiteShape.Circle)
        {
            if (model.RadiusMeters is < 20 or > 5000) ModelState.AddModelError(nameof(model.RadiusMeters), "Radius must be 20 – 5000 m.");
            model.PolygonJson = null;
        }
        else
        {
            var poly = GeoService.ParsePolygon(model.PolygonJson);
            if (poly.Count < 3) ModelState.AddModelError(nameof(model.PolygonJson), "Click at least 3 points on the map to draw the boundary.");
            else { model.CenterLat = poly.Average(p => p.Lat); model.CenterLng = poly.Average(p => p.Lng); }
        }
        if (Math.Abs(model.CenterLat) > 90 || Math.Abs(model.CenterLng) > 180) ModelState.AddModelError("", "Invalid coordinates.");
        ModelState.Remove(nameof(Site.OrganizationId));
        if (!ModelState.IsValid) return View(model);

        Site s;
        if (model.Id == 0) { s = new Site { OrganizationId = OrgId }; Db.Sites.Add(s); }
        else { s = (await Db.Sites.FindAsync(model.Id))!; if (s is null) return NotFound(); }
        s.Name = model.Name.Trim(); s.Address = model.Address?.Trim(); s.Shape = model.Shape; s.CenterLat = model.CenterLat; s.CenterLng = model.CenterLng;
        s.RadiusMeters = model.RadiusMeters; s.PolygonJson = model.PolygonJson; s.IsActive = model.IsActive;
        await Db.SaveChangesAsync();
        await Audit.LogAsync("site.save", $"{s.Name} ({s.Shape}, {(s.Shape == SiteShape.Circle ? s.RadiusMeters + " m" : "polygon")})");
        Success("Location saved. Changes apply to the next punch.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await Db.Sites.FindAsync(id); if (s is null) return NotFound();
        if (await Db.Attendance.AnyAsync(a => a.SiteId == id)) { s.IsActive = false; Success("Location has attendance history, so it was deactivated instead of deleted."); }
        else { Db.Sites.Remove(s); Success("Location deleted."); }
        await Db.SaveChangesAsync();
        await Audit.LogAsync("site.delete", s.Name);
        return RedirectToAction(nameof(Index));
    }
}

// ================================================================= Shifts
public class ShiftVm
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [Required, Display(Name = "Start")] public string Start { get; set; } = "09:00";
    [Required, Display(Name = "End")] public string End { get; set; } = "17:00";
    [Range(0, 180), Display(Name = "Grace (minutes)")] public int GraceMinutes { get; set; } = 10;
    [Range(0, 24), Display(Name = "Half-day below (hours)")] public double HalfDayHours { get; set; } = 4;
    public List<int> Days { get; set; } = new() { 1, 2, 3, 4, 5 };
    public bool MakeDefault { get; set; }
}

public class ShiftsController : AdminController
{
    public ShiftsController(AppDbContext db, AuditService audit) : base(db, audit) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Users = await Db.Users.Where(u => u.OrganizationId == OrgId && u.ShiftId != null).GroupBy(u => u.ShiftId!.Value).ToDictionaryAsync(g => g.Key, g => g.Count());
        return View(await Db.Shifts.OrderBy(s => s.Name).ToListAsync());
    }

    public IActionResult Create() => View("Edit", new ShiftVm());

    public async Task<IActionResult> Edit(int id)
    {
        var s = await Db.Shifts.FindAsync(id); if (s is null) return NotFound();
        return View(new ShiftVm
        {
            Id = s.Id, Name = s.Name, Start = s.StartTime.ToString(@"hh\:mm"), End = s.EndTime.ToString(@"hh\:mm"), GraceMinutes = s.GraceMinutes,
            HalfDayHours = s.HalfDayHours, Days = Enumerable.Range(0, 7).Where(i => s.IsWorkDay((DayOfWeek)i)).ToList(),
            MakeDefault = (await GetOrg()).DefaultShiftId == s.Id
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ShiftVm vm)
    {
        TimeSpan st = default, en = default;
        if (!TimeSpan.TryParse(vm.Start, out st) || !TimeSpan.TryParse(vm.End, out en)) ModelState.AddModelError("", "Invalid time.");
        if (vm.Days.Count == 0) ModelState.AddModelError("", "Select at least one working day.");
        if (!ModelState.IsValid) return View(vm);
        Shift s;
        if (vm.Id == 0) { s = new Shift { OrganizationId = OrgId }; Db.Shifts.Add(s); }
        else { s = (await Db.Shifts.FindAsync(vm.Id))!; if (s is null) return NotFound(); }
        s.Name = vm.Name.Trim(); s.StartTime = st; s.EndTime = en; s.GraceMinutes = vm.GraceMinutes; s.HalfDayHours = vm.HalfDayHours;
        s.WorkDaysMask = vm.Days.Where(d => d is >= 0 and <= 6).Aggregate(0, (m, d) => m | (1 << d));
        await Db.SaveChangesAsync();
        if (vm.MakeDefault) { (await GetOrg()).DefaultShiftId = s.Id; await Db.SaveChangesAsync(); }
        await Audit.LogAsync("shift.save", s.Name);
        Success("Shift saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await Db.Shifts.FindAsync(id); if (s is null) return NotFound();
        if (await Db.Users.AnyAsync(u => u.ShiftId == id) || (await GetOrg()).DefaultShiftId == id) { Fail("Shift is in use (assigned or default)."); return RedirectToAction(nameof(Index)); }
        Db.Shifts.Remove(s); await Db.SaveChangesAsync(); await Audit.LogAsync("shift.delete", s.Name);
        Success("Shift deleted.");
        return RedirectToAction(nameof(Index));
    }
}

// ================================================================= Holidays
public class HolidaysController : AdminController
{
    public HolidaysController(AppDbContext db, AuditService audit) : base(db, audit) { }

    public async Task<IActionResult> Index(int? year)
    {
        int y = year ?? DateTime.UtcNow.Year; ViewBag.Year = y;
        return View(await Db.Holidays.Where(h => h.Date.Year == y).OrderBy(h => h.Date).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Add(DateOnly date, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { Fail("Name required."); return RedirectToAction(nameof(Index)); }
        if (await Db.Holidays.AnyAsync(h => h.Date == date)) Fail("A holiday already exists on that date.");
        else { Db.Holidays.Add(new Holiday { OrganizationId = OrgId, Date = date, Name = name.Trim() }); await Db.SaveChangesAsync(); Success("Holiday added."); }
        return RedirectToAction(nameof(Index), new { year = date.Year });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var h = await Db.Holidays.FindAsync(id); if (h is null) return NotFound();
        Db.Holidays.Remove(h); await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { year = h.Date.Year });
    }
}
