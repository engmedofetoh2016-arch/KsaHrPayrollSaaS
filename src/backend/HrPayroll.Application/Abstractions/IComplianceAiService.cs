namespace HrPayroll.Application.Abstractions;

public interface IComplianceAiService
{
    Task<ComplianceAiResult> GenerateBriefAsync(ComplianceAiInput input, CancellationToken cancellationToken);
}

public sealed record ComplianceAiInput(
    string Language,
    int Score,
    string Grade,
    decimal SaudizationPercent,
    bool WpsCompanyReady,
    int EmployeesMissingPaymentData,
    int CriticalAlerts,
    int WarningAlerts,
    int NoticeAlerts,
    IReadOnlyCollection<string> Recommendations,
    string? UserPrompt);

public sealed record ComplianceAiResult(
    string Provider,
    bool UsedFallback,
    string Text);
