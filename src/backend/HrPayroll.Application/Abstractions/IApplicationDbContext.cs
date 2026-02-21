using HrPayroll.Domain.Entities;

namespace HrPayroll.Application.Abstractions;

public interface IApplicationDbContext
{
    IQueryable<Tenant> Tenants { get; }
    IQueryable<CompanyProfile> CompanyProfiles { get; }
    IQueryable<Employee> Employees { get; }
    IQueryable<AttendanceInput> AttendanceInputs { get; }
    IQueryable<PayrollPeriod> PayrollPeriods { get; }
    IQueryable<PayrollRun> PayrollRuns { get; }
    IQueryable<PayrollLine> PayrollLines { get; }
    IQueryable<PayrollAdjustment> PayrollAdjustments { get; }
    IQueryable<LeaveRequest> LeaveRequests { get; }
    IQueryable<LeaveBalance> LeaveBalances { get; }
    IQueryable<ExportArtifact> ExportArtifacts { get; }
    IQueryable<AuditLog> AuditLogs { get; }
    IQueryable<ComplianceAlert> ComplianceAlerts { get; }
    IQueryable<ComplianceScoreSnapshot> ComplianceScoreSnapshots { get; }
    IQueryable<ComplianceDigestDelivery> ComplianceDigestDeliveries { get; }

    void AddEntity<TEntity>(TEntity entity) where TEntity : class;
    void RemoveEntities<TEntity>(IEnumerable<TEntity> entities) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
