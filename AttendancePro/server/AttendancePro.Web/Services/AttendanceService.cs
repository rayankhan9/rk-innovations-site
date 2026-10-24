using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Services;

public class PunchRequest
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double Accuracy { get; set; }
    public bool IsMock { get; set; }
    /// <summary>Device clock at the moment of the punch (UTC). Required for offline punches.</summary>
    public DateTime? ClientTimeUtc { get; set; }
    public bool Offline { get; set; }
    /// <summary>Client generated GUID so retries / re-syncs are idempotent.</summary>
    public string? EventId { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceModel { get; set; }
    public string? SelfieBase64 { get; set; }
    public string Kind { get; set; } = "in"; // "in" | "out" (used by batch sync)
}

public record PunchResult(bool Ok, string Code, string Message, AttendanceRecord? Record = null,
    string? SiteName = null, double? DistanceMeters = null, bool Duplicate = false);

public class AttendanceService
{
    private const double ImpossibleSpeedMps = 70; // ~250 km/h
    private readonly AppDbContext _db;
    private readonly SelfieStore _selfies;

    public AttendanceService(AppDbContext db, SelfieStore selfies) { _db = db; _selfies = selfies; }

    public Task<PunchResult> CheckInAsync(ApplicationUser user, Organization org, PunchRequest r) => PunchAsync(user, org, r, true);
    public Task<PunchResult> CheckOutAsync(ApplicationUser user, Organization org, PunchRequest r) => PunchAsync(user, org, r, false);

    private async Task<PunchResult> PunchAsync(ApplicationUser user, Organization org, PunchRequest r, bool isIn)
    {
        if (user.Status != UserStatus.Active)
            return Fail("ACCOUNT_INACTIVE", user.Status == UserStatus.Pending ? "Your account is awaiting admin approval." : "Your account is disabled.");

        // 1. Idempotency — a retried/re-synced event returns the stored outcome.
        if (!string.IsNullOrWhiteSpace(r.EventId))
        {
            var dup = await _db.Attendance.FirstOrDefaultAsync(a => a.UserId == user.Id && (a.InEventId == r.EventId || a.OutEventId == r.EventId));
            if (dup != null) return new PunchResult(true, "DUPLICATE", "Already recorded.", dup, Duplicate: true);
        }

        // 2. Device binding (one phone per account, admin can reset) — stops buddy-punching.
        if (org.RequireDeviceBinding && !string.IsNullOrWhiteSpace(r.DeviceId))
        {
            if (string.IsNullOrEmpty(user.DeviceId)) { user.DeviceId = r.DeviceId; user.DeviceModel = r.DeviceModel; await _db.SaveChangesAsync(); }
            else if (user.DeviceId != r.DeviceId)
                return Fail("DEVICE_MISMATCH", "This account is linked to a different phone. Ask your administrator to reset your device.");
        }

        // 3. Location integrity.
        if (r.IsMock && org.BlockMockLocation)
            return Fail("MOCK_LOCATION", "Mock/fake location detected. Turn off mock-location apps and try again.");
        if (r.Accuracy <= 0 || r.Accuracy > org.MaxAccuracyMeters)
            return Fail("LOW_ACCURACY", $"GPS accuracy is too low ({r.Accuracy:0} m). Move to an open area and retry (needs ≤ {org.MaxAccuracyMeters} m).");
        if (Math.Abs(r.Lat) > 90 || Math.Abs(r.Lng) > 180 || (r.Lat == 0 && r.Lng == 0))
            return Fail("BAD_LOCATION", "Invalid location.");

        // 4. Trusted time: online punches use the server clock; offline punches are bounded.
        var now = Clock.UtcNow;
        DateTime when = now;
        string? flag = null;
        if (r.Offline)
        {
            if (r.ClientTimeUtc is null) return Fail("BAD_TIME", "Offline punch missing timestamp.");
            when = DateTime.SpecifyKind(r.ClientTimeUtc.Value, DateTimeKind.Utc);
            if (when > now.AddMinutes(org.MaxClockDriftMinutes)) return Fail("CLOCK_DRIFT", "Phone clock is ahead of real time.");
            if (when < now.AddHours(-org.MaxOfflineSyncHours)) return Fail("STALE_PUNCH", $"Offline punch is older than {org.MaxOfflineSyncHours} h and was rejected.");
        }
        else if (r.ClientTimeUtc is { } ct && Math.Abs((ct - now).TotalMinutes) > org.MaxClockDriftMinutes)
            return Fail("CLOCK_DRIFT", "Your phone's date/time is incorrect. Enable automatic date & time and retry.");

        // 5. Geofence (authoritative, server side) against the user's permitted sites.
        var allowed = await AllowedSitesAsync(user);
        if (allowed.Count == 0) return Fail("NO_SITES", "No attendance location has been configured for you yet.");
        Site? best = null; double bestDist = double.MaxValue; Site? nearest = null; double nearestDist = double.MaxValue;
        foreach (var s in allowed)
        {
            var g = GeoService.Evaluate(s, r.Lat, r.Lng, org.GeofenceToleranceMeters);
            if (g.DistanceMeters < nearestDist) { nearest = s; nearestDist = g.DistanceMeters; }
            if (g.Inside && g.DistanceMeters < bestDist) { best = s; bestDist = g.DistanceMeters; }
        }
        if (best is null)
            return new PunchResult(false, "OUTSIDE_GEOFENCE",
                $"You are about {Math.Max(0, nearestDist):0} m outside {nearest!.Name}. Move inside the allowed area to {(isIn ? "sign in" : "sign out")}.",
                SiteName: nearest.Name, DistanceMeters: nearestDist);

        // 6. Optional selfie.
        string? selfiePath = null;
        if (org.RequireSelfie && string.IsNullOrWhiteSpace(r.SelfieBase64)) return Fail("SELFIE_REQUIRED", "A selfie is required.");
        if (!string.IsNullOrWhiteSpace(r.SelfieBase64))
        {
            selfiePath = await _selfies.SaveAsync(org.Id, user.Id, r.SelfieBase64);
            if (selfiePath is null && org.RequireSelfie) return Fail("BAD_SELFIE", "Selfie could not be read. Retake it.");
        }

        // 7. Impossible travel heuristic (flag only; admin reviews).
        var last = await _db.Attendance.Where(a => a.UserId == user.Id).OrderByDescending(a => a.CheckInUtc).FirstOrDefaultAsync();
        if (last is not null)
        {
            var (llat, llng, lt) = last.CheckOutUtc != null && last.OutLat != null
                ? (last.OutLat, last.OutLng, last.CheckOutUtc.Value) : (last.InLat, last.InLng, last.CheckInUtc);
            if (llat != null && llng != null && when > lt)
            {
                var dist = GeoService.Haversine(llat.Value, llng.Value, r.Lat, r.Lng);
                var secs = (when - lt).TotalSeconds;
                if (dist > 2000 && secs > 0 && dist / secs > ImpossibleSpeedMps)
                    flag = Join(flag, $"Impossible travel: {dist / 1000:0.0} km in {secs / 60:0} min");
            }
        }
        if (r.Accuracy < 1) flag = Join(flag, "Suspiciously perfect GPS accuracy");

        var local = OrgTime.ToLocal(when, org);
        var workDate = DateOnly.FromDateTime(local);

        return isIn
            ? await DoCheckIn(user, org, r, best, bestDist, when, local, workDate, flag, selfiePath)
            : await DoCheckOut(user, org, r, best, bestDist, when, local, flag, selfiePath);
    }

    private async Task<PunchResult> DoCheckIn(ApplicationUser user, Organization org, PunchRequest r, Site site, double dist,
        DateTime when, DateTime local, DateOnly workDate, string? flag, string? selfie)
    {
        var open = await _db.Attendance.Where(a => a.UserId == user.Id && a.CheckOutUtc == null).OrderByDescending(a => a.CheckInUtc).FirstOrDefaultAsync();
        if (open != null)
        {
            if ((when - open.CheckInUtc).TotalHours < 20)
                return new PunchResult(false, "ALREADY_CHECKED_IN", "You are already signed in. Sign out first.", open, site.Name, dist);
            await CloseStaleAsync(open, org, user);
        }

        var shift = await new StatsService(_db).ShiftFor(user, org);
        bool firstToday = !await _db.Attendance.AnyAsync(a => a.UserId == user.Id && a.WorkDate == workDate);
        bool late = false; int lateMin = 0;
        if (firstToday && shift.IsWorkDay(local.DayOfWeek))
        {
            var allowedBy = shift.StartTime + TimeSpan.FromMinutes(shift.GraceMinutes);
            if (local.TimeOfDay > allowedBy) { late = true; lateMin = (int)(local.TimeOfDay - shift.StartTime).TotalMinutes; }
        }

        var rec = new AttendanceRecord
        {
            OrganizationId = org.Id, UserId = user.Id, SiteId = site.Id, WorkDate = workDate, CheckInUtc = when,
            InLat = r.Lat, InLng = r.Lng, InAccuracy = r.Accuracy, InDistanceMeters = dist,
            IsLate = late, LateMinutes = lateMin, IsOfflineSync = r.Offline, DeviceId = r.DeviceId,
            InEventId = r.EventId, InSelfie = selfie, Source = PunchSource.Mobile,
            IsFlagged = flag != null,
            FlagReason = flag
        };
        _db.Attendance.Add(rec);
        await _db.SaveChangesAsync();
        return new PunchResult(true, "OK", late ? $"Signed in at {site.Name} — {lateMin} min late." : $"Signed in at {site.Name}.", rec, site.Name, dist);
    }

    private async Task<PunchResult> DoCheckOut(ApplicationUser user, Organization org, PunchRequest r, Site site, double dist,
        DateTime when, DateTime local, string? flag, string? selfie)
    {
        // Offline check-outs are matched to the session that was open at that time, not just the latest one.
        var open = await _db.Attendance.Where(a => a.UserId == user.Id && a.CheckOutUtc == null && a.CheckInUtc <= when)
            .OrderByDescending(a => a.CheckInUtc).FirstOrDefaultAsync();
        if (open is null) return Fail("NOT_CHECKED_IN", "You are not signed in.");

        var shift = await new StatsService(_db).ShiftFor(user, org);
        open.CheckOutUtc = when;
        open.OutLat = r.Lat; open.OutLng = r.Lng; open.OutAccuracy = r.Accuracy; open.OutDistanceMeters = dist;
        open.OutEventId = r.EventId; open.OutSelfie = selfie;
        open.IsEarlyLeave = shift.IsWorkDay(local.DayOfWeek) && local.TimeOfDay < shift.EndTime && shift.EndTime > shift.StartTime;
        if (flag != null)
        {
            open.FlagReason = Join(open.FlagReason, flag);
            open.IsFlagged = true;
        }
        await _db.SaveChangesAsync();
        var hrs = open.WorkedHours;
        return new PunchResult(true, "OK", $"Signed out. Worked {(int)hrs}h {(int)((hrs % 1) * 60)}m this session.", open, site.Name, dist);
    }

    private async Task CloseStaleAsync(AttendanceRecord open, Organization org, ApplicationUser user)
    {
        var shift = await new StatsService(_db).ShiftFor(user, org);
        var end = OrgTime.LocalToUtc(open.WorkDate, shift.EndTime, org);
        open.CheckOutUtc = end > open.CheckInUtc ? end : open.CheckInUtc;
        open.IsFlagged = true;
        open.FlagReason = Join(open.FlagReason, "Auto-closed: no sign-out recorded");
        open.Source = PunchSource.AutoClose;
        await _db.SaveChangesAsync();
    }

    /// <summary>Called by the nightly job: close sessions left open from previous days.</summary>
    public async Task<int> AutoCloseStaleAsync(Organization org)
    {
        if (!org.AutoCloseOpenSessions) return 0;
        var today = OrgTime.Today(org);
        var stale = await _db.Attendance.IgnoreQueryFilters()
            .Include(a => a.User)
            .Where(a => a.OrganizationId == org.Id && a.CheckOutUtc == null && a.WorkDate < today).ToListAsync();
        foreach (var a in stale) await CloseStaleAsync(a, org, a.User!);
        return stale.Count;
    }

    public async Task<List<Site>> AllowedSitesAsync(ApplicationUser user)
    {
        var assigned = await _db.UserSites.IgnoreQueryFilters().Where(us => us.UserId == user.Id).Select(us => us.SiteId).ToListAsync();
        var q = _db.Sites.IgnoreQueryFilters().Where(s => s.OrganizationId == user.OrganizationId && s.IsActive);
        if (assigned.Count > 0) q = q.Where(s => assigned.Contains(s.Id));
        return await q.AsNoTracking().ToListAsync();
    }

    private static PunchResult Fail(string code, string msg) => new(false, code, msg);
    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "; " + b;
}
