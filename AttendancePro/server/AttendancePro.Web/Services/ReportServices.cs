using System.Globalization;
using System.Net;
using System.Text;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Services;

public static class ReportBuilder
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string MonthName(int y, int m) => new DateTime(y, m, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private static string Shell(Organization org, string title, string body) => $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:auto;color:#1f2937'>
  <div style='background:#0f766e;color:#fff;padding:20px 24px;border-radius:10px 10px 0 0'>
    <div style='font-size:13px;opacity:.85'>{E(org.Name)}</div>
    <div style='font-size:22px;font-weight:600'>{E(title)}</div>
  </div>
  <div style='border:1px solid #e5e7eb;border-top:0;padding:24px;border-radius:0 0 10px 10px'>{body}</div>
  <p style='font-size:11px;color:#9ca3af;text-align:center'>Sent automatically by AttendancePro.</p>
</div>";

    private static string Kpi(string label, string value, string color = "#0f766e") =>
        $"<td style='padding:10px;text-align:center;background:#f9fafb;border-radius:8px'><div style='font-size:22px;font-weight:700;color:{color}'>{E(value)}</div><div style='font-size:11px;color:#6b7280'>{E(label)}</div></td>";

    public static string UserReport(Organization org, ApplicationUser u, MonthSummary s)
    {
        var sb = new StringBuilder();
        sb.Append($"<p>Hi {E(u.FullName)},</p><p>Here is your attendance summary for <b>{MonthName(s.Year, s.Month)}</b>.</p>");
        sb.Append("<table width='100%' cellspacing='6'><tr>");
        sb.Append(Kpi("Attendance", $"{s.AttendancePercent:0.#}%"));
        sb.Append(Kpi("Present days", s.PresentDays.ToString()));
        sb.Append(Kpi("Absent", s.AbsentDays.ToString(), s.AbsentDays > 0 ? "#b91c1c" : "#0f766e"));
        sb.Append("</tr><tr>");
        sb.Append(Kpi("Late arrivals", s.LateDays.ToString(), s.LateDays > 0 ? "#b45309" : "#0f766e"));
        sb.Append(Kpi("Total hours", $"{s.TotalHours:0.#}"));
        sb.Append(Kpi("Avg hours/day", $"{s.AvgHoursPerDay:0.#}"));
        sb.Append("</tr></table>");
        sb.Append($"<p style='font-size:13px'>Punctuality: <b>{s.PunctualityPercent:0.#}%</b> · Half days: <b>{s.HalfDays}</b> · Leave days: <b>{s.LeaveDays}</b> · Holidays: <b>{s.Holidays}</b></p>");
        var absent = s.Days.Where(d => d.Status == DayStatus.Absent).Select(d => d.Date.ToString("dd MMM")).ToList();
        if (absent.Count > 0) sb.Append($"<p style='font-size:13px;color:#b91c1c'>Absent on: {E(string.Join(", ", absent))}. If any of these are wrong, raise a regularization request in the app.</p>");
        else sb.Append("<p style='font-size:13px;color:#047857'>No absences this month. Great job!</p>");
        sb.Append("<table width='100%' cellspacing='0' style='font-size:12px;border-collapse:collapse'><tr style='background:#f3f4f6'><th align='left' style='padding:6px'>Date</th><th align='left'>Status</th><th align='right'>Hours</th></tr>");
        foreach (var d in s.Days.Where(d => d.Status is DayStatus.Present or DayStatus.Late or DayStatus.HalfDay or DayStatus.Absent or DayStatus.Leave))
            sb.Append($"<tr style='border-top:1px solid #e5e7eb'><td style='padding:5px 6px'>{d.Date:ddd dd MMM}</td><td>{d.Status}{(d.Status == DayStatus.Late ? $" ({d.LateMinutes}m)" : "")}</td><td align='right'>{d.Hours:0.0}</td></tr>");
        sb.Append("</table>");
        return Shell(org, $"Attendance report — {MonthName(s.Year, s.Month)}", sb.ToString());
    }

    public static string AdminSummary(Organization org, int year, int month, List<(ApplicationUser User, MonthSummary S)> rows)
    {
        var sb = new StringBuilder();
        double avg = rows.Count == 0 ? 0 : rows.Average(r => r.S.AttendancePercent);
        sb.Append($"<p>Organization summary for <b>{MonthName(year, month)}</b> — {rows.Count} active members, average attendance <b>{avg:0.#}%</b>.</p>");
        sb.Append("<table width='100%' cellspacing='0' style='font-size:12px;border-collapse:collapse'><tr style='background:#f3f4f6'><th align='left' style='padding:6px'>Name</th><th>Present</th><th>Absent</th><th>Late</th><th>Hours</th><th>Att %</th></tr>");
        foreach (var r in rows.OrderBy(r => r.S.AttendancePercent).Take(25))
            sb.Append($"<tr style='border-top:1px solid #e5e7eb'><td style='padding:5px 6px'>{E(r.User.FullName)}</td><td align='center'>{r.S.PresentDays}</td><td align='center'>{r.S.AbsentDays}</td><td align='center'>{r.S.LateDays}</td><td align='center'>{r.S.TotalHours:0.#}</td><td align='center'>{r.S.AttendancePercent:0.#}</td></tr>");
        sb.Append("</table><p style='font-size:12px;color:#6b7280'>Lowest 25 attendance shown. Open the dashboard for the complete report and CSV export.</p>");
        return Shell(org, $"Admin summary — {MonthName(year, month)}", sb.ToString());
    }

    public static string Csv(List<(ApplicationUser User, MonthSummary S)> rows)
    {
        var sb = new StringBuilder("Name,EmployeeCode,Department,WorkingDays,Present,HalfDays,Absent,Late,LateMinutes,Leave,Holidays,TotalHours,AvgHours,AttendancePct,PunctualityPct\n");
        foreach (var (u, s) in rows)
            sb.AppendLine(string.Join(",", Q(u.FullName), Q(u.EmployeeCode), Q(u.Department), s.WorkingDays, s.PresentDays, s.HalfDays, s.AbsentDays,
                s.LateDays, s.TotalLateMinutes, s.LeaveDays, s.Holidays, s.TotalHours.ToString(CultureInfo.InvariantCulture),
                s.AvgHoursPerDay.ToString(CultureInfo.InvariantCulture), s.AttendancePercent.ToString(CultureInfo.InvariantCulture),
                s.PunctualityPercent.ToString(CultureInfo.InvariantCulture)));
        return sb.ToString();
    }

    /// <summary>CSV-quote and neutralise spreadsheet formula injection.</summary>
    public static string Q(string? v)
    {
        v ??= "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}

public class MonthlyReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MonthlyReportService> _log;
    public MonthlyReportService(IServiceScopeFactory scopes, ILogger<MonthlyReportService> log) { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Scheduled job failed"); }
            await Task.Delay(TimeSpan.FromMinutes(30), ct);
        }
    }

    /// <summary>
    /// Runs every 30 min. Per organization: closes stale sessions, and on the 1st of a month (org local time)
    /// sends last month's reports exactly once (tracked in ReportLog, so restarts never duplicate).
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var att = scope.ServiceProvider.GetRequiredService<AttendanceService>();
        var reports = scope.ServiceProvider.GetRequiredService<MonthlyReports>();
        foreach (var org in await db.Organizations.AsNoTracking().ToListAsync(ct))
        {
            await att.AutoCloseStaleAsync(org);
            var today = OrgTime.Today(org);
            if (today.Day < 1) continue;
            var prev = today.AddMonths(-1);
            // Send from the 1st (any time after midnight local); skip if already done.
            await reports.SendForMonthAsync(org, prev.Year, prev.Month, force: false, ct);
        }
    }
}

public class MonthlyReports
{
    private readonly AppDbContext _db;
    private readonly StatsService _stats;
    private readonly IMailSender _mail;
    private readonly ILogger<MonthlyReports> _log;
    public MonthlyReports(AppDbContext db, StatsService stats, IMailSender mail, ILogger<MonthlyReports> log)
    { _db = db; _stats = stats; _mail = mail; _log = log; }

    public async Task<int> SendForMonthAsync(Organization org, int year, int month, bool force, CancellationToken ct = default)
    {
        if (!org.SendMonthlyReports) return 0;
        var monthEnd = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        if (org.CreatedUtc >= monthEnd) return 0; // organization didn't exist yet — nothing to report
        var alreadySent = await _db.ReportLogs.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == org.Id && r.Year == year && r.Month == month && r.Success)
            .Select(r => r.UserId ?? "__admin__").ToListAsync(ct);
        var done = alreadySent.ToHashSet();
        var rows = await _stats.GetOrgMonthAsync(org, year, month);
        int sent = 0;
        foreach (var (u, s) in rows)
        {
            if (!u.ReceiveMonthlyReport || string.IsNullOrEmpty(u.Email) || u.CreatedUtc >= monthEnd) continue;
            if (!force && done.Contains(u.Id)) continue;
            sent += await TrySend(org, u.Id, u.Email, $"Your attendance report — {new DateTime(year, month, 1):MMMM yyyy}", ReportBuilder.UserReport(org, u, s), year, month, ct) ? 1 : 0;
        }
        if (org.SendAdminMonthlySummary && (force || !done.Contains("__admin__")))
        {
            var admins = await _db.Users.Where(u => u.OrganizationId == org.Id && u.IsAdmin && u.Status == UserStatus.Active && u.Email != null).ToListAsync(ct);
            var html = ReportBuilder.AdminSummary(org, year, month, rows);
            bool ok = false;
            foreach (var a in admins) ok |= await TrySend(org, null, a.Email!, $"Admin summary — {new DateTime(year, month, 1):MMMM yyyy}", html, year, month, ct, log: false);
            if (admins.Count > 0) { _db.ReportLogs.Add(new ReportLog { OrganizationId = org.Id, UserId = null, Year = year, Month = month, Success = ok }); await _db.SaveChangesAsync(ct); }
        }
        return sent;
    }

    private async Task<bool> TrySend(Organization org, string? userId, string to, string subject, string html, int y, int m, CancellationToken ct, bool log = true)
    {
        try
        {
            await _mail.SendAsync(to, subject, html, ct);
            if (log) _db.ReportLogs.Add(new ReportLog { OrganizationId = org.Id, UserId = userId, Year = y, Month = m, Success = true });
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Report email to {To} failed", to);
            if (log) _db.ReportLogs.Add(new ReportLog { OrganizationId = org.Id, UserId = userId, Year = y, Month = m, Success = false, Error = ex.Message.Length > 280 ? ex.Message[..280] : ex.Message });
            await _db.SaveChangesAsync(ct);
            return false;
        }
    }
}
