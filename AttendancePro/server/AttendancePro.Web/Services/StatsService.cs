using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Services;

public enum DayStatus { Present, Late, HalfDay, Absent, Leave, Holiday, WeekOff, Upcoming, NotJoined }

public class DayInfo
{
    public DateOnly Date { get; set; }
    public DayStatus Status { get; set; }
    public DateTime? FirstInUtc { get; set; }
    public DateTime? LastOutUtc { get; set; }
    public double Hours { get; set; }
    public int LateMinutes { get; set; }
    public bool Flagged { get; set; }
    public string? Note { get; set; }
}

public class MonthSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int WorkingDays { get; set; }
    public int PresentDays { get; set; }
    public int LateDays { get; set; }
    public int HalfDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int Holidays { get; set; }
    public double TotalHours { get; set; }
    public double AvgHoursPerDay { get; set; }
    public double AttendancePercent { get; set; }
    public double PunctualityPercent { get; set; }
    public int CurrentStreak { get; set; }
    public int TotalLateMinutes { get; set; }
    public List<DayInfo> Days { get; set; } = new();
}

public class StatsService
{
    private readonly AppDbContext _db;
    public StatsService(AppDbContext db) => _db = db;

    public record Bundle(List<AttendanceRecord> Records, List<LeaveRequest> Leaves, Dictionary<DateOnly, string> Holidays);

    public async Task<Bundle> LoadAsync(Organization org, DateOnly from, DateOnly to, IEnumerable<string>? userIds = null)
    {
        var recQ = _db.Attendance.IgnoreQueryFilters().Where(a => a.OrganizationId == org.Id && a.WorkDate >= from && a.WorkDate <= to);
        var leaveQ = _db.Leaves.IgnoreQueryFilters().Where(l => l.OrganizationId == org.Id && l.Status == RequestStatus.Approved && l.FromDate <= to && l.ToDate >= from);
        var ids = userIds?.ToList();
        if (ids != null) { recQ = recQ.Where(a => ids.Contains(a.UserId)); leaveQ = leaveQ.Where(l => ids.Contains(l.UserId)); }
        var holidays = await _db.Holidays.IgnoreQueryFilters().Where(h => h.OrganizationId == org.Id && h.Date >= from && h.Date <= to)
            .ToDictionaryAsync(h => h.Date, h => h.Name);
        return new Bundle(await recQ.AsNoTracking().ToListAsync(), await leaveQ.AsNoTracking().ToListAsync(), holidays);
    }

    public static List<DayInfo> BuildDays(ApplicationUser user, Shift shift, Organization org, DateOnly from, DateOnly to, Bundle b)
    {
        var today = OrgTime.Today(org);
        var recs = b.Records.Where(r => r.UserId == user.Id).GroupBy(r => r.WorkDate)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.CheckInUtc).ToList());
        var leaves = b.Leaves.Where(l => l.UserId == user.Id).ToList();
        var joined = DateOnly.FromDateTime(OrgTime.ToLocal(user.CreatedUtc, org));
        var days = new List<DayInfo>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var info = new DayInfo { Date = d };
            recs.TryGetValue(d, out var dayRecs);
            bool isHoliday = b.Holidays.TryGetValue(d, out var hName);
            bool isWorkDay = shift.IsWorkDay(d.DayOfWeek);
            var leave = leaves.FirstOrDefault(l => l.FromDate <= d && l.ToDate >= d);

            if (dayRecs is { Count: > 0 })
            {
                info.FirstInUtc = dayRecs[0].CheckInUtc;
                info.LastOutUtc = dayRecs.Where(r => r.CheckOutUtc != null).Max(r => r.CheckOutUtc);
                info.Hours = Math.Round(dayRecs.Sum(r => r.WorkedHours), 2);
                info.LateMinutes = dayRecs[0].LateMinutes;
                info.Flagged = dayRecs.Any(r => r.IsFlagged);
                bool open = dayRecs.Any(r => r.CheckOutUtc == null);
                if (!open && info.Hours < shift.HalfDayHours) info.Status = DayStatus.HalfDay;
                else info.Status = dayRecs[0].IsLate ? DayStatus.Late : DayStatus.Present;
            }
            else if (d < joined) info.Status = DayStatus.NotJoined;
            else if (leave != null) { info.Status = DayStatus.Leave; info.Note = leave.Type.ToString(); }
            else if (isHoliday) { info.Status = DayStatus.Holiday; info.Note = hName; }
            else if (!isWorkDay) info.Status = DayStatus.WeekOff;
            else if (d >= today) info.Status = DayStatus.Upcoming;
            else info.Status = DayStatus.Absent;
            days.Add(info);
        }
        return days;
    }

    public static MonthSummary Summarize(List<DayInfo> days, int year, int month, int streak)
    {
        var s = new MonthSummary { Year = year, Month = month, Days = days, CurrentStreak = streak };
        foreach (var d in days)
        {
            switch (d.Status)
            {
                case DayStatus.Present: s.PresentDays++; s.WorkingDays++; break;
                case DayStatus.Late: s.PresentDays++; s.LateDays++; s.WorkingDays++; s.TotalLateMinutes += d.LateMinutes; break;
                case DayStatus.HalfDay:
                    s.HalfDays++; s.WorkingDays++;
                    if (d.LateMinutes > 0) { s.LateDays++; s.TotalLateMinutes += d.LateMinutes; }
                    break;
                case DayStatus.Absent: s.AbsentDays++; s.WorkingDays++; break;
                case DayStatus.Leave: s.LeaveDays++; break;
                case DayStatus.Holiday: s.Holidays++; break;
            }
            s.TotalHours += d.Hours;
        }
        s.TotalHours = Math.Round(s.TotalHours, 2);
        double attended = s.PresentDays + s.HalfDays * 0.5;
        int attendedDays = s.PresentDays + s.HalfDays;
        s.AvgHoursPerDay = attendedDays == 0 ? 0 : Math.Round(s.TotalHours / attendedDays, 2);
        s.AttendancePercent = s.WorkingDays == 0 ? 0 : Math.Round(100.0 * attended / s.WorkingDays, 1);
        s.PunctualityPercent = attendedDays == 0 ? 0 : Math.Round(100.0 * (attendedDays - s.LateDays) / attendedDays, 1);
        return s;
    }

    /// <summary>Consecutive attended working days ending today (off days and leave don't break it).</summary>
    public static int Streak(List<DayInfo> days, DateOnly today)
    {
        int n = 0;
        foreach (var d in days.Where(d => d.Date <= today).OrderByDescending(d => d.Date))
        {
            if (d.Status is DayStatus.Present or DayStatus.Late or DayStatus.HalfDay) n++;
            else if (d.Status is DayStatus.Holiday or DayStatus.WeekOff or DayStatus.Leave) continue;
            else if (d.Status == DayStatus.Upcoming && d.Date == today) continue;
            else break;
        }
        return n;
    }

    public async Task<Shift> ShiftFor(ApplicationUser u, Organization org)
    {
        var id = u.ShiftId ?? org.DefaultShiftId;
        Shift? s = id == null ? null : await _db.Shifts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == org.Id);
        return s ?? new Shift { Name = "Default", OrganizationId = org.Id };
    }

    public async Task<MonthSummary> GetUserMonthAsync(ApplicationUser user, Organization org, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var today = OrgTime.Today(org);
        var shift = await ShiftFor(user, org);
        var lookback = today.AddDays(-90);
        var bundle = await LoadAsync(org, lookback < from ? lookback : from, to > today ? to : today, new[] { user.Id });
        var days = BuildDays(user, shift, org, from, to, bundle);
        var streakDays = BuildDays(user, shift, org, lookback, today, bundle);
        return Summarize(days, year, month, Streak(streakDays, today));
    }

    /// <summary>Everyone who is expected to punch: active, and not a pure admin account without an employee code.</summary>
    public IQueryable<ApplicationUser> Members(Organization org) =>
        _db.Users.Where(u => u.OrganizationId == org.Id && u.Status == UserStatus.Active && (!u.IsAdmin || u.EmployeeCode != null));

    public async Task<List<(ApplicationUser User, MonthSummary Summary)>> GetOrgMonthAsync(Organization org, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var users = await Members(org).OrderBy(u => u.FullName).AsNoTracking().ToListAsync();
        var bundle = await LoadAsync(org, from, to);
        var shifts = await _db.Shifts.IgnoreQueryFilters().Where(s => s.OrganizationId == org.Id).ToDictionaryAsync(s => s.Id);
        var result = new List<(ApplicationUser, MonthSummary)>();
        foreach (var u in users)
        {
            var id = u.ShiftId ?? org.DefaultShiftId;
            var shift = id != null && shifts.TryGetValue(id.Value, out var sh) ? sh : new Shift { Name = "Default" };
            result.Add((u, Summarize(BuildDays(u, shift, org, from, to, bundle), year, month, 0)));
        }
        return result;
    }
}
