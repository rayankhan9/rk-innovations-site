using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers;

public class DashboardVm
{
    public int Members, PresentToday, LateToday, InsideNow, OnLeaveToday, AbsentSoFar;
    public bool IsWorkingDayToday;
    public int PendingUsers, PendingLeaves, PendingRegs, FlaggedWeek;
    public List<(string Label, int Count)> Trend = new();
    public List<(string Name, string Site, string Since)> Live = new();
    public List<(string Name, int Late)> TopLate = new();
    public string MonthLabel = "";
    public double MonthAvgAttendance;
}

public class DashboardController : AdminController
{
    private readonly StatsService _stats;
    public DashboardController(AppDbContext db, AuditService audit, StatsService stats) : base(db, audit) => _stats = stats;

    public async Task<IActionResult> Index()
    {
        var org = await GetOrg();
        var today = OrgTime.Today(org);
        var members = await _stats.Members(org).Select(u => new { u.Id, u.FullName, u.ShiftId }).ToListAsync();
        var ids = members.Select(m => m.Id).ToHashSet();
        var vm = new DashboardVm { Members = members.Count };

        var todays = await Db.Attendance.Include(a => a.User).Include(a => a.Site).AsNoTracking().Where(a => a.WorkDate == today).ToListAsync();
        vm.PresentToday = todays.Select(a => a.UserId).Distinct().Count(ids.Contains);
        vm.LateToday = todays.Where(a => a.IsLate).Select(a => a.UserId).Distinct().Count();
        var open = todays.Where(a => a.CheckOutUtc == null).ToList();
        vm.InsideNow = open.Count;
        vm.Live = open.OrderBy(a => a.CheckInUtc).Take(12)
            .Select(a => (a.User!.FullName, a.Site?.Name ?? "—", OrgTime.ToLocal(a.CheckInUtc, org).ToString("HH:mm"))).ToList();
        var onLeave = await Db.Leaves.AsNoTracking().Where(l => l.Status == RequestStatus.Approved && l.FromDate <= today && l.ToDate >= today).Select(l => l.UserId).Distinct().ToListAsync();
        vm.OnLeaveToday = onLeave.Count(ids.Contains);
        var holiday = await Db.Holidays.AnyAsync(h => h.Date == today);
        var defShift = org.DefaultShiftId == null ? null : await Db.Shifts.FindAsync(org.DefaultShiftId);
        vm.IsWorkingDayToday = !holiday && (defShift ?? new Shift()).IsWorkDay(today.DayOfWeek);
        vm.AbsentSoFar = vm.IsWorkingDayToday ? Math.Max(0, vm.Members - vm.PresentToday - vm.OnLeaveToday) : 0;

        vm.PendingUsers = await Db.Users.CountAsync(u => u.OrganizationId == org.Id && u.Status == UserStatus.Pending);
        vm.PendingLeaves = await Db.Leaves.CountAsync(l => l.Status == RequestStatus.Pending);
        vm.PendingRegs = await Db.Regularizations.CountAsync(r => r.Status == RequestStatus.Pending);
        vm.FlaggedWeek = await Db.Attendance.CountAsync(a => a.IsFlagged && a.WorkDate >= today.AddDays(-7));

        var from = today.AddDays(-13);
        var trendRecs = await Db.Attendance.AsNoTracking().Where(a => a.WorkDate >= from && a.WorkDate <= today).Select(a => new { a.WorkDate, a.UserId }).ToListAsync();
        for (var d = from; d <= today; d = d.AddDays(1))
            vm.Trend.Add((d.ToString("dd MMM"), trendRecs.Where(r => r.WorkDate == d).Select(r => r.UserId).Distinct().Count()));

        var monthRows = await _stats.GetOrgMonthAsync(org, today.Year, today.Month);
        vm.MonthLabel = new DateTime(today.Year, today.Month, 1).ToString("MMMM yyyy");
        vm.MonthAvgAttendance = monthRows.Count == 0 ? 0 : Math.Round(monthRows.Average(r => r.Summary.AttendancePercent), 1);
        vm.TopLate = monthRows.Where(r => r.Summary.LateDays > 0).OrderByDescending(r => r.Summary.LateDays).Take(5)
            .Select(r => (r.User.FullName, r.Summary.LateDays)).ToList();
        return View(vm);
    }
}
