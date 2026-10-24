using System.ComponentModel.DataAnnotations;
using AttendancePro.Web.Data;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Controllers;

public class LoginVm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
}

public class OrgSignupVm
{
    [Required, StringLength(120), Display(Name = "Organization name")] public string OrgName { get; set; } = "";
    [Display(Name = "Type")] public OrgType Type { get; set; } = OrgType.Company;
    [Required, Display(Name = "Time zone")] public string TimeZoneId { get; set; } = "UTC";
    [Required, StringLength(120), Display(Name = "Your name")] public string FullName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")] public string Confirm { get; set; } = "";
}

public class ChangePwVm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string Current { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")] public string New { get; set; } = "";
}

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly AuditService _audit;

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppDbContext db, IConfiguration cfg, AuditService audit)
    { _signIn = signIn; _users = users; _db = db; _cfg = cfg; _audit = audit; }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.AllowSignup = _cfg.GetValue("Registration:AllowOrganizationSignup", true);
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginVm());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        ViewBag.AllowSignup = _cfg.GetValue("Registration:AllowOrganizationSignup", true);
        if (!ModelState.IsValid) return View(vm);
        var user = await _users.FindByEmailAsync(vm.Email.Trim());
        if (user is null || !user.IsAdmin || user.Status != UserStatus.Active)
        {
            ModelState.AddModelError("", "Invalid email or password, or this account has no admin access.");
            return View(vm);
        }
        var res = await _signIn.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (res.IsLockedOut) { ModelState.AddModelError("", "Account locked for 10 minutes after too many attempts."); return View(vm); }
        if (!res.Succeeded) { ModelState.AddModelError("", "Invalid email or password, or this account has no admin access."); return View(vm); }
        user.LastLoginUtc = Clock.UtcNow; await _users.UpdateAsync(user);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Dashboard");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout() { await _signIn.SignOutAsync(); return RedirectToAction(nameof(Login)); }

    [AllowAnonymous] public IActionResult Denied() => View();

    [AllowAnonymous, HttpGet]
    public IActionResult Signup()
    {
        if (!_cfg.GetValue("Registration:AllowOrganizationSignup", true)) return NotFound();
        return View(new OrgSignupVm());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
    public async Task<IActionResult> Signup(OrgSignupVm vm)
    {
        if (!_cfg.GetValue("Registration:AllowOrganizationSignup", true)) return NotFound();
        if (!ModelState.IsValid) return View(vm);

        string code;
        do code = Seeder.NewOrgCode(); while (await _db.Organizations.AnyAsync(o => o.Code == code));
        var org = new Organization { Name = vm.OrgName.Trim(), Type = vm.Type, TimeZoneId = vm.TimeZoneId, Code = code, ContactEmail = vm.Email.Trim() };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        var shift = new Shift { OrganizationId = org.Id, Name = "General" };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();
        org.DefaultShiftId = shift.Id;
        await _db.SaveChangesAsync();

        var admin = new ApplicationUser
        {
            UserName = vm.Email.Trim(), Email = vm.Email.Trim(), FullName = vm.FullName.Trim(),
            OrganizationId = org.Id, IsAdmin = true, Status = UserStatus.Active
        };
        var res = await _users.CreateAsync(admin, vm.Password);
        if (!res.Succeeded)
        {
            _db.Organizations.Remove(org); _db.Shifts.Remove(shift); await _db.SaveChangesAsync();
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await _users.AddToRoleAsync(admin, "Admin");
        await _audit.LogAsync("org.create", $"Organization '{org.Name}' created", org.Id);
        await _signIn.SignInAsync(admin, false);
        TempData["ok"] = $"Welcome! Your organization code is {org.Code}. Next: add your first location under Sites.";
        return RedirectToAction("Create", "Sites");
    }

    [Authorize, HttpGet] public IActionResult Password() => View(new ChangePwVm());

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(ChangePwVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _users.GetUserAsync(User);
        var res = await _users.ChangePasswordAsync(user!, vm.Current, vm.New);
        if (!res.Succeeded) { foreach (var e in res.Errors) ModelState.AddModelError("", e.Description); return View(vm); }
        await _signIn.RefreshSignInAsync(user!);
        TempData["ok"] = "Password changed.";
        return RedirectToAction("Index", "Dashboard");
    }
}
