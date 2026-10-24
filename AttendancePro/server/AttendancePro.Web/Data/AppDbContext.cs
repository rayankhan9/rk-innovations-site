using System.Linq.Expressions;
using AttendancePro.Web.Models;
using AttendancePro.Web.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AttendancePro.Web.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantProvider _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenant) : base(options)
        => _tenant = tenant;

    /// <summary>Current tenant for query filters; null = background/system context (no filtering).</summary>
    public int? CurrentOrgId => _tenant.OrganizationId;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<UserSite> UserSites => Set<UserSite>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<LeaveRequest> Leaves => Set<LeaveRequest>();
    public DbSet<RegularizationRequest> Regularizations => Set<RegularizationRequest>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ReportLog> ReportLogs => Set<ReportLog>();

    /// <summary>SQLite returns DateTimeKind.Unspecified; force UTC so JSON always carries a 'Z'.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<UtcNullableConverter>();
    }

    private class UtcConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public UtcConverter() : base(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
    }
    private class UtcNullableConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
    {
        public UtcNullableConverter() : base(v => v == null ? v : v.Value.ToUniversalTime(), v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) { }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Global tenant filter on every tenant-owned entity.
        foreach (var et in b.Model.GetEntityTypes().Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType, "e");
            var orgProp = Expression.Property(p, nameof(ITenantEntity.OrganizationId));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentOrgId));
            var body = Expression.OrElse(
                Expression.Equal(current, Expression.Constant(null, typeof(int?))),
                Expression.Equal(Expression.Convert(orgProp, typeof(int?)), current));
            b.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
        }

        b.Entity<Organization>().HasIndex(o => o.Code).IsUnique();
        b.Entity<ApplicationUser>().HasIndex(u => new { u.OrganizationId, u.EmployeeCode });
        b.Entity<ApplicationUser>().HasOne(u => u.Organization).WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApplicationUser>().HasOne(u => u.Shift).WithMany().HasForeignKey(u => u.ShiftId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<AttendanceRecord>().HasIndex(a => new { a.UserId, a.WorkDate });
        b.Entity<AttendanceRecord>().HasIndex(a => new { a.OrganizationId, a.WorkDate });
        b.Entity<AttendanceRecord>().HasIndex(a => new { a.UserId, a.InEventId });
        b.Entity<AttendanceRecord>().HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AttendanceRecord>().HasOne(a => a.Site).WithMany().HasForeignKey(a => a.SiteId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<UserSite>().HasOne(us => us.User).WithMany(u => u.Sites).HasForeignKey(us => us.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSite>().HasOne(us => us.Site).WithMany(s => s.Users).HasForeignKey(us => us.SiteId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSite>().HasIndex(us => new { us.UserId, us.SiteId }).IsUnique();

        b.Entity<LeaveRequest>().HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RegularizationRequest>().HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Holiday>().HasIndex(h => new { h.OrganizationId, h.Date }).IsUnique();
        b.Entity<RefreshToken>().HasIndex(r => r.TokenHash).IsUnique();
        b.Entity<ReportLog>().HasIndex(r => new { r.OrganizationId, r.Year, r.Month, r.UserId });
        b.Entity<AuditLog>().HasIndex(a => new { a.OrganizationId, a.AtUtc });

        // SQLite can't order/compare TimeSpan/DateOnly natively in all cases; store TimeSpan as ticks-friendly.
        b.Entity<Shift>().Property(s => s.StartTime).HasConversion<long>();
        b.Entity<Shift>().Property(s => s.EndTime).HasConversion<long>();
        b.Entity<RegularizationRequest>().Property(r => r.RequestedIn).HasConversion<long?>();
        b.Entity<RegularizationRequest>().Property(r => r.RequestedOut).HasConversion<long?>();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Stamp tenant on new tenant entities.
        if (CurrentOrgId is int org)
            foreach (var e in ChangeTracker.Entries<ITenantEntity>().Where(e => e.State == EntityState.Added && e.Entity.OrganizationId == 0))
                e.Entity.OrganizationId = org;
        return base.SaveChangesAsync(ct);
    }
}

/// <summary>Lets `dotnet ef` build the model without starting the web host (and its seeding).</summary>
public class DesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    private class NoTenant : ITenantProvider { public int? OrganizationId => null; public string? UserId => null; }
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=App_Data/design.db").Options, new NoTenant());
}
