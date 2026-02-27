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
    public DbSet<OffboardingChecklistTemplate> OffboardingChecklistTemplateSet => Set<OffboardingChecklistTemplate>();
    public DbSet<OffboardingChecklistApproval> OffboardingChecklistApprovalSet => Set<OffboardingChecklistApproval>();
    public DbSet<OffboardingEsignDocument> OffboardingEsignDocumentSet => Set<OffboardingEsignDocument>();
    public DbSet<ShiftRule> ShiftRuleSet => Set<ShiftRule>();
    public DbSet<TimesheetEntry> TimesheetEntrySet => Set<TimesheetEntry>();
    public DbSet<AllowancePolicy> AllowancePolicySet => Set<AllowancePolicy>();
    public DbSet<AllowancePolicyMatrix> AllowancePolicyMatrixSet => Set<AllowancePolicyMatrix>();
    public DbSet<PayrollApprovalMatrix> PayrollApprovalMatrixSet => Set<PayrollApprovalMatrix>();
    public DbSet<PayrollApprovalAction> PayrollApprovalActionSet => Set<PayrollApprovalAction>();
    public DbSet<EmployeeSelfServiceRequest> EmployeeSelfServiceRequestSet => Set<EmployeeSelfServiceRequest>();
    public DbSet<ComplianceRule> ComplianceRuleSet => Set<ComplianceRule>();
    public DbSet<ComplianceRuleEvent> ComplianceRuleEventSet => Set<ComplianceRuleEvent>();
    public DbSet<PayrollForecastScenario> PayrollForecastScenarioSet => Set<PayrollForecastScenario>();
    public DbSet<PayrollForecastResult> PayrollForecastResultSet => Set<PayrollForecastResult>();
    public DbSet<NotificationTemplate> NotificationTemplateSet => Set<NotificationTemplate>();
    public DbSet<NotificationQueueItem> NotificationQueueSet => Set<NotificationQueueItem>();
    public DbSet<DataQualityIssue> DataQualityIssueSet => Set<DataQualityIssue>();
    public DbSet<DataQualityFixBatch> DataQualityFixBatchSet => Set<DataQualityFixBatch>();

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
    public IQueryable<OffboardingChecklistTemplate> OffboardingChecklistTemplates => OffboardingChecklistTemplateSet.AsQueryable();
    public IQueryable<OffboardingChecklistApproval> OffboardingChecklistApprovals => OffboardingChecklistApprovalSet.AsQueryable();
    public IQueryable<OffboardingEsignDocument> OffboardingEsignDocuments => OffboardingEsignDocumentSet.AsQueryable();
    public IQueryable<ShiftRule> ShiftRules => ShiftRuleSet.AsQueryable();
    public IQueryable<TimesheetEntry> TimesheetEntries => TimesheetEntrySet.AsQueryable();
    public IQueryable<AllowancePolicy> AllowancePolicies => AllowancePolicySet.AsQueryable();
    public IQueryable<AllowancePolicyMatrix> AllowancePolicyMatrices => AllowancePolicyMatrixSet.AsQueryable();
    public IQueryable<PayrollApprovalMatrix> PayrollApprovalMatrices => PayrollApprovalMatrixSet.AsQueryable();
    public IQueryable<PayrollApprovalAction> PayrollApprovalActions => PayrollApprovalActionSet.AsQueryable();
    public IQueryable<EmployeeSelfServiceRequest> EmployeeSelfServiceRequests => EmployeeSelfServiceRequestSet.AsQueryable();
    public IQueryable<ComplianceRule> ComplianceRules => ComplianceRuleSet.AsQueryable();
    public IQueryable<ComplianceRuleEvent> ComplianceRuleEvents => ComplianceRuleEventSet.AsQueryable();
    public IQueryable<PayrollForecastScenario> PayrollForecastScenarios => PayrollForecastScenarioSet.AsQueryable();
    public IQueryable<PayrollForecastResult> PayrollForecastResults => PayrollForecastResultSet.AsQueryable();
    public IQueryable<NotificationTemplate> NotificationTemplates => NotificationTemplateSet.AsQueryable();
    public IQueryable<NotificationQueueItem> NotificationQueueItems => NotificationQueueSet.AsQueryable();
    public IQueryable<DataQualityIssue> DataQualityIssues => DataQualityIssueSet.AsQueryable();
    public IQueryable<DataQualityFixBatch> DataQualityFixBatches => DataQualityFixBatchSet.AsQueryable();

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
        modelBuilder.Entity<OffboardingChecklistTemplate>().HasIndex(x => new { x.TenantId, x.RoleName, x.ItemCode }).IsUnique();
        modelBuilder.Entity<OffboardingChecklistTemplate>().HasIndex(x => new { x.TenantId, x.RoleName, x.IsActive });
        modelBuilder.Entity<OffboardingChecklistApproval>().HasIndex(x => new { x.TenantId, x.ChecklistItemId, x.Status });
        modelBuilder.Entity<OffboardingEsignDocument>().HasIndex(x => new { x.TenantId, x.ChecklistItemId, x.Status });
        modelBuilder.Entity<ShiftRule>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        modelBuilder.Entity<TimesheetEntry>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.WorkDate }).IsUnique();
        modelBuilder.Entity<TimesheetEntry>().HasIndex(x => new { x.TenantId, x.Status, x.WorkDate });
        modelBuilder.Entity<AllowancePolicy>().HasIndex(x => new { x.TenantId, x.IsActive, x.JobTitle });
        modelBuilder.Entity<AllowancePolicy>().HasIndex(x => new { x.TenantId, x.PolicyName }).IsUnique();
        modelBuilder.Entity<AllowancePolicyMatrix>().HasIndex(x => new { x.TenantId, x.IsActive, x.GradeCode, x.LocationCode });
        modelBuilder.Entity<AllowancePolicyMatrix>().HasIndex(x => new { x.TenantId, x.GradeCode, x.LocationCode, x.EffectiveFrom }).IsUnique();
        modelBuilder.Entity<PayrollApprovalMatrix>().HasIndex(x => new { x.TenantId, x.PayrollScope, x.StageCode }).IsUnique();
        modelBuilder.Entity<PayrollApprovalAction>().HasIndex(x => new { x.TenantId, x.PayrollRunId, x.ActionAtUtc });
        modelBuilder.Entity<PayrollApprovalAction>().HasIndex(x => new { x.TenantId, x.StageCode, x.ActionStatus });
        modelBuilder.Entity<EmployeeSelfServiceRequest>().HasIndex(x => new { x.TenantId, x.EmployeeId, x.RequestType, x.Status });
        modelBuilder.Entity<EmployeeSelfServiceRequest>().HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        modelBuilder.Entity<ComplianceRule>().HasIndex(x => new { x.TenantId, x.RuleCode }).IsUnique();
        modelBuilder.Entity<ComplianceRuleEvent>().HasIndex(x => new { x.TenantId, x.Status, x.TriggeredAtUtc });
        modelBuilder.Entity<ComplianceRuleEvent>().HasIndex(x => new { x.TenantId, x.RuleId, x.Status });
        modelBuilder.Entity<PayrollForecastScenario>().HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        modelBuilder.Entity<PayrollForecastResult>().HasIndex(x => new { x.TenantId, x.ScenarioId, x.ForecastYear, x.ForecastMonth }).IsUnique();
        modelBuilder.Entity<NotificationTemplate>().HasIndex(x => new { x.TenantId, x.TemplateCode, x.Channel }).IsUnique();
        modelBuilder.Entity<NotificationQueueItem>().HasIndex(x => new { x.TenantId, x.Status, x.ScheduledAtUtc });
        modelBuilder.Entity<NotificationQueueItem>().HasIndex(x => new { x.TenantId, x.Channel, x.Status });
        modelBuilder.Entity<DataQualityIssue>().HasIndex(x => new { x.TenantId, x.IssueStatus, x.Severity, x.DetectedAtUtc });
        modelBuilder.Entity<DataQualityIssue>().HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        modelBuilder.Entity<DataQualityFixBatch>().HasIndex(x => new { x.TenantId, x.BatchReference }).IsUnique();

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
        modelBuilder.Entity<OffboardingChecklistTemplate>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<OffboardingChecklistApproval>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<OffboardingEsignDocument>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ShiftRule>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<TimesheetEntry>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<AllowancePolicy>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<AllowancePolicyMatrix>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollApprovalMatrix>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollApprovalAction>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<EmployeeSelfServiceRequest>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ComplianceRule>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<ComplianceRuleEvent>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollForecastScenario>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<PayrollForecastResult>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<NotificationTemplate>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<NotificationQueueItem>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<DataQualityIssue>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
        modelBuilder.Entity<DataQualityFixBatch>().HasQueryFilter(x => _tenantContextAccessor.TenantId == Guid.Empty || x.TenantId == _tenantContextAccessor.TenantId);
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
