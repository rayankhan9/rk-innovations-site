using System.ComponentModel.DataAnnotations;
using System.Text;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers;

public class AttendanceFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? UserId { get; set; }
    public int? SiteId { get; set; }
    public bool FlaggedOnly { get; set; }
    public int Page { get; set; } = 1;
}

public class ManualPunchVm
{
    public long Id { get; set; }
    [Required, Display(Name = "Member")] public string UserId { get; set; } = "";
    [Required] public DateOnly Date { get; set; }
    [Required, Display(Name = "Sign in")] public string InTime { get; set; } = "09:00";
    [Display(Name = "Sign out")] public string? OutTime { get; set; }
    public int? SiteId { get; set; }
    [Required, StringLength(300), Display(Name = "Reason (audited)")] public string Notes { get; set; } = "";
}

public class AttendanceController : AdminController
{
    private const int PageSize = 50;
    private readonly SelfieStore _selfies;
    public AttendanceController(AppDbContext db, AuditService audit, SelfieStore selfies) : base(db, audit) => _selfies = selfies;

    private IQueryable<AttendanceRecord> Query(AttendanceFilter f, Organization org)
    {
        var today = OrgTime.Today(org);
        f.To ??= today; f.From ??= f.To.Value.AddDays(-6);
        var q = Db.Attendance.Include(a => a.User).Include(a => a.Site).AsNoTracking().Where(a => a.WorkDate >= f.From && a.WorkDate <= f.To);
        if (!string.IsNullOrEmpty(f.UserId)) q = q.Where(a => a.UserId == f.UserId);
        if (f.SiteId != null) q = q.Where(a => a.SiteId == f.SiteId);
        if (f.FlaggedOnly) q = q.Where(a => a.IsFlagged);
        return q.OrderByDescending(a => a.CheckInUtc);
    }

    private async Task<List<SelectListItem>> UserList() =>
        (await Db.Users.Where(u => u.OrganizationId == OrgId && u.Status == UserStatus.Active).OrderBy(u => u.FullName).ToListAsync())
        .Select(u => new SelectListItem($"{u.FullName}{(u.EmployeeCode != null ? " (" + u.EmployeeCode + ")" : "")}", u.Id)).ToList();

    public async Task<IActionResult> Index(AttendanceFilter f)
    {
        var org = await GetOrg();
        var q = Query(f, org);
        int total = await q.CountAsync();
        ViewBag.Total = total; ViewBag.PageSize = PageSize; ViewBag.Filter = f;
        ViewBag.Users = await UserList();
        ViewBag.Sites = await Db.Sites.OrderBy(s => s.Name).ToListAsync();
        return View(await q.Skip((Math.Max(1, f.Page) - 1) * PageSize).Take(PageSize).ToListAsync());
    }

    public async Task<IActionResult> Export(AttendanceFilter f)
    {
        var org = await GetOrg();
        var rows = await Query(f, org).Take(50000).ToListAsync();
        var sb = new StringBuilder("Date,Name,EmployeeCode,Site,CheckIn,CheckOut,Hours,Late,LateMinutes,InDistanceM,Source,Flagged,FlagReason\n");
        foreach (var a in rows)
            sb.AppendLine(string.Join(",", a.WorkDate.ToString("yyyy-MM-dd"), ReportBuilder.Q(a.User?.FullName), ReportBuilder.Q(a.User?.EmployeeCode), ReportBuilder.Q(a.Site?.Name),
                FormatLocal(a.CheckInUtc, "yyyy-MM-dd HH:mm"), a.CheckOutUtc == null ? "" : FormatLocal(a.CheckOutUtc, "yyyy-MM-dd HH:mm"),
                a.WorkedHours.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), a.IsLate, a.LateMinutes,
                a.InDistanceMeters?.ToString("0", System.Globalization.CultureInfo.InvariantCulture), a.Source, a.IsFlagged, ReportBuilder.Q(a.FlagReason)));
        await Audit.LogAsync("export.attendance", $"{rows.Count} rows");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"attendance_{f.From:yyyyMMdd}_{f.To:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> Details(long id)
    {
        var a = await Db.Attendance.Include(x => x.User).Include(x => x.Site).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return a is null ? NotFound() : View(a);
    }

    /// <summary>Selfies live outside wwwroot and are only served to admins of the same organization.</summary>
    public async Task<IActionResult> Selfie(long id, bool outPunch)
    {
        var a = await Db.Attendance.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        var rel = outPunch ? a?.OutSelfie : a?.InSelfie;
        if (rel is null) return NotFound();
        var full = _selfies.ResolveSafe(OrgId, rel);
        return full is null ? NotFound() : PhysicalFile(full, full.EndsWith(".png") ? "image/png" : "image/jpeg");
    }

    [HttpGet]
    public async Task<IActionResult> Manual(long? id, string? userId)
    {
        var org = await GetOrg();
        var vm = new ManualPunchVm { Date = OrgTime.Today(org), UserId = userId ?? "" };
        if (id != null)
        {
            var a = await Db.Attendance.FindAsync(id.Value); if (a is null) return NotFound();
            vm = new ManualPunchVm
            {
                Id = a.Id, UserId = a.UserId, Date = a.WorkDate, SiteId = a.SiteId, Notes = a.Notes ?? "",
                InTime = OrgTime.ToLocal(a.CheckInUtc, org).ToString("HH:mm"),
                OutTime = a.CheckOutUtc == null ? null : OrgTime.ToLocal(a.CheckOutUtc.Value, org).ToString("HH:mm")
            };
        }
        await ManualLists(); return View(vm);
    }

    private async Task ManualLists() { ViewBag.Users = await UserList(); ViewBag.Sites = await Db.Sites.OrderBy(s => s.Name).ToListAsync(); }

    [HttpPost]
    public async Task<IActionResult> Manual(ManualPunchVm vm)
    {
        var org = await GetOrg();
        TimeSpan? tout = null;
        if (!TimeSpan.TryParse(vm.InTime, out var tin)) ModelState.AddModelError(nameof(vm.InTime), "Invalid time");
        if (!string.IsNullOrWhiteSpace(vm.OutTime)) { if (TimeSpan.TryParse(vm.OutTime, out var o)) tout = o; else ModelState.AddModelError(nameof(vm.OutTime), "Invalid time"); }
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == vm.UserId && u.OrganizationId == OrgId);
        if (user is null) ModelState.AddModelError(nameof(vm.UserId), "Unknown member");
        if (!ModelState.IsValid) { await ManualLists(); return View(vm); }

        var cin = OrgTime.LocalToUtc(vm.Date, tin, org);
        DateTime? cout = tout == null ? null : OrgTime.LocalToUtc(tout.Value < tin ? vm.Date.AddDays(1) : vm.Date, tout.Value, org);
        AttendanceRecord a;
        if (vm.Id == 0) { a = new AttendanceRecord { OrganizationId = OrgId, UserId = user!.Id }; Db.Attendance.Add(a); }
        else { a = (await Db.Attendance.FindAsync(vm.Id))!; if (a is null) return NotFound(); }
        var shift = await new StatsService(Db).ShiftFor(user!, org);
        a.WorkDate = vm.Date; a.CheckInUtc = cin; a.CheckOutUtc = cout; a.SiteId = vm.SiteId; a.Source = PunchSource.Manual; a.Notes = vm.Notes.Trim();
        a.IsLate = shift.IsWorkDay(vm.Date.DayOfWeek) && tin > shift.StartTime + TimeSpan.FromMinutes(shift.GraceMinutes);
        a.LateMinutes = a.IsLate ? (int)(tin - shift.StartTime).TotalMinutes : 0;
        a.IsFlagged = false; a.FlagReason = null;
        await Db.SaveChangesAsync();
        await Audit.LogAsync(vm.Id == 0 ? "attendance.manual_add" : "attendance.edit", $"{user!.FullName} {vm.Date:yyyy-MM-dd} {vm.InTime}-{vm.OutTime}: {vm.Notes}");
        Success("Attendance saved.");
        return RedirectToAction(nameof(Details), new { id = a.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Clear(long id)
    {
        var a = await Db.Attendance.FindAsync(id); if (a is null) return NotFound();
        a.IsFlagged = false; await Db.SaveChangesAsync();
        await Audit.LogAsync("attendance.unflag", $"#{id}");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(long id)
    {
        var a = await Db.Attendance.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id); if (a is null) return NotFound();
        await Audit.LogAsync("attendance.delete", $"{a.User?.FullName} {a.WorkDate:yyyy-MM-dd}");
        Db.Attendance.Remove(a); await Db.SaveChangesAsync();
        Success("Record deleted.");
        return RedirectToAction(nameof(Index));
    }
}

// ================================================================= Leave
public class LeavesController : AdminController
{
    public LeavesController(AppDbContext db, AuditService audit) : base(db, audit) { }

    public async Task<IActionResult> Index(RequestStatus? status)
    {
        ViewBag.Status = status;
        var q = Db.Leaves.Include(l => l.User).AsNoTracking();
        if (status != null) q = q.Where(l => l.Status == status);
        return View(await q.OrderBy(l => l.Status).ThenByDescending(l => l.FromDate).Take(300).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Review(int id, bool approve, string? note)
    {
        var l = await Db.Leaves.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id); if (l is null) return NotFound();
        if (l.Status != RequestStatus.Pending) { Fail("Already reviewed."); return RedirectToAction(nameof(Index)); }
        l.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected;
        l.ReviewedBy = User.Identity?.Name; l.ReviewedUtc = DateTime.UtcNow; l.ReviewNote = note?.Trim();
        await Db.SaveChangesAsync();
        await Audit.LogAsync("leave." + l.Status.ToString().ToLower(), $"{l.User?.FullName} {l.FromDate:yyyy-MM-dd}→{l.ToDate:yyyy-MM-dd}");
        Success($"Leave {l.Status.ToString().ToLower()}.");
        return RedirectToAction(nameof(Index), new { status = RequestStatus.Pending });
    }
}

// ================================================================= Regularizations
public class RegularizationsController : AdminController
{
    public RegularizationsController(AppDbContext db, AuditService audit) : base(db, audit) { }

    public async Task<IActionResult> Index(RequestStatus? status)
    {
        ViewBag.Status = status;
        var q = Db.Regularizations.Include(r => r.User).AsNoTracking();
        if (status != null) q = q.Where(r => r.Status == status);
        return View(await q.OrderBy(r => r.Status).ThenByDescending(r => r.Date).Take(300).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Review(int id, bool approve, string? note)
    {
        var r = await Db.Regularizations.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id); if (r is null) return NotFound();
        if (r.Status != RequestStatus.Pending) { Fail("Already reviewed."); return RedirectToAction(nameof(Index)); }
        var org = await GetOrg();
        if (approve)
        {
            var day = await Db.Attendance.Where(a => a.UserId == r.UserId && a.WorkDate == r.Date).OrderBy(a => a.CheckInUtc).ToListAsync();
            var shift = await new StatsService(Db).ShiftFor(r.User!, org);
            var rec = day.FirstOrDefault();
            if (rec is null)
            {
                if (r.RequestedIn is null) { Fail("Can't create a record without a sign-in time."); return RedirectToAction(nameof(Index)); }
                rec = new AttendanceRecord { OrganizationId = OrgId, UserId = r.UserId, WorkDate = r.Date, CheckInUtc = OrgTime.LocalToUtc(r.Date, r.RequestedIn.Value, org) };
                Db.Attendance.Add(rec);
            }
            if (r.RequestedIn is { } tin) rec.CheckInUtc = OrgTime.LocalToUtc(r.Date, tin, org);
            if (r.RequestedOut is { } tout) rec.CheckOutUtc = OrgTime.LocalToUtc(r.Date, tout, org);
            var inLocal = OrgTime.ToLocal(rec.CheckInUtc, org).TimeOfDay;
            rec.IsLate = shift.IsWorkDay(r.Date.DayOfWeek) && inLocal > shift.StartTime + TimeSpan.FromMinutes(shift.GraceMinutes);
            rec.LateMinutes = rec.IsLate ? (int)(inLocal - shift.StartTime).TotalMinutes : 0;
            rec.Source = PunchSource.Regularization; rec.IsFlagged = false;
            rec.Notes = $"Regularized: {r.Reason}";
        }
        r.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected;
        r.ReviewedBy = User.Identity?.Name; r.ReviewedUtc = DateTime.UtcNow; r.ReviewNote = note?.Trim();
        await Db.SaveChangesAsync();
        await Audit.LogAsync("regularization." + r.Status.ToString().ToLower(), $"{r.User?.FullName} {r.Date:yyyy-MM-dd}");
        Success($"Request {r.Status.ToString().ToLower()}.");
        return RedirectToAction(nameof(Index), new { status = RequestStatus.Pending });
    }
}

// ================================================================= Reports
public class ReportsController : AdminController
{
    private readonly StatsService _stats;
    private readonly MonthlyReports _reports;
    public ReportsController(AppDbContext db, AuditService audit, StatsService stats, MonthlyReports reports) : base(db, audit) { _stats = stats; _reports = reports; }

    public async Task<IActionResult> Monthly(int? year, int? month, string? dept)
    {
        var org = await GetOrg();
        var t = OrgTime.Today(org);
        int y = year ?? t.Year, m = month ?? t.Month;
        var rows = await _stats.GetOrgMonthAsync(org, y, m);
        ViewBag.Departments = rows.Select(r => r.User.Department).Where(d => !string.IsNullOrEmpty(d)).Distinct().OrderBy(d => d).ToList();
        if (!string.IsNullOrEmpty(dept)) rows = rows.Where(r => r.User.Department == dept).ToList();
        ViewBag.Year = y; ViewBag.Month = m; ViewBag.Dept = dept;
        ViewBag.LastSent = await Db.ReportLogs.Where(r => r.Year == y && r.Month == m && r.Success && r.UserId != null).CountAsync();
        return View(rows);
    }

    public async Task<IActionResult> Csv(int year, int month, string? dept)
    {
        var org = await GetOrg();
        var rows = await _stats.GetOrgMonthAsync(org, year, month);
        if (!string.IsNullOrEmpty(dept)) rows = rows.Where(r => r.User.Department == dept).ToList();
        await Audit.LogAsync("export.monthly", $"{year}-{month:00}");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportBuilder.Csv(rows))).ToArray(), "text/csv", $"monthly_{year}{month:00}.csv");
    }

    public async Task<IActionResult> Daily(DateOnly? date)
    {
        var org = await GetOrg();
        var d = date ?? OrgTime.Today(org);
        var members = await _stats.Members(org).OrderBy(u => u.FullName).AsNoTracking().ToListAsync();
        var bundle = await _stats.LoadAsync(org, d, d);
        var rows = new List<(ApplicationUser, DayInfo)>();
        var shifts = await Db.Shifts.ToDictionaryAsync(s => s.Id);
        foreach (var u in members)
        {
            var id = u.ShiftId ?? org.DefaultShiftId;
            var shift = id != null && shifts.TryGetValue(id.Value, out var sh) ? sh : new Shift();
            rows.Add((u, StatsService.BuildDays(u, shift, org, d, d, bundle)[0]));
        }
        ViewBag.Date = d;
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> SendNow(int year, int month, bool force)
    {
        var org = await GetOrg();
        var n = await _reports.SendForMonthAsync(org, year, month, force);
        await Audit.LogAsync("report.send", $"{year}-{month:00} sent={n} force={force}");
        Success($"{n} report email(s) sent for {new DateTime(year, month, 1):MMMM yyyy}.");
        return RedirectToAction(nameof(Monthly), new { year, month });
    }
}

// ================================================================= Settings / Audit
public class SettingsVm
{
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public OrgType Type { get; set; }
    [Required] public string TimeZoneId { get; set; } = "UTC";
    [EmailAddress] public string? ContactEmail { get; set; }
    public bool RequireApproval { get; set; }
    public bool RequireSelfie { get; set; }
    public bool BlockMockLocation { get; set; }
    public bool RequireDeviceBinding { get; set; }
    [Range(10, 500)] public int MaxAccuracyMeters { get; set; }
    [Range(0, 200)] public int GeofenceToleranceMeters { get; set; }
    [Range(1, 60)] public int MaxClockDriftMinutes { get; set; }
    [Range(1, 168)] public int MaxOfflineSyncHours { get; set; }
    public bool AutoCloseOpenSessions { get; set; }
    public bool SendMonthlyReports { get; set; }
    public bool SendAdminMonthlySummary { get; set; }
    public string Code { get; set; } = "";
}

public class SettingsController : AdminController
{
    private readonly IMailSender _mail;
    public SettingsController(AppDbContext db, AuditService audit, IMailSender mail) : base(db, audit) => _mail = mail;

    public async Task<IActionResult> Index()
    {
        var o = await GetOrg();
        return View(new SettingsVm
        {
            Name = o.Name, Type = o.Type, TimeZoneId = o.TimeZoneId, ContactEmail = o.ContactEmail, RequireApproval = o.RequireApproval, RequireSelfie = o.RequireSelfie,
            BlockMockLocation = o.BlockMockLocation, RequireDeviceBinding = o.RequireDeviceBinding, MaxAccuracyMeters = o.MaxAccuracyMeters,
            GeofenceToleranceMeters = o.GeofenceToleranceMeters, MaxClockDriftMinutes = o.MaxClockDriftMinutes, MaxOfflineSyncHours = o.MaxOfflineSyncHours,
            AutoCloseOpenSessions = o.AutoCloseOpenSessions, SendMonthlyReports = o.SendMonthlyReports, SendAdminMonthlySummary = o.SendAdminMonthlySummary, Code = o.Code
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(SettingsVm vm)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(vm.TimeZoneId); } catch { ModelState.AddModelError(nameof(vm.TimeZoneId), "Unknown time zone."); }
        if (!ModelState.IsValid) return View(vm);
        var o = await GetOrg();
        o.Name = vm.Name.Trim(); o.Type = vm.Type; o.TimeZoneId = vm.TimeZoneId; o.ContactEmail = vm.ContactEmail;
        o.RequireApproval = vm.RequireApproval; o.RequireSelfie = vm.RequireSelfie; o.BlockMockLocation = vm.BlockMockLocation;
        o.RequireDeviceBinding = vm.RequireDeviceBinding; o.MaxAccuracyMeters = vm.MaxAccuracyMeters; o.GeofenceToleranceMeters = vm.GeofenceToleranceMeters;
        o.MaxClockDriftMinutes = vm.MaxClockDriftMinutes; o.MaxOfflineSyncHours = vm.MaxOfflineSyncHours; o.AutoCloseOpenSessions = vm.AutoCloseOpenSessions;
        o.SendMonthlyReports = vm.SendMonthlyReports; o.SendAdminMonthlySummary = vm.SendAdminMonthlySummary;
        await Db.SaveChangesAsync();
        await Audit.LogAsync("settings.update", "Organization settings changed");
        Success("Settings saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RegenerateCode()
    {
        var o = await GetOrg(); string code;
        do code = Seeder.NewOrgCode(); while (await Db.Organizations.AnyAsync(x => x.Code == code));
        o.Code = code; await Db.SaveChangesAsync();
        await Audit.LogAsync("org.code_regenerated", "Join code changed");
        Success($"New join code: {code}. The old code no longer works.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> TestEmail()
    {
        var o = await GetOrg();
        try { await _mail.SendAsync(o.ContactEmail ?? User.Identity!.Name!, "AttendancePro test email", "<p>Email delivery works.</p>"); Success("Test email sent (or saved to App_Data/outbox if SMTP is not configured)."); }
        catch (Exception ex) { Fail("Email failed: " + ex.Message); }
        return RedirectToAction(nameof(Index));
    }
}

public class AuditController : AdminController
{
    public AuditController(AppDbContext db, AuditService audit) : base(db, audit) { }
    public async Task<IActionResult> Index(int page = 1)
    {
        ViewBag.Page = page;
        ViewBag.Total = await Db.AuditLogs.CountAsync();
        return View(await Db.AuditLogs.AsNoTracking().OrderByDescending(a => a.AtUtc).Skip((Math.Max(1, page) - 1) * 50).Take(50).ToListAsync());
    }
}
