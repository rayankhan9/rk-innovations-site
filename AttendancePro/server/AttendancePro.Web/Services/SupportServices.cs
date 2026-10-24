using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using System.IdentityModel.Tokens.Jwt;

namespace AttendancePro.Web.Services;

// ---------------------------------------------------------------- selfies
public class SelfieStore
{
    private readonly string _root;
    public SelfieStore(IWebHostEnvironment env) => _root = Path.Combine(env.ContentRootPath, "App_Data", "selfies");

    public async Task<string?> SaveAsync(int orgId, string userId, string base64)
    {
        try
        {
            var raw = base64.Contains(',') ? base64[(base64.IndexOf(',') + 1)..] : base64;
            var bytes = Convert.FromBase64String(raw);
            if (bytes.Length is < 500 or > 800_000) return null;
            // Only accept JPEG/PNG magic numbers.
            bool jpeg = bytes[0] == 0xFF && bytes[1] == 0xD8, png = bytes[0] == 0x89 && bytes[1] == 0x50;
            if (!jpeg && !png) return null;
            var rel = Path.Combine(orgId.ToString(), Clock.UtcNow.ToString("yyyyMM"), $"{userId[..8]}_{Guid.NewGuid():N}.{(jpeg ? "jpg" : "png")}");
            var full = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes);
            return rel.Replace('\\', '/');
        }
        catch { return null; }
    }

    public string? ResolveSafe(int orgId, string rel)
    {
        if (!rel.StartsWith(orgId + "/")) return null;
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        return full.StartsWith(Path.GetFullPath(_root)) && File.Exists(full) ? full : null;
    }
}

// ---------------------------------------------------------------- audit
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    public AuditService(AppDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

    public async Task LogAsync(string action, string? details = null, int? orgId = null)
    {
        var u = _http.HttpContext?.User;
        _db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = orgId ?? _db.CurrentOrgId ?? 0,
            ActorId = u?.FindFirstValue(ClaimTypes.NameIdentifier),
            ActorName = u?.FindFirstValue(ClaimTypes.Name),
            Action = action,
            Details = details?.Length > 480 ? details[..480] : details,
            Ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }
}

// ---------------------------------------------------------------- JWT + refresh tokens
public class JwtOptions
{
    public string Issuer { get; set; } = "AttendancePro";
    public string Audience { get; set; } = "AttendancePro.Mobile";
    public string Key { get; set; } = "";
    public int AccessMinutes { get; set; } = 30;
    public int RefreshDays { get; set; } = 60;
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessExpiresUtc);

public class TokenService
{
    private readonly JwtOptions _opt;
    private readonly AppDbContext _db;
    public TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> opt, AppDbContext db) { _opt = opt.Value; _db = db; }

    public async Task<TokenPair> IssueAsync(ApplicationUser user)
    {
        var exp = Clock.UtcNow.AddMinutes(_opt.AccessMinutes);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(TenantProvider.OrgClaim, user.OrganizationId.ToString()),
            new Claim("typ", "mobile")
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims, expires: exp, signingCredentials: creds);

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        _db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = Hash(refresh), ExpiresUtc = Clock.UtcNow.AddDays(_opt.RefreshDays) });
        await _db.SaveChangesAsync();
        return new TokenPair(new JwtSecurityTokenHandler().WriteToken(jwt), refresh, exp);
    }

    /// <summary>Rotates the refresh token; a reused/revoked token returns null.</summary>
    public async Task<(ApplicationUser User, TokenPair Tokens)?> RotateAsync(string refresh)
    {
        var h = Hash(refresh);
        var rt = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == h);
        if (rt is null || !rt.IsActive) return null;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == rt.UserId);
        if (user is null || user.Status == UserStatus.Disabled) return null;
        rt.RevokedUtc = Clock.UtcNow;
        await _db.SaveChangesAsync();
        return (user, await IssueAsync(user));
    }

    public async Task RevokeAsync(string refresh)
    {
        var h = Hash(refresh);
        var rt = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == h);
        if (rt != null) { rt.RevokedUtc = Clock.UtcNow; await _db.SaveChangesAsync(); }
    }

    public async Task RevokeAllAsync(string userId)
    {
        foreach (var t in await _db.RefreshTokens.Where(r => r.UserId == userId && r.RevokedUtc == null).ToListAsync())
            t.RevokedUtc = Clock.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}

// ---------------------------------------------------------------- email (free: any SMTP, MailKit)
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "AttendancePro";
    public bool UseStartTls { get; set; } = true;
}

public interface IMailSender { Task SendAsync(string to, string subject, string html, CancellationToken ct = default); }

public class SmtpMailSender : IMailSender
{
    private readonly SmtpOptions _o;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SmtpMailSender> _log;
    public SmtpMailSender(Microsoft.Extensions.Options.IOptions<SmtpOptions> o, IWebHostEnvironment env, ILogger<SmtpMailSender> log)
    { _o = o.Value; _env = env; _log = log; }

    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.Host))
        {
            // No SMTP configured: write to an outbox folder so development/testing still works.
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "outbox");
            Directory.CreateDirectory(dir);
            var safe = new string(to.Where(char.IsLetterOrDigit).ToArray());
            await File.WriteAllTextAsync(Path.Combine(dir, $"{Clock.UtcNow:yyyyMMdd_HHmmss}_{safe}.html"),
                $"<!-- To: {to} | Subject: {subject} -->\n{html}", ct);
            _log.LogWarning("SMTP not configured; email to {To} saved to App_Data/outbox", to);
            return;
        }
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, _o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(_o.User)) await client.AuthenticateAsync(_o.User, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
