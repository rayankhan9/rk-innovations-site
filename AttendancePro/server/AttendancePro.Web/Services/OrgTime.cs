using AttendancePro.Web.Models;

namespace AttendancePro.Web.Services;

public static class OrgTime
{
    public static TimeZoneInfo Zone(Organization org)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(org.TimeZoneId); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static DateTime ToLocal(DateTime utc, Organization org) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(org));

    public static DateOnly Today(Organization org) => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow, org));

    public static DateTime LocalToUtc(DateOnly d, TimeSpan t, Organization org)
    {
        var local = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.FromTimeSpan(t)), DateTimeKind.Unspecified);
        var zone = Zone(org);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
