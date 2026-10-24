using System.Security.Claims;

namespace AttendancePro.Web.Services;

public interface ITenantProvider { int? OrganizationId { get; } string? UserId { get; } }

public class TenantProvider : ITenantProvider
{
    public const string OrgClaim = "org_id";
    private readonly IHttpContextAccessor _http;
    public TenantProvider(IHttpContextAccessor http) => _http = http;

    public int? OrganizationId
    {
        get
        {
            var v = _http.HttpContext?.User.FindFirstValue(OrgClaim);
            return int.TryParse(v, out var id) ? id : null;
        }
    }
    public string? UserId => _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
