using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers.Api;

public record UpdateProfileDto([Required, StringLength(120, MinimumLength = 2)] string FullName, [StringLength(80)] string? Department, [Phone] string? Phone);
public record ChangePasswordDto([Required] string CurrentPassword, [Required, StringLength(100, MinimumLength = 8)] string NewPassword);
public record LeaveDto(LeaveType Type, DateOnly FromDate, DateOnly ToDate, [StringLength(500)] string? Reason);
public record RegularizeDto(DateOnly Date, string? InTime, string? OutTime, [Required, StringLength(500)] string Reason);

[ApiController]
[Route("api/v1/me")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MeApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly StatsService _stats;
    private readonly AttendanceService _att;
    private readonly TokenService _tokens;
    private readonly AuditService _audit;

    public MeApiController(AppDbContext db, UserManager<ApplicationUser> users, StatsService stats, AttendanceService att, TokenService tokens, AuditService audit)
    { _db = db; _users = users; _stats = stats; _att = att; _tokens = tokens; _audit = audit; }

    private async Task<(ApplicationUser User, Organization Org)?> Ctx()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _users.FindByIdAsync(id!);
        if (user is null || user.Status == UserStatus.Disabled) return null;
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == user.OrganizationId);
        return (user, org);
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (await Ctx() is not { } c) return Unauthorized();
        return Ok(ApiMap.User(c.User, c.Org));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileDto dto)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        c.User.FullName = dto.FullName.Trim(); c.User.Department = dto.Department?.Trim(); c.User.PhoneNumber = dto.Phone;
        await _users.UpdateAsync(c.User);
        return Ok(ApiMap.User(c.User, c.Org));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        var r = await _users.ChangePasswordAsync(c.User, dto.CurrentPassword, dto.NewPassword);
        if (!r.Succeeded) return BadRequest(new { code = "BAD_PASSWORD", message = string.Join(" ", r.Errors.Select(e => e.Description)) });
        await _tokens.RevokeAllAsync(c.User.Id);
        return Ok(new { message = "Password changed. Please sign in again on other devices." });
    }

    /// <summary>Everything the home screen needs in one round trip.</summary>
    [HttpGet("home")]
    public async Task<IActionResult> Home()
    {
        if (await Ctx() is not { } c) return Unauthorized();
        var (user, org) = c;
        var sites = await _att.AllowedSitesAsync(user);
        var shift = await _stats.ShiftFor(user, org);
        var today = OrgTime.Today(org);
        var todays = await _db.Attendance.AsNoTracking().Where(a => a.UserId == user.Id && a.WorkDate == today).OrderBy(a => a.CheckInUtc).ToListAsync();
        var open = await _db.Attendance.AsNoTracking().Where(a => a.UserId == user.Id && a.CheckOutUtc == null).OrderByDescending(a => a.CheckInUtc).FirstOrDefaultAsync();
        var siteNames = sites.ToDictionary(s => s.Id, s => s.Name);
        return Ok(new
        {
            serverTimeUtc = Clock.UtcNow,
            user = ApiMap.User(user, org),
            policy = new
            {
                maxAccuracyMeters = org.MaxAccuracyMeters, requireSelfie = org.RequireSelfie, blockMockLocation = org.BlockMockLocation,
                geofenceToleranceMeters = org.GeofenceToleranceMeters, maxClockDriftMinutes = org.MaxClockDriftMinutes,
                maxOfflineSyncHours = org.MaxOfflineSyncHours
            },
            shift = new { shift.Name, start = shift.StartTime.ToString(@"hh\:mm"), end = shift.EndTime.ToString(@"hh\:mm"), shift.GraceMinutes, workDays = Enumerable.Range(0, 7).Where(i => shift.IsWorkDay((DayOfWeek)i)).ToArray() },
            sites = sites.Select(s => new
            {
                s.Id, s.Name, s.Address, shape = s.Shape.ToString(), lat = s.CenterLat, lng = s.CenterLng, radius = s.RadiusMeters,
                polygon = s.Shape == SiteShape.Polygon ? GeoService.ParsePolygon(s.PolygonJson).Select(p => new[] { p.Lat, p.Lng }) : null
            }),
            openSession = open is null ? null : new { open.Id, checkInUtc = open.CheckInUtc, siteName = open.SiteId != null && siteNames.TryGetValue(open.SiteId.Value, out var n) ? n : null },
            today = new
            {
                date = today.ToString("yyyy-MM-dd"),
                hours = Math.Round(todays.Sum(t => t.CheckOutUtc != null ? t.WorkedHours : (Clock.UtcNow - t.CheckInUtc).TotalHours), 2),
                sessions = todays.Select(t => new { checkInUtc = t.CheckInUtc, checkOutUtc = t.CheckOutUtc, t.IsLate })
            }
        });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(int? year, int? month)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        var t = OrgTime.Today(c.Org);
        int y = year ?? t.Year, m = month ?? t.Month;
        if (m is < 1 or > 12 || y is < 2000 or > 2100) return BadRequest();
        var s = await _stats.GetUserMonthAsync(c.User, c.Org, y, m);
        return Ok(new
        {
            s.Year, s.Month, s.WorkingDays, s.PresentDays, s.LateDays, s.HalfDays, s.AbsentDays, s.LeaveDays, s.Holidays,
            s.TotalHours, s.AvgHoursPerDay, s.AttendancePercent, s.PunctualityPercent, s.CurrentStreak, s.TotalLateMinutes,
            days = s.Days.Select(d => new { date = d.Date.ToString("yyyy-MM-dd"), status = d.Status.ToString(), d.Hours, d.LateMinutes, d.Note, firstInUtc = d.FirstInUtc, lastOutUtc = d.LastOutUtc })
        });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(int? year, int? month)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        var t = OrgTime.Today(c.Org);
        var from = new DateOnly(year ?? t.Year, month ?? t.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var recs = await _db.Attendance.AsNoTracking().Include(a => a.Site)
            .Where(a => a.UserId == c.User.Id && a.WorkDate >= from && a.WorkDate <= to).OrderByDescending(a => a.CheckInUtc).ToListAsync();
        return Ok(recs.Select(a => new
        {
            a.Id, date = a.WorkDate.ToString("yyyy-MM-dd"), checkInUtc = a.CheckInUtc, checkOutUtc = a.CheckOutUtc,
            site = a.Site?.Name, hours = Math.Round(a.WorkedHours, 2), a.IsLate, a.LateMinutes, a.IsEarlyLeave,
            a.IsOfflineSync, source = a.Source.ToString(), note = a.IsFlagged ? "Under review" : null
        }));
    }

    // ---- Leave ---------------------------------------------------------------------------------
    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var list = await _db.Leaves.AsNoTracking().Where(l => l.UserId == uid).OrderByDescending(l => l.FromDate).Take(100).ToListAsync();
        return Ok(list.Select(l => new
        {
            l.Id, type = l.Type.ToString(), from = l.FromDate.ToString("yyyy-MM-dd"), to = l.ToDate.ToString("yyyy-MM-dd"),
            l.Reason, status = l.Status.ToString(), l.ReviewNote, days = l.ToDate.DayNumber - l.FromDate.DayNumber + 1
        }));
    }

    [HttpPost("leaves")]
    public async Task<IActionResult> ApplyLeave(LeaveDto dto)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        if (dto.ToDate < dto.FromDate) return BadRequest(new { code = "BAD_DATES", message = "End date is before start date." });
        if ((dto.ToDate.DayNumber - dto.FromDate.DayNumber) > 90) return BadRequest(new { code = "TOO_LONG", message = "Maximum 90 days per request." });
        if (dto.FromDate < OrgTime.Today(c.Org).AddDays(-31)) return BadRequest(new { code = "TOO_OLD", message = "Cannot apply for leave older than 31 days." });
        bool overlap = await _db.Leaves.AnyAsync(l => l.UserId == c.User.Id && l.Status != RequestStatus.Rejected && l.Status != RequestStatus.Cancelled && l.FromDate <= dto.ToDate && l.ToDate >= dto.FromDate);
        if (overlap) return Conflict(new { code = "OVERLAP", message = "You already have a leave request for these dates." });
        _db.Leaves.Add(new LeaveRequest { OrganizationId = c.Org.Id, UserId = c.User.Id, Type = dto.Type, FromDate = dto.FromDate, ToDate = dto.ToDate, Reason = dto.Reason?.Trim() });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Leave request submitted." });
    }

    [HttpPost("leaves/{id:int}/cancel")]
    public async Task<IActionResult> CancelLeave(int id)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var l = await _db.Leaves.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (l is null) return NotFound();
        if (l.Status is RequestStatus.Rejected or RequestStatus.Cancelled) return BadRequest(new { code = "FINAL", message = "Already closed." });
        l.Status = RequestStatus.Cancelled;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Leave cancelled." });
    }

    // ---- Regularization ----------------------------------------------------------------------
    [HttpGet("regularizations")]
    public async Task<IActionResult> Regs()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var list = await _db.Regularizations.AsNoTracking().Where(r => r.UserId == uid).OrderByDescending(r => r.Date).Take(60).ToListAsync();
        return Ok(list.Select(r => new
        {
            r.Id, date = r.Date.ToString("yyyy-MM-dd"), inTime = r.RequestedIn?.ToString(@"hh\:mm"), outTime = r.RequestedOut?.ToString(@"hh\:mm"),
            r.Reason, status = r.Status.ToString(), r.ReviewNote
        }));
    }

    [HttpPost("regularizations")]
    public async Task<IActionResult> Regularize(RegularizeDto dto)
    {
        if (await Ctx() is not { } c) return Unauthorized();
        if (!TryTime(dto.InTime, out var tin)) return BadRequest(new { code = "BAD_TIME", message = "Invalid in-time." });
        if (!TryTime(dto.OutTime, out var tout)) return BadRequest(new { code = "BAD_TIME", message = "Invalid out-time." });
        if (tin is null && tout is null) return BadRequest(new { code = "BAD_TIME", message = "Provide an in-time, out-time, or both." });
        var today = OrgTime.Today(c.Org);
        if (dto.Date > today || dto.Date < today.AddDays(-31)) return BadRequest(new { code = "BAD_DATE", message = "Date must be within the last 31 days." });
        if (await _db.Regularizations.AnyAsync(r => r.UserId == c.User.Id && r.Date == dto.Date && r.Status == RequestStatus.Pending))
            return Conflict(new { code = "PENDING", message = "A request for this date is already pending." });
        _db.Regularizations.Add(new RegularizationRequest { OrganizationId = c.Org.Id, UserId = c.User.Id, Date = dto.Date, RequestedIn = tin, RequestedOut = tout, Reason = dto.Reason.Trim() });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Request sent to your administrator." });
    }

    private static bool TryTime(string? s, out TimeSpan? t)
    {
        t = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (!TimeSpan.TryParse(s, out var v) || v < TimeSpan.Zero || v >= TimeSpan.FromDays(1)) return false;
        t = v; return true;
    }

    [HttpGet("holidays")]
    public async Task<IActionResult> Holidays(int? year)
    {
        int y = year ?? Clock.UtcNow.Year;
        var list = await _db.Holidays.AsNoTracking().Where(h => h.Date.Year == y).OrderBy(h => h.Date).ToListAsync();
        return Ok(list.Select(h => new { date = h.Date.ToString("yyyy-MM-dd"), h.Name }));
    }
}

[ApiController]
[Route("api/v1/attendance")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class AttendanceApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AttendanceService _att;
    public AttendanceApiController(AppDbContext db, UserManager<ApplicationUser> users, AttendanceService att) { _db = db; _users = users; _att = att; }

    private static object Dto(PunchResult r) => new
    {
        ok = r.Ok, code = r.Code, message = r.Message, duplicate = r.Duplicate, site = r.SiteName,
        distanceMeters = r.DistanceMeters is null or double.MaxValue ? null : (double?)Math.Round(r.DistanceMeters.Value, 1),
        checkInUtc = r.Record?.CheckInUtc, checkOutUtc = r.Record?.CheckOutUtc
    };

    private async Task<(ApplicationUser, Organization)?> Ctx()
    {
        var user = await _users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return null;
        return (user, await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == user.OrganizationId));
    }

    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn(PunchRequest req)
    {
        if (await Ctx() is not var (u, o)) return Unauthorized();
        return Ok(Dto(await _att.CheckInAsync(u, o, req)));
    }

    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut(PunchRequest req)
    {
        if (await Ctx() is not var (u, o)) return Unauthorized();
        return Ok(Dto(await _att.CheckOutAsync(u, o, req)));
    }

    /// <summary>Replays punches captured offline, oldest first. Each result is final (client drops rejected ones).</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(List<PunchRequest> batch)
    {
        if (await Ctx() is not var (u, o)) return Unauthorized();
        if (batch.Count > 50) return BadRequest(new { code = "TOO_MANY", message = "Max 50 per batch." });
        var results = new List<object>();
        foreach (var p in batch.OrderBy(p => p.ClientTimeUtc))
        {
            p.Offline = true;
            var r = p.Kind == "out" ? await _att.CheckOutAsync(u, o, p) : await _att.CheckInAsync(u, o, p);
            results.Add(new { eventId = p.EventId, result = Dto(r) });
        }
        return Ok(results);
    }
}
