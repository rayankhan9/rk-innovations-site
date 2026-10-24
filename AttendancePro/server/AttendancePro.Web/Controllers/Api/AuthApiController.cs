using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers.Api;

public record RegisterDto(
    [Required, StringLength(20)] string OrgCode,
    [Required, StringLength(120, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(200)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [StringLength(40)] string? EmployeeCode,
    [StringLength(80)] string? Department,
    [StringLength(100)] string? DeviceId,
    [StringLength(100)] string? DeviceModel);

public record LoginDto([Required] string Email, [Required] string Password, string? DeviceId, string? DeviceModel);
public record RefreshDto([Required] string RefreshToken);
public record ForgotDto([Required, EmailAddress] string Email);
public record ResetDto([Required, EmailAddress] string Email, [Required] string Code, [Required, StringLength(100, MinimumLength = 8)] string NewPassword);

public static class ApiMap
{
    public static object User(ApplicationUser u, Organization o) => new
    {
        id = u.Id, fullName = u.FullName, email = u.Email, employeeCode = u.EmployeeCode, department = u.Department,
        designation = u.Designation, status = u.Status.ToString(),
        org = new { name = o.Name, type = o.Type.ToString(), code = o.Code, memberLabel = o.MemberLabel, timeZone = o.TimeZoneId, utcOffsetMinutes = (int)OrgTime.Zone(o).GetUtcOffset(Clock.UtcNow).TotalMinutes }
    };
}

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public class AuthApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly TokenService _tokens;
    private readonly IMailSender _mail;
    private readonly AuditService _audit;

    public AuthApiController(AppDbContext db, UserManager<ApplicationUser> users, TokenService tokens, IMailSender mail, AuditService audit)
    { _db = db; _users = users; _tokens = tokens; _mail = mail; _audit = audit; }

    /// <summary>Lets the app show "Join Acme University?" before the user fills the form.</summary>
    [HttpGet("org/{code}")]
    public async Task<IActionResult> Org(string code)
    {
        var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Code == code.Trim().ToUpper());
        if (org is null) return NotFound(new { code = "ORG_NOT_FOUND", message = "No organization found for that code." });
        return Ok(new { name = org.Name, type = org.Type.ToString(), memberLabel = org.MemberLabel, requiresApproval = org.RequireApproval });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Code == dto.OrgCode.Trim().ToUpper());
        if (org is null) return BadRequest(new { code = "ORG_NOT_FOUND", message = "Invalid organization code." });

        if (!string.IsNullOrWhiteSpace(dto.EmployeeCode) &&
            await _db.Users.AnyAsync(u => u.OrganizationId == org.Id && u.EmployeeCode == dto.EmployeeCode))
            return Conflict(new { code = "CODE_TAKEN", message = "That ID is already registered. Contact your administrator." });

        var user = new ApplicationUser
        {
            UserName = dto.Email.Trim(), Email = dto.Email.Trim(), FullName = dto.FullName.Trim(),
            EmployeeCode = dto.EmployeeCode?.Trim(), Department = dto.Department?.Trim(), OrganizationId = org.Id,
            Status = org.RequireApproval ? UserStatus.Pending : UserStatus.Active,
            DeviceId = org.RequireDeviceBinding ? dto.DeviceId : null, DeviceModel = dto.DeviceModel,
            ShiftId = org.DefaultShiftId
        };
        var res = await _users.CreateAsync(user, dto.Password);
        if (!res.Succeeded)
        {
            var dup = res.Errors.Any(e => e.Code.StartsWith("Duplicate"));
            return dup ? Conflict(new { code = "EMAIL_TAKEN", message = "An account with this email already exists." })
                       : BadRequest(new { code = "WEAK_PASSWORD", message = string.Join(" ", res.Errors.Select(e => e.Description)) });
        }
        await _audit.LogAsync("user.register", $"{user.FullName} <{user.Email}> joined via app", org.Id);
        var pair = await _tokens.IssueAsync(user);
        return Ok(new { tokens = pair, user = ApiMap.User(user, org) });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email.Trim());
        const string generic = "Incorrect email or password.";
        if (user is null) return Unauthorized(new { code = "BAD_CREDENTIALS", message = generic });
        if (await _users.IsLockedOutAsync(user)) return StatusCode(429, new { code = "LOCKED", message = "Too many attempts. Try again in a few minutes." });
        if (!await _users.CheckPasswordAsync(user, dto.Password))
        {
            await _users.AccessFailedAsync(user);
            return Unauthorized(new { code = "BAD_CREDENTIALS", message = generic });
        }
        await _users.ResetAccessFailedCountAsync(user);
        if (user.Status == UserStatus.Disabled) return StatusCode(403, new { code = "DISABLED", message = "Your account is disabled. Contact your administrator." });

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == user.OrganizationId);
        if (org.RequireDeviceBinding && !string.IsNullOrEmpty(user.DeviceId) && !string.IsNullOrEmpty(dto.DeviceId) && user.DeviceId != dto.DeviceId)
            return StatusCode(403, new { code = "DEVICE_MISMATCH", message = "This account is linked to another phone. Ask your administrator to reset your device." });
        if (org.RequireDeviceBinding && string.IsNullOrEmpty(user.DeviceId) && !string.IsNullOrEmpty(dto.DeviceId))
        { user.DeviceId = dto.DeviceId; user.DeviceModel = dto.DeviceModel; }
        user.LastLoginUtc = Clock.UtcNow;
        await _users.UpdateAsync(user);
        return Ok(new { tokens = await _tokens.IssueAsync(user), user = ApiMap.User(user, org) });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshDto dto)
    {
        var r = await _tokens.RotateAsync(dto.RefreshToken);
        if (r is null) return Unauthorized(new { code = "REFRESH_INVALID", message = "Session expired. Please sign in again." });
        return Ok(new { tokens = r.Value.Tokens });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshDto dto) { await _tokens.RevokeAsync(dto.RefreshToken); return NoContent(); }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(ForgotDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email.Trim());
        // Always respond OK so the endpoint can't be used to discover accounts.
        if (user is not null && user.Status != UserStatus.Disabled)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            user.ResetCodeHash = HashCode(user.Id, code);
            user.ResetCodeExpiresUtc = Clock.UtcNow.AddMinutes(15);
            user.ResetAttempts = 0;
            await _users.UpdateAsync(user);
            await _mail.SendAsync(user.Email!, "Your password reset code",
                $"<p>Hi {System.Net.WebUtility.HtmlEncode(user.FullName)},</p><p>Your AttendancePro reset code is <b style='font-size:20px'>{code}</b>. It expires in 15 minutes. If you did not request this, ignore this email.</p>");
        }
        return Ok(new { message = "If that email exists, a reset code has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email.Trim());
        var bad = BadRequest(new { code = "BAD_CODE", message = "Invalid or expired code." });
        if (user?.ResetCodeHash is null || user.ResetCodeExpiresUtc < Clock.UtcNow || user.ResetAttempts >= 5) return bad;
        user.ResetAttempts++;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.ResetCodeHash), Encoding.UTF8.GetBytes(HashCode(user.Id, dto.Code.Trim()))))
        { await _users.UpdateAsync(user); return bad; }
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var res = await _users.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!res.Succeeded) return BadRequest(new { code = "WEAK_PASSWORD", message = string.Join(" ", res.Errors.Select(e => e.Description)) });
        user.ResetCodeHash = null; user.ResetCodeExpiresUtc = null;
        await _users.UpdateAsync(user);
        await _tokens.RevokeAllAsync(user.Id);
        return Ok(new { message = "Password updated. You can sign in now." });
    }

    private static string HashCode(string userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + code)));
}
