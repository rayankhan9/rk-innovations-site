using System.Diagnostics;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers;

/// <summary>Base for every admin-portal controller: admin-only, tenant-scoped, org available to views.</summary>
[Authorize(Policy = "Admin")]
[AutoValidateAntiforgeryToken]
public abstract class AdminController : Controller
{
    protected readonly AppDbContext Db;
    protected readonly AuditService Audit;
    private Organization? _org;

    protected AdminController(AppDbContext db, AuditService audit) { Db = db; Audit = audit; }

    protected int OrgId => Db.CurrentOrgId ?? throw new InvalidOperationException("No tenant");

    protected async Task<Organization> GetOrg() => _org ??= await Db.Organizations.FirstAsync(o => o.Id == OrgId);

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (User.Identity?.IsAuthenticated == true && Db.CurrentOrgId != null)
        {
            var org = await GetOrg();
            ViewBag.Org = org;
            ViewBag.Today = OrgTime.Today(org);
            ViewBag.PendingBadge =
                await Db.Users.CountAsync(u => u.OrganizationId == org.Id && u.Status == UserStatus.Pending) +
                await Db.Leaves.CountAsync(l => l.Status == RequestStatus.Pending) +
                await Db.Regularizations.CountAsync(r => r.Status == RequestStatus.Pending);
        }
        await base.OnActionExecutionAsync(context, next);
    }

    protected string FormatLocal(DateTime? utc, string fmt = "dd MMM HH:mm") =>
        utc is null ? "—" : OrgTime.ToLocal(utc.Value, (Organization)ViewBag.Org).ToString(fmt);

    protected void Success(string msg) => TempData["ok"] = msg;
    protected void Fail(string msg) => TempData["err"] = msg;
}

[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("Error")]
    public IActionResult Index() => View("~/Views/Shared/Error.cshtml", new Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
