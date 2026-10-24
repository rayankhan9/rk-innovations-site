using System.Text.Json;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Data;

/// <summary>
/// Realistic sample organization ("Greenfield Institute") for demos, QA and store screenshots.
/// Enabled only with Seed:SampleData=true. 24 members, 45 days of history, leave, corrections,
/// flagged punches, holidays, audit trail. Deterministic (fixed random seed).
/// </summary>
public static class DemoData
{
    public const string OrgCode = "GREEN1";
    public const string AdminEmail = "admin@greenfield.edu";
    public const string AdminPassword = "Admin#12345";
    public const string PersonaEmail = "priya.sharma@greenfield.edu";
    public const string MemberPassword = "Demo#12345";

    private const double CampusLat = 30.7590, CampusLng = 76.7948;

    private record Person(string Name, string Code, string Dept, string Shift, string Site, double Attend, double Late, string Title);

    private static readonly Person[] People =
    {
        new("Priya Sharma", "GF-1001", "Computer Science", "Faculty", "Main", .98, .06, "Assistant Professor"),
        new("Arjun Mehta", "GF-1002", "Computer Science", "Faculty", "Main", .94, .12, "Lecturer"),
        new("Neha Kapoor", "GF-1003", "Computer Science", "Faculty", "Main", .96, .08, "Lab Instructor"),
        new("Rahul Verma", "GF-1004", "Mathematics", "Faculty", "Main", .91, .18, "Associate Professor"),
        new("Ananya Rao", "GF-1005", "Mathematics", "Faculty", "Main", .97, .05, "Lecturer"),
        new("Imran Qureshi", "GF-1006", "Physics", "Faculty", "Main", .93, .10, "Professor"),
        new("Kavita Nair", "GF-1007", "Physics", "Faculty", "Main", .95, .07, "Lecturer"),
        new("Aisha Siddiqui", "GF-1008", "Chemistry", "Faculty", "Main", .96, .06, "Assistant Professor"),
        new("Manoj Patel", "GF-1009", "Chemistry", "Faculty", "Main", .88, .22, "Lab Technician"),
        new("Sneha Reddy", "GF-1010", "Biology", "Faculty", "Main", .97, .04, "Assistant Professor"),
        new("Karan Malhotra", "GF-1011", "Biology", "Faculty", "Main", .90, .15, "Lecturer"),
        new("Deepa Menon", "GF-1012", "English", "Faculty", "Main", .95, .09, "Associate Professor"),
        new("Harish Gupta", "GF-1013", "English", "Faculty", "Main", .92, .14, "Lecturer"),
        new("Dr. Meera Iyer", "GF-1000", "Administration", "Admin", "Main", .99, .02, "Registrar"),
        new("Sandeep Singh", "GF-1101", "Administration", "Admin", "Main", .96, .07, "Office Manager"),
        new("Fatima Khan", "GF-1102", "Administration", "Admin", "Main", .97, .05, "Admissions Officer"),
        new("Pooja Bansal", "GF-1103", "Accounts", "Admin", "Main", .98, .04, "Accountant"),
        new("Amit Chauhan", "GF-1104", "Accounts", "Admin", "Main", .93, .11, "Accounts Assistant"),
        new("Zoya Ali", "GF-1105", "IT Support", "Admin", "Main", .96, .06, "Systems Engineer"),
        new("Nikhil Bose", "GF-1106", "IT Support", "Admin", "Main", .94, .10, "Support Technician"),
        new("Rohan Das", "GF-1107", "Facilities", "Admin", "Main", .90, .13, "Facilities Supervisor"),
        new("Lakshmi Iyer", "GF-1201", "Library", "Library", "Annex", .97, .05, "Head Librarian"),
        new("Vikram Joshi", "GF-1202", "Library", "Library", "Annex", .94, .09, "Library Assistant"),
        new("Ritu Agarwal", "GF-1301", "Sports", "Admin", "Sports", .95, .08, "Sports Coordinator"),
        new("Suresh Kumar", "GF-1302", "Sports", "Admin", "Sports", .92, .12, "Ground Staff"),
    };

    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var um = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Code == OrgCode);
        if (org is null)
        {
            org = await Create(db, um);
        }
        await RefreshToday(db, org);
    }

    private static async Task<Organization> Create(AppDbContext db, UserManager<ApplicationUser> um)
    {
        var rnd = new Random(2026);
        var org = new Organization
        {
            Name = "Greenfield Institute", Code = OrgCode, Type = OrgType.Education, TimeZoneId = "Asia/Kolkata",
            ContactEmail = AdminEmail, RequireApproval = true, RequireDeviceBinding = true, BlockMockLocation = true,
            MaxAccuracyMeters = 50, MaxClockDriftMinutes = 100000, // huge: demo clock may be offset from the phone's
            CreatedUtc = Clock.UtcNow.AddDays(-120)
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        // ---- shifts
        var shifts = new Dictionary<string, Shift>
        {
            ["Faculty"] = new() { OrganizationId = org.Id, Name = "Faculty", StartTime = new(9, 30, 0), EndTime = new(17, 30, 0), GraceMinutes = 15, HalfDayHours = 4, WorkDaysMask = 2 | 4 | 8 | 16 | 32 },
            ["Admin"] = new() { OrganizationId = org.Id, Name = "Administration", StartTime = new(9, 0, 0), EndTime = new(17, 0, 0), GraceMinutes = 10, HalfDayHours = 4, WorkDaysMask = 2 | 4 | 8 | 16 | 32 },
            ["Library"] = new() { OrganizationId = org.Id, Name = "Library", StartTime = new(10, 0, 0), EndTime = new(18, 0, 0), GraceMinutes = 10, HalfDayHours = 4, WorkDaysMask = 2 | 4 | 8 | 16 | 32 | 64 },
        };
        db.Shifts.AddRange(shifts.Values);
        await db.SaveChangesAsync();
        org.DefaultShiftId = shifts["Faculty"].Id;

        // ---- sites
        var sites = new Dictionary<string, Site>
        {
            ["Main"] = new() { OrganizationId = org.Id, Name = "Main Campus", Address = "Academic block, labs and administration", Shape = SiteShape.Circle, CenterLat = CampusLat, CenterLng = CampusLng, RadiusMeters = 450 },
            ["Annex"] = new() { OrganizationId = org.Id, Name = "Library Annex", Address = "Central library building", Shape = SiteShape.Circle, CenterLat = CampusLat - .0032, CenterLng = CampusLng - .0021, RadiusMeters = 110 },
            ["Sports"] = new()
            {
                OrganizationId = org.Id, Name = "Sports Complex", Address = "Grounds, courts and pavilion", Shape = SiteShape.Polygon,
                CenterLat = CampusLat - .0058, CenterLng = CampusLng + .0036,
                PolygonJson = JsonSerializer.Serialize(new[] { new[] { CampusLat - .0050, CampusLng + .0024 }, new[] { CampusLat - .0050, CampusLng + .0050 }, new[] { CampusLat - .0068, CampusLng + .0052 }, new[] { CampusLat - .0070, CampusLng + .0026 } })
            },
        };
        db.Sites.AddRange(sites.Values);
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();

        // ---- holidays
        foreach (var (d, n) in new[] { (new DateOnly(2026, 8, 15), "Independence Day"), (new DateOnly(2026, 10, 2), "Gandhi Jayanti"), (new DateOnly(2026, 10, 20), "Dussehra"),
                                       (new DateOnly(2026, 11, 8), "Diwali"), (new DateOnly(2026, 11, 9), "Diwali (day 2)"), (new DateOnly(2026, 11, 24), "Guru Nanak Jayanti"), (new DateOnly(2026, 12, 25), "Christmas Day") })
            db.Holidays.Add(new Holiday { OrganizationId = org.Id, Date = d, Name = n });
        await db.SaveChangesAsync();
        var holidays = new HashSet<DateOnly>(await db.Holidays.IgnoreQueryFilters().Where(h => h.OrganizationId == org.Id).Select(h => h.Date).ToListAsync());

        // ---- admin
        var admin = new ApplicationUser
        {
            UserName = AdminEmail, Email = AdminEmail, EmailConfirmed = true, FullName = "Dr. Meera Iyer", OrganizationId = org.Id, IsAdmin = true,
            Status = UserStatus.Active, Department = "Administration", Designation = "Registrar", ShiftId = shifts["Admin"].Id
        };
        await um.CreateAsync(admin, AdminPassword);
        await um.AddToRoleAsync(admin, "Admin");

        // ---- members
        var users = new Dictionary<string, ApplicationUser>();
        foreach (var p in People.Where(p => p.Code != "GF-1000"))
        {
            var u = new ApplicationUser
            {
                UserName = Email(p.Name), Email = Email(p.Name), EmailConfirmed = true, FullName = p.Name, OrganizationId = org.Id,
                EmployeeCode = p.Code, Department = p.Dept, Designation = p.Title, Status = UserStatus.Active, ShiftId = shifts[p.Shift].Id,
                CreatedUtc = Clock.UtcNow.AddDays(-90 - rnd.Next(0, 25)),
                LastLoginUtc = Clock.UtcNow.AddHours(-rnd.Next(1, 70))
            };
            var r = await um.CreateAsync(u, MemberPassword);
            if (!r.Succeeded) throw new InvalidOperationException(string.Join(";", r.Errors.Select(e => e.Description)));
            users[p.Code] = u;
            if (p.Site != "Main" || p.Code == "GF-1001")
                db.UserSites.Add(new UserSite { OrganizationId = org.Id, UserId = u.Id, SiteId = sites[p.Site].Id });
        }
        // two sign-ups waiting for approval
        foreach (var (n, c, d) in new[] { ("Tanvi Deshmukh", "GF-1401", "Biology"), ("Omar Farooq", "GF-1402", "Physics") })
            await um.CreateAsync(new ApplicationUser
            {
                UserName = Email(n), Email = Email(n), EmailConfirmed = true, FullName = n, OrganizationId = org.Id, EmployeeCode = c, Department = d,
                Status = UserStatus.Pending, ShiftId = shifts["Faculty"].Id, CreatedUtc = Clock.UtcNow.AddHours(-9)
            }, MemberPassword);
        await db.SaveChangesAsync();

        // ---- 45 days of history (everything before today)
        var today = OrgTime.Today(org);
        var leaveDays = new Dictionary<string, HashSet<DateOnly>>();

        void AddLeave(string code, LeaveType t, DateOnly from, DateOnly to, string reason, RequestStatus st, string? note = null)
        {
            db.Leaves.Add(new LeaveRequest { OrganizationId = org.Id, UserId = users[code].Id, Type = t, FromDate = from, ToDate = to, Reason = reason, Status = st, ReviewedBy = st == RequestStatus.Pending ? null : "Dr. Meera Iyer", ReviewedUtc = st == RequestStatus.Pending ? null : Clock.UtcNow.AddDays(-3), ReviewNote = note, CreatedUtc = Clock.UtcNow.AddDays(-5) });
            if (st == RequestStatus.Approved)
                for (var d = from; d <= to; d = d.AddDays(1)) { if (!leaveDays.TryGetValue(code, out var set)) leaveDays[code] = set = new(); set.Add(d); }
        }
        AddLeave("GF-1001", LeaveType.Casual, new(2026, 10, 9), new(2026, 10, 9), "Family function", RequestStatus.Approved, "Approved. Enjoy!");
        AddLeave("GF-1001", LeaveType.Annual, new(2026, 9, 21), new(2026, 9, 22), "Conference travel", RequestStatus.Approved);
        AddLeave("GF-1004", LeaveType.Sick, new(2026, 10, 13), new(2026, 10, 15), "Viral fever", RequestStatus.Approved);
        AddLeave("GF-1010", LeaveType.Annual, new(2026, 11, 12), new(2026, 11, 13), "Family trip", RequestStatus.Approved);
        AddLeave("GF-1103", LeaveType.Casual, new(2026, 10, 6), new(2026, 10, 6), "Personal work", RequestStatus.Approved);
        AddLeave("GF-1013", LeaveType.Casual, new(2026, 10, 14), new(2026, 10, 14), "Personal errand", RequestStatus.Rejected, "Exam invigilation duty that day.");
        AddLeave("GF-1002", LeaveType.Sick, today.AddDays(1), today.AddDays(2), "Dental procedure", RequestStatus.Pending);
        AddLeave("GF-1105", LeaveType.Annual, today.AddDays(8), today.AddDays(12), "Wedding in family", RequestStatus.Pending);
        AddLeave("GF-1011", LeaveType.Casual, today.AddDays(5), today.AddDays(5), "Bank work", RequestStatus.Pending);

        var siteByKey = sites;
        for (var d = today.AddDays(-45); d < today; d = d.AddDays(1))
        {
            if (holidays.Contains(d)) continue;
            foreach (var p in People.Where(p => p.Code != "GF-1000"))
            {
                var u = users[p.Code]; var sh = shifts[p.Shift];
                if (!sh.IsWorkDay(d.DayOfWeek)) continue;
                if (leaveDays.TryGetValue(p.Code, out var ld) && ld.Contains(d)) continue;
                if (rnd.NextDouble() > p.Attend) continue;
                // Rohan's missing sign-out two days ago is added below as a flagged record
                if (p.Code == "GF-1107" && d == today.AddDays(-2)) continue;
                AddDay(db, org, u, sh, siteByKey[p.Site], d, p, rnd);
            }
        }
        await db.SaveChangesAsync();

        // ---- hand-crafted review cases
        var rohan = users["GF-1107"]; var karan = users["GF-1011"]; var suresh = users["GF-1302"];
        var rd = today.AddDays(-2);
        db.Attendance.Add(new AttendanceRecord
        {
            OrganizationId = org.Id, UserId = rohan.Id, SiteId = sites["Main"].Id, WorkDate = rd, CheckInUtc = OrgTime.LocalToUtc(rd, new(8, 58, 0), org),
            CheckOutUtc = OrgTime.LocalToUtc(rd, new(17, 0, 0), org), InLat = CampusLat + .0004, InLng = CampusLng - .0003, InAccuracy = 9, InDistanceMeters = -380,
            IsFlagged = true, FlagReason = "Auto-closed: no sign-out recorded", Source = PunchSource.AutoClose
        });
        var kd = today.AddDays(-4);
        db.Attendance.Add(new AttendanceRecord
        {
            OrganizationId = org.Id, UserId = karan.Id, SiteId = sites["Main"].Id, WorkDate = kd, CheckInUtc = OrgTime.LocalToUtc(kd, new(9, 41, 0), org),
            CheckOutUtc = OrgTime.LocalToUtc(kd, new(17, 36, 0), org), InLat = CampusLat + .0011, InLng = CampusLng + .0008, InAccuracy = 14, InDistanceMeters = -310,
            IsLate = true, LateMinutes = 11, IsFlagged = true, FlagReason = "Impossible travel: 1184.0 km in 4 min", Source = PunchSource.Mobile
        });
        var sd = today.AddDays(-1);
        db.Attendance.Add(new AttendanceRecord
        {
            OrganizationId = org.Id, UserId = suresh.Id, SiteId = sites["Sports"].Id, WorkDate = sd, CheckInUtc = OrgTime.LocalToUtc(sd, new(8, 55, 0), org),
            CheckOutUtc = OrgTime.LocalToUtc(sd, new(17, 4, 0), org), InLat = CampusLat - .0058, InLng = CampusLng + .0036, InAccuracy = 11, InDistanceMeters = -40, IsOfflineSync = true
        });

        // ---- corrections
        db.Regularizations.AddRange(
            new RegularizationRequest { OrganizationId = org.Id, UserId = rohan.Id, Date = rd, RequestedOut = new(17, 5, 0), Reason = "Forgot to sign out — left at 5:05 PM.", Status = RequestStatus.Pending, CreatedUtc = Clock.UtcNow.AddHours(-20) },
            new RegularizationRequest { OrganizationId = org.Id, UserId = users["GF-1012"].Id, Date = today.AddDays(-3), RequestedIn = new(9, 20, 0), Reason = "Phone battery died; I was on campus from 9:20.", Status = RequestStatus.Pending, CreatedUtc = Clock.UtcNow.AddHours(-30) },
            new RegularizationRequest { OrganizationId = org.Id, UserId = users["GF-1106"].Id, Date = today.AddDays(-9), RequestedOut = new(17, 10, 0), Reason = "Network outage at the gate.", Status = RequestStatus.Approved, ReviewedBy = "Dr. Meera Iyer", ReviewedUtc = Clock.UtcNow.AddDays(-8), ReviewNote = "OK", CreatedUtc = Clock.UtcNow.AddDays(-9) });

        // ---- audit trail + last month's report log
        var actions = new (int hAgo, string a, string d)[]
        {
            (2, "user.status", "Tanvi Deshmukh → Pending (self sign-up)"), (6, "leave.approved", "Priya Sharma 2026-10-09→2026-10-09"), (27, "regularization.approved", "Nikhil Bose 2026-10-16"),
            (50, "site.save", "Library Annex (Circle, 110 m)"), (74, "shift.save", "Library (10:00–18:00, Mon–Sat)"), (120, "user.device_reset", "Aisha Siddiqui"),
            (200, "settings.update", "Organization settings changed"), (310, "export.monthly", "2026-09"), (330, "report.send", "2026-09 sent=24 force=False"),
        };
        foreach (var (h, a, dtl) in actions)
            db.AuditLogs.Add(new AuditLog { OrganizationId = org.Id, AtUtc = Clock.UtcNow.AddHours(-h), ActorId = admin.Id, ActorName = admin.FullName, Action = a, Details = dtl, Ip = "10.0.0.12" });
        foreach (var u in users.Values)
            db.ReportLogs.Add(new ReportLog { OrganizationId = org.Id, UserId = u.Id, Year = 2026, Month = 9, Success = true, SentUtc = Clock.UtcNow.AddDays(-24) });
        db.ReportLogs.Add(new ReportLog { OrganizationId = org.Id, UserId = null, Year = 2026, Month = 9, Success = true, SentUtc = Clock.UtcNow.AddDays(-24) });
        await db.SaveChangesAsync();
        return org;
    }

    /// <summary>Re-creates today's punches relative to the (possibly staged) current time. Priya is left untouched for live demos.</summary>
    private static async Task RefreshToday(AppDbContext db, Organization org)
    {
        var today = OrgTime.Today(org);
        var nowLocal = OrgTime.ToLocal(Clock.UtcNow, org).TimeOfDay;
        var old = await db.Attendance.IgnoreQueryFilters().Include(a => a.User)
            .Where(a => a.OrganizationId == org.Id && a.WorkDate == today && a.User!.EmployeeCode != "GF-1001").ToListAsync();
        db.Attendance.RemoveRange(old);
        await db.SaveChangesAsync();

        var rnd = new Random(today.DayNumber);
        var holiday = await db.Holidays.IgnoreQueryFilters().AnyAsync(h => h.OrganizationId == org.Id && h.Date == today);
        if (holiday) return;
        var shifts = await db.Shifts.IgnoreQueryFilters().Where(s => s.OrganizationId == org.Id).ToDictionaryAsync(s => s.Id);
        var sites = await db.Sites.IgnoreQueryFilters().Where(s => s.OrganizationId == org.Id).ToListAsync();
        var onLeave = (await db.Leaves.IgnoreQueryFilters().Where(l => l.OrganizationId == org.Id && l.Status == RequestStatus.Approved && l.FromDate <= today && l.ToDate >= today).Select(l => l.UserId).ToListAsync()).ToHashSet();
        var members = await db.Users.Where(u => u.OrganizationId == org.Id && u.Status == UserStatus.Active && !u.IsAdmin && u.EmployeeCode != "GF-1001").ToListAsync();
        var siteMap = await db.UserSites.IgnoreQueryFilters().Where(s => s.OrganizationId == org.Id).ToListAsync();

        foreach (var u in members)
        {
            var sh = shifts[u.ShiftId!.Value];
            if (!sh.IsWorkDay(today.DayOfWeek) || onLeave.Contains(u.Id)) continue;
            var p = People.First(x => x.Code == u.EmployeeCode);
            if (rnd.NextDouble() > p.Attend) continue;
            var inT = sh.StartTime + TimeSpan.FromMinutes(rnd.Next(-22, 6) + (rnd.NextDouble() < p.Late ? rnd.Next(sh.GraceMinutes + 3, 40) : 0));
            if (inT > nowLocal) continue; // hasn't arrived yet at the staged time
            var assigned = siteMap.FirstOrDefault(s => s.UserId == u.Id)?.SiteId;
            var site = sites.First(s => s.Id == assigned || (assigned == null && s.Name == "Main Campus"));
            var late = inT > sh.StartTime + TimeSpan.FromMinutes(sh.GraceMinutes);
            var (lat, lng) = Jitter(site, rnd);
            db.Attendance.Add(new AttendanceRecord
            {
                OrganizationId = org.Id, UserId = u.Id, SiteId = site.Id, WorkDate = today, CheckInUtc = OrgTime.LocalToUtc(today, inT, org),
                InLat = lat, InLng = lng, InAccuracy = 5 + rnd.Next(0, 14), InDistanceMeters = -(20 + rnd.Next(0, 200)),
                IsLate = late, LateMinutes = late ? (int)(inT - sh.StartTime).TotalMinutes : 0, Source = PunchSource.Mobile
            });
        }
        await db.SaveChangesAsync();
    }

    private static void AddDay(AppDbContext db, Organization org, ApplicationUser u, Shift sh, Site site, DateOnly d, Person p, Random rnd)
    {
        var lateDay = rnd.NextDouble() < p.Late;
        var inMin = rnd.Next(-24, 6) + (lateDay ? rnd.Next(sh.GraceMinutes + 3, 42) : 0);
        var inT = sh.StartTime + TimeSpan.FromMinutes(inMin);
        var outT = sh.EndTime + TimeSpan.FromMinutes(rnd.Next(-6, 55));
        if (rnd.NextDouble() < .05) outT = sh.EndTime - TimeSpan.FromMinutes(rnd.Next(60, 150)); // early leave
        bool split = rnd.NextDouble() < .18;
        var sessions = split
            ? new[] { (inT, new TimeSpan(13, rnd.Next(0, 20), 0)), (new TimeSpan(14, rnd.Next(0, 25), 0), outT) }
            : new[] { (inT, outT) };
        bool first = true;
        foreach (var (a, b) in sessions)
        {
            var (lat, lng) = Jitter(site, rnd);
            bool late = first && a > sh.StartTime + TimeSpan.FromMinutes(sh.GraceMinutes);
            db.Attendance.Add(new AttendanceRecord
            {
                OrganizationId = org.Id, UserId = u.Id, SiteId = site.Id, WorkDate = d,
                CheckInUtc = OrgTime.LocalToUtc(d, a, org), CheckOutUtc = OrgTime.LocalToUtc(d, b, org),
                InLat = lat, InLng = lng, InAccuracy = 4 + rnd.Next(0, 16), InDistanceMeters = -(15 + rnd.Next(0, 220)),
                OutLat = lat, OutLng = lng, OutAccuracy = 5 + rnd.Next(0, 14), OutDistanceMeters = -(15 + rnd.Next(0, 220)),
                IsLate = late, LateMinutes = late ? (int)(a - sh.StartTime).TotalMinutes : 0,
                IsEarlyLeave = b < sh.EndTime - TimeSpan.FromMinutes(20), Source = PunchSource.Mobile
            });
            first = false;
        }
    }

    private static (double, double) Jitter(Site s, Random rnd)
    {
        double rMax = s.Shape == SiteShape.Circle ? s.RadiusMeters * .7 : 120;
        double r = rMax * Math.Sqrt(rnd.NextDouble()), th = rnd.NextDouble() * Math.PI * 2;
        return (s.CenterLat + r * Math.Cos(th) / 111_320.0, s.CenterLng + r * Math.Sin(th) / (111_320.0 * Math.Cos(s.CenterLat * Math.PI / 180)));
    }

    private static string Email(string name) => name.ToLower().Replace("dr. ", "").Replace(' ', '.') + "@greenfield.edu";
}
