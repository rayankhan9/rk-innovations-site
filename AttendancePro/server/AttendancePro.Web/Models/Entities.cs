using AttendancePro.Web.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AttendancePro.Web.Models;

public enum OrgType { Company, Education, Healthcare, Retail, Government, Other }
public enum UserStatus { Pending, Active, Disabled }
public enum SiteShape { Circle, Polygon }
public enum RequestStatus { Pending, Approved, Rejected, Cancelled }
public enum PunchSource { Mobile, Manual, AutoClose, Regularization }
public enum LeaveType { Casual, Sick, Annual, Unpaid, Other }

public interface ITenantEntity { int OrganizationId { get; set; } }

public class Organization
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    /// <summary>Short code users type in the app to join this organization.</summary>
    [Required, StringLength(20)] public string Code { get; set; } = "";
    public OrgType Type { get; set; } = OrgType.Company;
    [StringLength(80)] public string TimeZoneId { get; set; } = "UTC";
    [StringLength(200)] public string? ContactEmail { get; set; }

    // Policy
    public bool RequireApproval { get; set; } = true;
    public bool RequireSelfie { get; set; }
    public bool BlockMockLocation { get; set; } = true;
    public bool RequireDeviceBinding { get; set; } = true;
    public int MaxAccuracyMeters { get; set; } = 50;
    public int GeofenceToleranceMeters { get; set; } = 0;
    public int MaxClockDriftMinutes { get; set; } = 5;
    public int MaxOfflineSyncHours { get; set; } = 48;
    public bool AutoCloseOpenSessions { get; set; } = true;
    public bool SendMonthlyReports { get; set; } = true;
    public bool SendAdminMonthlySummary { get; set; } = true;
    public int? DefaultShiftId { get; set; }

    public DateTime CreatedUtc { get; set; } = Clock.UtcNow;

    /// <summary>Wording shown in the UI/app: Employee/Staff vs Student.</summary>
    public string MemberLabel => Type == OrgType.Education ? "Member" : "Employee";
}

public class ApplicationUser : IdentityUser
{
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    [Required, StringLength(120)] public string FullName { get; set; } = "";
    [StringLength(40)] public string? EmployeeCode { get; set; }
    [StringLength(80)] public string? Department { get; set; }
    [StringLength(60)] public string? Designation { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public bool IsAdmin { get; set; }

    [StringLength(100)] public string? DeviceId { get; set; }
    [StringLength(100)] public string? DeviceModel { get; set; }
    public bool ReceiveMonthlyReport { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = Clock.UtcNow;
    public DateTime? LastLoginUtc { get; set; }
    [StringLength(128)] public string? ResetCodeHash { get; set; }
    public DateTime? ResetCodeExpiresUtc { get; set; }
    public int ResetAttempts { get; set; }

    public List<UserSite> Sites { get; set; } = new();
}

public class Site : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(250)] public string? Address { get; set; }
    public SiteShape Shape { get; set; } = SiteShape.Circle;
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusMeters { get; set; } = 100;
    /// <summary>JSON array of [lat,lng] pairs when Shape = Polygon.</summary>
    public string? PolygonJson { get; set; }
    public bool IsActive { get; set; } = true;
    public List<UserSite> Users { get; set; } = new();
}

/// <summary>Restricts a user to specific sites. A user with no rows may punch at any active site.</summary>
public class UserSite : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }
}

public class Shift : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "General";
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);
    public int GraceMinutes { get; set; } = 10;
    /// <summary>Hours worked below which a day counts as half-day.</summary>
    public double HalfDayHours { get; set; } = 4;
    /// <summary>Bitmask: Sun=1, Mon=2, Tue=4, Wed=8, Thu=16, Fri=32, Sat=64.</summary>
    public int WorkDaysMask { get; set; } = 2 | 4 | 8 | 16 | 32;
    public bool IsWorkDay(DayOfWeek d) => (WorkDaysMask & (1 << (int)d)) != 0;
    public double ScheduledHours => Math.Max(0, ((EndTime - StartTime).TotalHours + 24) % 24);
}

public class AttendanceRecord : ITenantEntity
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    /// <summary>Org-local calendar date of check-in, used for grouping.</summary>
    public DateOnly WorkDate { get; set; }
    public DateTime CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }

    public double? InLat { get; set; }
    public double? InLng { get; set; }
    public double? InAccuracy { get; set; }
    public double? InDistanceMeters { get; set; }
    public double? OutLat { get; set; }
    public double? OutLng { get; set; }
    public double? OutAccuracy { get; set; }
    public double? OutDistanceMeters { get; set; }

    public bool IsLate { get; set; }
    public int LateMinutes { get; set; }
    public bool IsEarlyLeave { get; set; }
    public bool IsOfflineSync { get; set; }
    public bool IsFlagged { get; set; }
    [StringLength(300)] public string? FlagReason { get; set; }
    public PunchSource Source { get; set; } = PunchSource.Mobile;
    [StringLength(100)] public string? DeviceId { get; set; }
    [StringLength(60)] public string? InEventId { get; set; }
    [StringLength(60)] public string? OutEventId { get; set; }
    [StringLength(200)] public string? InSelfie { get; set; }
    [StringLength(200)] public string? OutSelfie { get; set; }
    [StringLength(300)] public string? Notes { get; set; }

    public double WorkedHours => CheckOutUtc is null ? 0 : Math.Max(0, (CheckOutUtc.Value - CheckInUtc).TotalHours);
}

public class LeaveRequest : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public LeaveType Type { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    [StringLength(300)] public string? ReviewNote { get; set; }
    public DateTime CreatedUtc { get; set; } = Clock.UtcNow;
}

/// <summary>User asks admin to fix a missed punch (forgot to check out, etc.).</summary>
public class RegularizationRequest : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public DateOnly Date { get; set; }
    public TimeSpan? RequestedIn { get; set; }
    public TimeSpan? RequestedOut { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    [StringLength(300)] public string? ReviewNote { get; set; }
    public DateTime CreatedUtc { get; set; } = Clock.UtcNow;
}

public class Holiday : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public DateOnly Date { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
}

public class AuditLog : ITenantEntity
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    public DateTime AtUtc { get; set; } = Clock.UtcNow;
    public string? ActorId { get; set; }
    [StringLength(120)] public string? ActorName { get; set; }
    [Required, StringLength(60)] public string Action { get; set; } = "";
    [StringLength(500)] public string? Details { get; set; }
    [StringLength(60)] public string? Ip { get; set; }
}

public class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    [Required, StringLength(128)] public string TokenHash { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = Clock.UtcNow;
    public DateTime? RevokedUtc { get; set; }
    public bool IsActive => RevokedUtc is null && ExpiresUtc > Clock.UtcNow;
}

public class ReportLog : ITenantEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }          // null = admin summary
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SentUtc { get; set; } = Clock.UtcNow;
    public bool Success { get; set; }
    [StringLength(300)] public string? Error { get; set; }
}
