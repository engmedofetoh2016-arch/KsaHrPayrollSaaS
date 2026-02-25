using HrPayroll.Application.Abstractions;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Auth;
using HrPayroll.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContextAccessor tenantContextAccessor,
        IDateTimeProvider dateTimeProvider) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _dateTimeProvider = dateTimeProvider;
    }

    public DbSet<Tenant> TenantSet => Set<Tenant>();
    public DbSet<CompanyProfile> CompanyProfileSet => Set<CompanyProfile>();
    public DbSet<Employee> EmployeeSet => Set<Employee>();
    public DbSet<AttendanceInput> AttendanceInputSet => Set<AttendanceInput>();
    public DbSet<PayrollPeriod> PayrollPeriodSet => Set<PayrollPeriod>();
    public DbSet<PayrollRun> PayrollRunSet => Set<PayrollRun>();
    public DbSet<PayrollLine> PayrollLineSet => Set<PayrollLine>();
    public DbSet<PayrollAdjustment> PayrollAdjustmentSet => Set<PayrollAdjustment>();
    public DbSet<LeaveRequest> LeaveRequestSet => Set<LeaveRequest>();
    public DbSet<LeaveBalance> LeaveBalanceSet => Set<LeaveBalance>();
    public DbSet<ExportArtifact> ExportArtifactSet => Set<ExportArtifact>();
    public DbSet<AuditLog> AuditLogSet => Set<AuditLog>();
    public DbSet<ComplianceAlert> ComplianceAlertSet => Set<ComplianceAlert>();
    public DbSet<ComplianceScoreSnapshot> ComplianceScoreSnapshotSet => Set<ComplianceScoreSnapshot>();
    public DbSet<ComplianceDigestDelivery> ComplianceDigestDeliverySet => Set<ComplianceDigestDelivery>();
    public DbSet<EmployeeLoan> EmployeeLoanSet => Set<EmployeeLoan>();
    public DbSet<EmployeeLoanInstallment> EmployeeLoanInstallmentSet => Set<EmployeeLoanInstallment>();
    public DbSet<EmployeeOffboarding> EmployeeOffboardingSet => Set<EmployeeOffboarding>();
    public DbSet<OffboardingChecklist> OffboardingChecklistSet => Set<OffboardingChecklist>();
    public DbSet<OffboardingChecklistItem> OffboardingChecklistItemSet => Set<OffboardingChecklistItem>();

    public IQueryable<Tenant> Tenants => TenantSet.AsQueryable();
    public IQueryable<CompanyProfile> CompanyProfiles => CompanyProfileSet.AsQueryable();
    public IQueryable<Employee> Employees => EmployeeSet.AsQueryable();
    public IQueryable<AttendanceInput> AttendanceInputs => AttendanceInputSet.AsQueryable();
    public IQueryable<PayrollPeriod> PayrollPeriods => PayrollPeriodSet.AsQueryable();
    public IQueryable<PayrollRun> PayrollRuns => PayrollRunSet.AsQueryable();
    public IQueryable<PayrollLine> PayrollLines => PayrollLineSet.AsQueryable();
    public IQueryable<PayrollAdjustment> PayrollAdjustments => PayrollAdjustmentSet.AsQueryable();
    public IQueryable<LeaveRequest> LeaveRequests => LeaveRequestSet.AsQueryable();
    public IQueryable<LeaveBalance> LeaveBalances => LeaveBalanceSet.AsQueryable();
    public IQueryable<ExportArtifact> ExportArtifacts => ExportArtifactSet.AsQueryable();
    public IQueryable<AuditLog> AuditLogs => AuditLogSet.AsQueryable();
    public IQueryable<ComplianceAlert> ComplianceAlerts => ComplianceAlertSet.AsQueryable();
    public IQueryable<ComplianceScoreSnapshot> ComplianceScoreSnapshots => ComplianceScoreSnapshotSet.AsQueryable();
    public IQueryable<ComplianceDigestDelivery> ComplianceDigestDeliveries => ComplianceDigestDeliverySet.AsQueryable();
    public IQueryable<EmployeeLoan> EmployeeLoans => EmployeeLoanSet.AsQueryable();
    public IQueryable<EmployeeLoanInstallment> EmployeeLoanInstallments => EmployeeLoanInstallmentSet.AsQueryable();
    public IQueryable<EmployeeOffboarding> EmployeeOffboardings => EmployeeOffboardingSet.AsQueryable();
    public IQueryable<OffboardingChecklist> OffboardingChecklists => OffboardingChecklistSet.AsQueryable();
    public IQueryable<OffboardingChecklistItem> OffboardingChecklistItems => OffboardingChecklistItemSet.AsQueryable();

    public void AddEntity<TEntity>(TEntity entity) where TEntity : class
    {
        Set<TEntity>().Add(entity);
    }

    public void RemoveEntities<TEntity>(IEnumerable<TEntity> entities) where TEntity : class
    {
        Set<TEntity>().RemoveRange(entities);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<CompanyProfile>().HasIndex(x => x.TenantId).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => new { x.TenantId, x.Email });
        modelBuilder.Entity<AttendanceInput>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<PayrollPeriod>().HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<PayrollRun>().HasIndex(x => new { x.TenantId, x.PayrollPeriodId });
        modelBuilder.Entity<PayrollLine>().HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId }).IsUnique();
        modelBuilder.Entity<PayrollAdjustment>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.Year, x.Month, x.Type });
        modelBuilder.Entity<LeaveRequest>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate, x.EndDate });
        modelBuilder.Entity<LeaveBalance>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.Year, x.LeaveType }).IsUnique();
        modelBuilder.Entity<ExportArtifact>().HasIndex(x => new { x.TenantId, x.PayrollRunId, x.CreatedAtUtc });
        modelBuilder.Entity<ExportArtifact>().HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<ComplianceAlert>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.DocumentType, x.ExpiryDate }).IsUnique();
        modelBuilder.Entity<ComplianceAlert>().HasIndex(x => new { x.TenantId, x.IsResolved, x.DaysLeft });
        modelBuilder.Entity<ComplianceScoreSnapshot>().HasIndex(x => new { x.TenantId, x.SnapshotDate }).IsUnique();
        modelBuilder.Entity<ComplianceDigestDelivery>().HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        modelBuilder.Entity<ComplianceDigestDelivery>().HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<EmployeeLoan>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        modelBuilder.Entity<EmployeeLoanInstallment>().HasIndex(x => new { x.TenantId, x.EmployeeLoanId, x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<EmployeeLoanInstallment>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.Year, x.Month, x.Status });
        modelBuilder.Entity<EmployeeOffboarding>().HasIndex(x => new { x.TenantId, x.Status, x.EffectiveDate });
        modelBuilder.Entity<EmployeeOffboarding>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.CreatedAtUtc });
        modelBuilder.Entity<OffboardingChecklist>().HasIndex(x => new { x.TenantId, x.OffboardingId }).IsUnique();
        modelBuilder.Entity<OffboardingChecklistItem>().HasIndex(x => new { x.TenantId, x.ChecklistId, x.ItemCode }).IsUnique();
        modelBuilder.Entity<OffboardingChecklistItem>().HasIndex(x => new { x.TenantId, x.ChecklistId, x.SortOrder });

        modelBuilder.Entity<CompanyProfile>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<Employee>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<AttendanceInput>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollPeriod>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollRun>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollLine>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollAdjustment>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<LeaveRequest>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<LeaveBalance>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ExportArtifact>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ComplianceAlert>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ComplianceScoreSnapshot>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ComplianceDigestDelivery>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<EmployeeLoan>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<EmployeeLoanInstallment>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<EmployeeOffboarding>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<OffboardingChecklist>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<OffboardingChecklistItem>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseAuditableEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = _dateTimeProvider.UtcNow;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = _dateTimeProvider.UtcNow;
            }

            if (entry.Entity is ITenantScoped tenantScoped && tenantScoped.TenantId == Guid.Empty && _tenantContextAccessor.TenantId != Guid.Empty)
            {
                tenantScoped.TenantId = _tenantContextAccessor.TenantId;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
