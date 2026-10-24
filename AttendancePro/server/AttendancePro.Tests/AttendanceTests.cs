using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace AttendancePro.Tests;

public class GeoServiceTests
{
    private static Site Circle() => new() { Shape = SiteShape.Circle, CenterLat = 28.6139, CenterLng = 77.2090, RadiusMeters = 100 };

    [Fact]
    public void Circle_inside_and_outside()
    {
        Assert.True(GeoService.Evaluate(Circle(), 28.6139, 77.2090).Inside);
        Assert.True(GeoService.Evaluate(Circle(), 28.6143, 77.2090).Inside);      // ~44 m north
        var far = GeoService.Evaluate(Circle(), 28.6200, 77.2090);                  // ~680 m north
        Assert.False(far.Inside);
        Assert.InRange(far.DistanceMeters, 550, 650);                               // distance beyond the edge
    }

    [Fact]
    public void Tolerance_extends_the_boundary()
    {
        var p = (Lat: 28.6139 + 0.00115, Lng: 77.2090);                             // ~128 m: 28 m outside
        Assert.False(GeoService.Evaluate(Circle(), p.Lat, p.Lng, 0).Inside);
        Assert.True(GeoService.Evaluate(Circle(), p.Lat, p.Lng, 40).Inside);
    }

    [Fact]
    public void Polygon_inside_outside_and_edge_distance()
    {
        var s = new Site { Shape = SiteShape.Polygon, PolygonJson = "[[28.6130,77.2080],[28.6130,77.2100],[28.6150,77.2100],[28.6150,77.2080]]" };
        var inside = GeoService.Evaluate(s, 28.6140, 77.2090);
        Assert.True(inside.Inside);
        Assert.True(inside.DistanceMeters < 0);
        var outside = GeoService.Evaluate(s, 28.6160, 77.2090);                     // ~110 m north of the top edge
        Assert.False(outside.Inside);
        Assert.InRange(outside.DistanceMeters, 90, 130);
    }

    [Fact]
    public void Degenerate_polygon_never_matches() =>
        Assert.False(GeoService.Evaluate(new Site { Shape = SiteShape.Polygon, PolygonJson = "[[1,1],[2,2]]" }, 1, 1).Inside);
}

public class PunchFlowTests : IDisposable
{
    private class NoTenant : ITenantProvider { public int? OrganizationId => null; public string? UserId => null; }
    private class FakeEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "t"; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath(); public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly AttendanceService _svc;
    private readonly Organization _org;
    private readonly ApplicationUser _user;
    private const double Lat = 28.6139, Lng = 77.2090;

    public PunchFlowTests()
    {
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options, new NoTenant());
        _db.Database.EnsureCreated();
        _org = new Organization { Name = "T", Code = "TEST01", TimeZoneId = "UTC", RequireDeviceBinding = true, MaxAccuracyMeters = 50 };
        _db.Organizations.Add(_org); _db.SaveChanges();
        var shift = new Shift { OrganizationId = _org.Id, Name = "G", StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0), WorkDaysMask = 127 };
        _db.Shifts.Add(shift);
        _db.Sites.Add(new Site { OrganizationId = _org.Id, Name = "HQ", CenterLat = Lat, CenterLng = Lng, RadiusMeters = 100 });
        _db.SaveChanges();
        _org.DefaultShiftId = shift.Id;
        _user = new ApplicationUser { UserName = "a@b.c", Email = "a@b.c", FullName = "A", OrganizationId = _org.Id, Status = UserStatus.Active };
        _db.Users.Add(_user); _db.SaveChanges();
        _svc = new AttendanceService(_db, new SelfieStore(new FakeEnv()));
    }

    private PunchRequest Req(Action<PunchRequest>? tweak = null)
    {
        var r = new PunchRequest { Lat = Lat, Lng = Lng, Accuracy = 10, DeviceId = "dev1", ClientTimeUtc = DateTime.UtcNow, EventId = Guid.NewGuid().ToString() };
        tweak?.Invoke(r);
        return r;
    }

    [Fact]
    public async Task Check_in_then_out_inside_fence()
    {
        var a = await _svc.CheckInAsync(_user, _org, Req());
        Assert.True(a.Ok, a.Message);
        var b = await _svc.CheckOutAsync(_user, _org, Req());
        Assert.True(b.Ok, b.Message);
        Assert.NotNull(b.Record!.CheckOutUtc);
    }

    [Fact]
    public async Task Rejects_outside_fence_mock_and_poor_accuracy()
    {
        Assert.Equal("OUTSIDE_GEOFENCE", (await _svc.CheckInAsync(_user, _org, Req(r => r.Lat = 28.70))).Code);
        Assert.Equal("MOCK_LOCATION", (await _svc.CheckInAsync(_user, _org, Req(r => r.IsMock = true))).Code);
        Assert.Equal("LOW_ACCURACY", (await _svc.CheckInAsync(_user, _org, Req(r => r.Accuracy = 400))).Code);
        Assert.Equal(0, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Device_is_bound_on_first_use_and_others_are_refused()
    {
        Assert.True((await _svc.CheckInAsync(_user, _org, Req())).Ok);
        await _svc.CheckOutAsync(_user, _org, Req());
        Assert.Equal("DEVICE_MISMATCH", (await _svc.CheckInAsync(_user, _org, Req(r => r.DeviceId = "other"))).Code);
    }

    [Fact]
    public async Task Same_event_id_is_idempotent()
    {
        var r = Req();
        Assert.True((await _svc.CheckInAsync(_user, _org, r)).Ok);
        var again = await _svc.CheckInAsync(_user, _org, r);
        Assert.True(again.Duplicate);
        Assert.Equal(1, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Cannot_check_in_twice_or_out_without_in()
    {
        Assert.Equal("NOT_CHECKED_IN", (await _svc.CheckOutAsync(_user, _org, Req())).Code);
        await _svc.CheckInAsync(_user, _org, Req());
        Assert.Equal("ALREADY_CHECKED_IN", (await _svc.CheckInAsync(_user, _org, Req())).Code);
    }

    [Fact]
    public async Task Clock_drift_rejected_online_and_stale_offline_rejected()
    {
        Assert.Equal("CLOCK_DRIFT", (await _svc.CheckInAsync(_user, _org, Req(r => r.ClientTimeUtc = DateTime.UtcNow.AddHours(-2)))).Code);
        Assert.Equal("STALE_PUNCH", (await _svc.CheckInAsync(_user, _org, Req(r => { r.Offline = true; r.ClientTimeUtc = DateTime.UtcNow.AddDays(-5); }))).Code);
        var ok = await _svc.CheckInAsync(_user, _org, Req(r => { r.Offline = true; r.ClientTimeUtc = DateTime.UtcNow.AddHours(-3); }));
        Assert.True(ok.Ok, ok.Message);
        Assert.True(ok.Record!.IsOfflineSync);
        Assert.True((DateTime.UtcNow - ok.Record.CheckInUtc).TotalHours > 2.9);   // device time is preserved
    }

    [Fact]
    public async Task Pending_users_cannot_punch()
    {
        _user.Status = UserStatus.Pending;
        Assert.Equal("ACCOUNT_INACTIVE", (await _svc.CheckInAsync(_user, _org, Req())).Code);
    }

    [Fact]
    public async Task Impossible_travel_is_flagged_not_blocked()
    {
        _db.Sites.Add(new Site { OrganizationId = _org.Id, Name = "Far", CenterLat = 19.0760, CenterLng = 72.8777, RadiusMeters = 100 }); // Mumbai
        await _db.SaveChangesAsync();
        await _svc.CheckInAsync(_user, _org, Req());
        await _svc.CheckOutAsync(_user, _org, Req());
        var far = await _svc.CheckInAsync(_user, _org, Req(r => { r.Lat = 19.0760; r.Lng = 72.8777; }));
        Assert.True(far.Ok);
        Assert.True(far.Record!.IsFlagged);
        Assert.Contains("Impossible travel", far.Record.FlagReason);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}

public class StatsTests
{
    [Fact]
    public void Absent_only_counts_after_joining_and_off_days_are_excluded()
    {
        var org = new Organization { Id = 1, TimeZoneId = "UTC" };
        var shift = new Shift { Id = 1, WorkDaysMask = 2 | 4 | 8 | 16 | 32 };                 // Mon–Fri
        var user = new ApplicationUser { Id = "u", CreatedUtc = new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc) }; // Wed
        var rec = new AttendanceRecord { UserId = "u", WorkDate = new DateOnly(2026, 3, 11), CheckInUtc = new DateTime(2026, 3, 11, 9, 0, 0, DateTimeKind.Utc), CheckOutUtc = new DateTime(2026, 3, 11, 17, 0, 0, DateTimeKind.Utc) };
        var bundle = new StatsService.Bundle(new() { rec }, new(), new() { [new DateOnly(2026, 3, 12)] = "Holiday" });
        var days = StatsService.BuildDays(user, shift, org, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 13), bundle);

        Assert.Equal(DayStatus.NotJoined, days[2].Status);                                   // Mar 3
        Assert.Equal(DayStatus.Present, days[10].Status);                                    // Mar 11
        Assert.Equal(DayStatus.Holiday, days[11].Status);
    }
}
