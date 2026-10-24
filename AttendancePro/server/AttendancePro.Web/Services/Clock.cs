namespace AttendancePro.Web.Services;

/// <summary>
/// Single time source. The optional offset exists only to stage realistic demo/promo data
/// (config key Demo:ClockOffsetHours) and must never be set in production.
/// </summary>
public static class Clock
{
    public static TimeSpan Offset { get; set; } = TimeSpan.Zero;
    public static DateTime UtcNow => DateTime.UtcNow + Offset;
}
