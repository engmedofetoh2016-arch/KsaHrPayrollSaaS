using HrPayroll.Application.Abstractions;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace HrPayroll.Worker;

public class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;

    public Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var nextComplianceScanUtc = DateTime.MinValue;
        var nextComplianceScoreSnapshotUtc = DateTime.MinValue;
        var nextComplianceDigestCheckUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var nowUtc = dateTimeProvider.UtcNow;

            if (nowUtc >= nextComplianceScanUtc)
            {
                await SyncComplianceAlertsAsync(dbContext, nowUtc, stoppingToken);
                nextComplianceScanUtc = nowUtc.AddMinutes(30);
            }

            if (nowUtc >= nextComplianceScoreSnapshotUtc)
            {
                await RecordComplianceScoreSnapshotsAsync(dbContext, nowUtc, stoppingToken);
                nextComplianceScoreSnapshotUtc = nowUtc.AddHours(6);
            }

            if (nowUtc >= nextComplianceDigestCheckUtc)
            {
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                await ProcessComplianceDigestsAsync(dbContext, configuration, nowUtc, stoppingToken);
                nextComplianceDigestCheckUtc = nowUtc.AddMinutes(30);
            }

            var exportJob = await dbContext.ExportArtifacts
                .Where(x => x.Status == ExportArtifactStatus.Pending)
                .OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(stoppingToken);

            if (exportJob is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            exportJob.Status = ExportArtifactStatus.Processing;
            exportJob.ErrorMessage = null;
            await dbContext.SaveChangesAsync(stoppingToken);

            try
            {
                var fileData = exportJob.ArtifactType switch
                {
                    "PayrollRegisterCsv" => await GenerateRegisterCsvAsync(dbContext, exportJob.PayrollRunId, stoppingToken),
                    "GosiCsv" => await GenerateGosiCsvAsync(dbContext, exportJob.PayrollRunId, stoppingToken),
                    "WpsCsv" => await GenerateWpsCsvAsync(dbContext, exportJob.PayrollRunId, stoppingToken),
                    "PayslipPdf" when exportJob.EmployeeId.HasValue => await GeneratePayslipPdfAsync(dbContext, exportJob.PayrollRunId, exportJob.EmployeeId.Value, stoppingToken),
                    "FinalSettlementPdf" when exportJob.EmployeeId.HasValue => await GenerateFinalSettlementPdfAsync(dbContext, exportJob.EmployeeId.Value, exportJob.MetadataJson, stoppingToken),
                    _ => throw new InvalidOperationException($"Unsupported export type '{exportJob.ArtifactType}'.")
                };

                exportJob.FileData = fileData;
                exportJob.SizeBytes = fileData.Length;
                exportJob.Status = ExportArtifactStatus.Completed;
                exportJob.CompletedAtUtc = dateTimeProvider.UtcNow;
            }
            catch (Exception ex)
            {
                exportJob.Status = ExportArtifactStatus.Failed;
                exportJob.ErrorMessage = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
                _logger.LogError(ex, "Failed to process export job {ExportJobId}", exportJob.Id);
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }

    private static async Task<byte[]> GenerateRegisterCsvAsync(
        IApplicationDbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Payroll run not found.");
        }

        var rows = await (from line in dbContext.PayrollLines
                          join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                          where line.PayrollRunId == runId
                          orderby employee.FirstName, employee.LastName
                          select new
                          {
                              EmployeeName = employee.FirstName + " " + employee.LastName,
                              line.BaseSalary,
                              line.Allowances,
                              line.ManualDeductions,
                              line.UnpaidLeaveDays,
                              line.UnpaidLeaveDeduction,
                              line.GosiWageBase,
                              line.GosiEmployeeContribution,
                              line.GosiEmployerContribution,
                              line.Deductions,
                              line.OvertimeHours,
                              line.OvertimeAmount,
                              line.NetAmount
                          }).ToListAsync(cancellationToken);

        static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        var csv = new StringBuilder();
        csv.AppendLine("Employee,BaseSalary,Allowances,ManualDeductions,UnpaidLeaveDays,UnpaidLeaveDeduction,GosiWageBase,GosiEmployeeContribution,GosiEmployerContribution,Deductions,OvertimeHours,OvertimeAmount,NetAmount");
        foreach (var row in rows)
        {
            csv.AppendLine(
                $"{EscapeCsvValue(row.EmployeeName)},{row.BaseSalary:F2},{row.Allowances:F2},{row.ManualDeductions:F2},{row.UnpaidLeaveDays:F2},{row.UnpaidLeaveDeduction:F2},{row.GosiWageBase:F2},{row.GosiEmployeeContribution:F2},{row.GosiEmployerContribution:F2},{row.Deductions:F2},{row.OvertimeHours:F2},{row.OvertimeAmount:F2},{row.NetAmount:F2}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static async Task<byte[]> GeneratePayslipPdfAsync(
        IApplicationDbContext dbContext,
        Guid runId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Payroll run not found.");
        }

        var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
        if (period is null)
        {
            throw new InvalidOperationException("Payroll period not found.");
        }

        var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        var line = await dbContext.PayrollLines.FirstOrDefaultAsync(x => x.PayrollRunId == runId && x.EmployeeId == employeeId, cancellationToken);
        if (employee is null || line is null)
        {
            throw new InvalidOperationException("Payroll line not found for employee.");
        }

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                var currency = string.IsNullOrWhiteSpace(company?.CurrencyCode) ? "SAR" : company!.CurrencyCode;

                page.Content().Column(col =>
                {
                    col.Spacing(7);
                    col.Item().Text(company?.LegalName ?? "Company").FontSize(18).SemiBold();
                    col.Item().Text($"Payslip / مسير رواتب - {period.Year}/{period.Month:00}").FontSize(12).SemiBold();
                    col.Item().Text($"Employee / الموظف: {employee.FirstName} {employee.LastName}");
                    col.Item().Text($"Employee No. / الرقم الوظيفي: {employee.EmployeeNumber}");
                    col.Item().Text($"Job Title / المسمى الوظيفي: {employee.JobTitle}");
                    col.Item().Text($"Currency / العملة: {currency}");
                    col.Item().PaddingVertical(8).LineHorizontal(1);
                    col.Item().Text($"Base Salary / الراتب الأساسي: {line.BaseSalary:F2}");
                    col.Item().Text($"Allowances / البدلات: {line.Allowances:F2}");
                    col.Item().Text($"Overtime Hours / ساعات إضافية: {line.OvertimeHours:F2}");
                    col.Item().Text($"Overtime Amount / بدل الساعات الإضافية: {line.OvertimeAmount:F2}");
                    col.Item().Text($"GOSI Wage Base / وعاء التأمينات: {line.GosiWageBase:F2}");
                    col.Item().Text($"GOSI Employee Contribution / استقطاع الموظف للتأمينات: {line.GosiEmployeeContribution:F2}");
                    col.Item().Text($"GOSI Employer Contribution / مساهمة صاحب العمل للتأمينات: {line.GosiEmployerContribution:F2}");
                    col.Item().Text($"Manual Deductions / خصومات يدوية: {line.ManualDeductions:F2}");
                    col.Item().Text($"Unpaid Leave Days / أيام إجازة غير مدفوعة: {line.UnpaidLeaveDays:F2}");
                    col.Item().Text($"Unpaid Leave Deduction / خصم الإجازة غير المدفوعة: {line.UnpaidLeaveDeduction:F2}");
                    col.Item().Text($"Total Deductions / إجمالي الخصومات: {line.Deductions:F2}");
                    col.Item().PaddingVertical(8).LineHorizontal(1);
                    col.Item().Text($"Net Amount / صافي الراتب: {line.NetAmount:F2} {currency}").FontSize(14).Bold();
                });
            });
        }).GeneratePdf();
    }

    private static async Task<byte[]> GenerateGosiCsvAsync(
        IApplicationDbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Payroll run not found.");
        }

        var rows = await (from line in dbContext.PayrollLines
                          join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                          where line.PayrollRunId == runId
                          orderby employee.FirstName, employee.LastName
                          select new
                          {
                              EmployeeName = employee.FirstName + " " + employee.LastName,
                              employee.IsSaudiNational,
                              employee.IsGosiEligible,
                              line.GosiWageBase,
                              line.GosiEmployeeContribution,
                              line.GosiEmployerContribution
                          }).ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("Employee,IsSaudiNational,IsGosiEligible,GosiWageBase,EmployeeContribution,EmployerContribution");
        foreach (var row in rows)
        {
            csv.AppendLine(
                $"\"{row.EmployeeName.Replace("\"", "\"\"")}\",{row.IsSaudiNational},{row.IsGosiEligible},{row.GosiWageBase:F2},{row.GosiEmployeeContribution:F2},{row.GosiEmployerContribution:F2}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static async Task<byte[]> GenerateWpsCsvAsync(
        IApplicationDbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            throw new InvalidOperationException("Payroll run not found.");
        }

        var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
        if (period is null)
        {
            throw new InvalidOperationException("Payroll period not found.");
        }

        var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            throw new InvalidOperationException("Company profile not found.");
        }

        var paymentDate = new DateOnly(period.Year, period.Month, 1).AddMonths(1).AddDays(-1);

        var rows = await (from line in dbContext.PayrollLines
                          join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                          where line.PayrollRunId == runId
                          orderby employee.FirstName, employee.LastName
                          select new
                          {
                              employee.EmployeeNumber,
                              EmployeeName = employee.FirstName + " " + employee.LastName,
                              employee.BankName,
                              employee.BankIban,
                              line.BaseSalary,
                              line.Allowances,
                              line.OvertimeAmount,
                              line.Deductions,
                              line.NetAmount
                          }).ToListAsync(cancellationToken);

        static string EscapeCsvValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        var csv = new StringBuilder();
        csv.AppendLine("CompanyBankName,CompanyBankCode,CompanyIban,EmployeeNumber,EmployeeName,EmployeeBankName,EmployeeIban,GrossAmount,Deductions,NetAmount,Currency,PaymentDate,PayrollPeriod,Note");
        foreach (var row in rows)
        {
            var gross = Math.Round(row.BaseSalary + row.Allowances + row.OvertimeAmount, 2);
            csv.AppendLine(
                $"{EscapeCsvValue(company.WpsCompanyBankName)},{EscapeCsvValue(company.WpsCompanyBankCode)},{EscapeCsvValue(company.WpsCompanyIban)},{EscapeCsvValue(row.EmployeeNumber)},{EscapeCsvValue(row.EmployeeName)},{EscapeCsvValue(row.BankName)},{EscapeCsvValue(row.BankIban)},{gross:F2},{row.Deductions:F2},{row.NetAmount:F2},{company.CurrencyCode},{paymentDate:yyyy-MM-dd},{period.Year}-{period.Month:00},Template WPS v1");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static async Task<byte[]> GenerateFinalSettlementPdfAsync(
        IApplicationDbContext dbContext,
        Guid employeeId,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            throw new InvalidOperationException("Employee not found.");
        }

        var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            throw new InvalidOperationException("Company profile not found.");
        }

        var metadata = JsonSerializer.Deserialize<FinalSettlementPdfMetadata>(metadataJson ?? "{}");
        if (metadata is null)
        {
            throw new InvalidOperationException("Final settlement metadata is invalid.");
        }

        var terminationDate = metadata.TerminationDate;
        if (terminationDate < employee.StartDate)
        {
            throw new InvalidOperationException("Termination date cannot be before employee start date.");
        }

        var targetYear = metadata.Year ?? terminationDate.Year;
        var targetMonth = metadata.Month ?? terminationDate.Month;
        var periodStart = new DateOnly(targetYear, targetMonth, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (terminationDate < periodEnd)
        {
            periodEnd = terminationDate;
        }

        var manualDeductions = await dbContext.PayrollAdjustments
            .Where(x =>
                x.EmployeeId == employee.Id &&
                x.Year == targetYear &&
                x.Month == targetMonth &&
                x.Type == PayrollAdjustmentType.Deduction)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var unpaidLeaves = await dbContext.LeaveRequests
            .Where(x =>
                x.EmployeeId == employee.Id &&
                x.LeaveType == LeaveType.Unpaid &&
                x.Status == LeaveRequestStatus.Approved &&
                x.StartDate <= periodEnd &&
                x.EndDate >= periodStart)
            .ToListAsync(cancellationToken);

        var unpaidLeaveDays = unpaidLeaves.Sum(x =>
        {
            var overlapStart = x.StartDate > periodStart ? x.StartDate : periodStart;
            var overlapEnd = x.EndDate < periodEnd ? x.EndDate : periodEnd;
            return overlapEnd < overlapStart ? 0 : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
        });

        var dailyRate = employee.BaseSalary / 30m;
        var payableSalaryDays = Math.Max(0, periodEnd.DayNumber - periodStart.DayNumber + 1);
        var pendingSalaryAmount = Math.Round(payableSalaryDays * dailyRate, 2);
        var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

        var annualLeaveBalance = await dbContext.LeaveBalances
            .Where(x =>
                x.EmployeeId == employee.Id &&
                x.Year == targetYear &&
                x.LeaveType == LeaveType.Annual)
            .Select(x => new { x.AllocatedDays, x.UsedDays })
            .FirstOrDefaultAsync(cancellationToken);

        var leaveEncashmentDays = annualLeaveBalance is null
            ? 0m
            : Math.Max(0m, annualLeaveBalance.AllocatedDays - annualLeaveBalance.UsedDays);
        var leaveEncashmentAmount = Math.Round(leaveEncashmentDays * dailyRate, 2);

        var serviceDays = terminationDate.DayNumber - employee.StartDate.DayNumber + 1;
        var serviceYears = Math.Round(serviceDays / 365m, 4);
        var firstYears = Math.Min(serviceYears, 5m);
        var remainingYears = Math.Max(0m, serviceYears - 5m);
        var eosMonths = Math.Round(
            (firstYears * profile.EosFirstFiveYearsMonthFactor) +
            (remainingYears * profile.EosAfterFiveYearsMonthFactor),
            4);
        var eosAmount = Math.Round(eosMonths * employee.BaseSalary, 2);

        var additionalManualDeduction = Math.Round(metadata.AdditionalManualDeduction, 2);
        var totalDeductions = Math.Round(manualDeductions + additionalManualDeduction + unpaidLeaveDeduction, 2);
        var settlementGross = Math.Round(eosAmount + pendingSalaryAmount + leaveEncashmentAmount, 2);
        var netSettlement = Math.Round(settlementGross - totalDeductions, 2);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.Size(PageSizes.A4);
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"{profile.LegalName}").FontSize(18).SemiBold();
                    col.Item().Text("Final Settlement Letter / بيان التسوية النهائية").FontSize(13).SemiBold();
                    col.Item().Text($"Employee / الموظف: {employee.FirstName} {employee.LastName}");
                    col.Item().Text($"Start Date / تاريخ المباشرة: {employee.StartDate:dd-MM-yyyy}");
                    col.Item().Text($"Termination Date / تاريخ نهاية الخدمة: {terminationDate:dd-MM-yyyy}");
                    col.Item().Text($"Period / الفترة: {targetMonth:00}-{targetYear}");
                    col.Item().Text($"Currency / العملة: {profile.CurrencyCode}");
                    col.Item().PaddingVertical(8).LineHorizontal(1);
                    col.Item().Text($"EOS Amount / مكافأة نهاية الخدمة: {eosAmount:F2}");
                    col.Item().Text($"Pending Salary ({payableSalaryDays} days) / الراتب المستحق: {pendingSalaryAmount:F2}");
                    col.Item().Text($"Leave Encashment ({leaveEncashmentDays:F2} days) / بدل رصيد الإجازة: {leaveEncashmentAmount:F2}");
                    col.Item().Text($"Settlement Gross / إجمالي المستحقات: {settlementGross:F2}");
                    col.Item().Text($"Unpaid Leave Deduction / خصم إجازة غير مدفوعة: {unpaidLeaveDeduction:F2}");
                    col.Item().Text($"Manual Deductions (Payroll) / خصومات الرواتب: {manualDeductions:F2}");
                    col.Item().Text($"Additional Manual Deduction / خصم إضافي: {additionalManualDeduction:F2}");
                    col.Item().Text($"Total Deductions / إجمالي الخصومات: {totalDeductions:F2}");
                    col.Item().PaddingVertical(8).LineHorizontal(1);
                    col.Item().Text($"Net Final Settlement / صافي التسوية النهائية: {netSettlement:F2} {profile.CurrencyCode}").FontSize(14).Bold();
                    col.Item().PaddingTop(8).Text($"Service Years / سنوات الخدمة: {serviceYears:F4}");
                    col.Item().Text($"EOS Months / أشهر المكافأة: {eosMonths:F4}");
                    col.Item().Text($"Payable Salary Days / أيام الراتب المستحق: {payableSalaryDays}");
                    col.Item().Text($"Leave Encashment Days / أيام بدل الإجازة: {leaveEncashmentDays:F2}");
                    col.Item().Text($"Unpaid Leave Days / أيام الإجازة غير المدفوعة: {unpaidLeaveDays}");
                    if (!string.IsNullOrWhiteSpace(metadata.Notes))
                    {
                        col.Item().PaddingTop(6).Text($"Notes / ملاحظات: {metadata.Notes}");
                    }
                });
            });
        }).GeneratePdf();
    }

    private async Task SyncComplianceAlertsAsync(
        IApplicationDbContext dbContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(nowUtc.Date);
        var maxDays = 60;

        var employees = await dbContext.Employees
            .Select(x => new
            {
                x.Id,
                x.TenantId,
                x.FirstName,
                x.LastName,
                x.IsSaudiNational,
                x.EmployeeNumber,
                x.IqamaNumber,
                x.IqamaExpiryDate,
                x.WorkPermitExpiryDate,
                x.ContractEndDate
            })
            .ToListAsync(cancellationToken);

        var openAlerts = await dbContext.ComplianceAlerts
            .Where(x => !x.IsResolved)
            .ToListAsync(cancellationToken);

        var openByKey = openAlerts.ToDictionary(
            x => BuildAlertKey(x.TenantId, x.EmployeeId, x.DocumentType, x.ExpiryDate),
            x => x,
            StringComparer.Ordinal);

        var detectedKeys = new HashSet<string>(StringComparer.Ordinal);
        var changes = 0;

        foreach (var employee in employees)
        {
            var employeeName = $"{employee.FirstName} {employee.LastName}".Trim();

            if (employee.IqamaExpiryDate.HasValue)
            {
                changes += UpsertComplianceAlert(
                    dbContext,
                    openByKey,
                    detectedKeys,
                    employee.TenantId,
                    employee.Id,
                    employeeName,
                    employee.IsSaudiNational,
                    "Iqama",
                    employee.IqamaNumber,
                    employee.IqamaExpiryDate.Value,
                    today,
                    maxDays,
                    nowUtc);
            }

            if (employee.WorkPermitExpiryDate.HasValue)
            {
                changes += UpsertComplianceAlert(
                    dbContext,
                    openByKey,
                    detectedKeys,
                    employee.TenantId,
                    employee.Id,
                    employeeName,
                    employee.IsSaudiNational,
                    "WorkPermit",
                    employee.IqamaNumber,
                    employee.WorkPermitExpiryDate.Value,
                    today,
                    maxDays,
                    nowUtc);
            }

            if (employee.ContractEndDate.HasValue)
            {
                changes += UpsertComplianceAlert(
                    dbContext,
                    openByKey,
                    detectedKeys,
                    employee.TenantId,
                    employee.Id,
                    employeeName,
                    employee.IsSaudiNational,
                    "Contract",
                    employee.EmployeeNumber,
                    employee.ContractEndDate.Value,
                    today,
                    maxDays,
                    nowUtc);
            }
        }

        foreach (var existing in openAlerts)
        {
            var key = BuildAlertKey(existing.TenantId, existing.EmployeeId, existing.DocumentType, existing.ExpiryDate);
            if (detectedKeys.Contains(key))
            {
                continue;
            }

            existing.IsResolved = true;
            existing.ResolveReason = "AutoResolvedByScan";
            existing.ResolvedAtUtc = nowUtc;
            existing.UpdatedAtUtc = nowUtc;
            changes++;
        }

        if (changes > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Compliance alert scan completed. Changes applied: {Changes}", changes);
        }
    }

    private async Task RecordComplianceScoreSnapshotsAsync(
        IApplicationDbContext dbContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var snapshotDate = DateOnly.FromDateTime(nowUtc.Date);
        var tenantIds = await dbContext.Tenants
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
        {
            return;
        }

        var changes = 0;
        foreach (var tenantId in tenantIds)
        {
            var score = await BuildComplianceScoreForTenantAsync(dbContext, tenantId, cancellationToken);
            var existing = await dbContext.ComplianceScoreSnapshots
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SnapshotDate == snapshotDate, cancellationToken);

            if (existing is null)
            {
                dbContext.AddEntity(new ComplianceScoreSnapshot
                {
                    TenantId = tenantId,
                    SnapshotDate = snapshotDate,
                    Score = score.Score,
                    Grade = score.Grade,
                    SaudizationPercent = score.SaudizationPercent,
                    WpsCompanyReady = score.WpsCompanyReady,
                    EmployeesMissingPaymentData = score.EmployeesMissingPaymentData,
                    CriticalAlerts = score.CriticalAlerts,
                    WarningAlerts = score.WarningAlerts,
                    NoticeAlerts = score.NoticeAlerts
                });
                changes++;
                continue;
            }

            existing.Score = score.Score;
            existing.Grade = score.Grade;
            existing.SaudizationPercent = score.SaudizationPercent;
            existing.WpsCompanyReady = score.WpsCompanyReady;
            existing.EmployeesMissingPaymentData = score.EmployeesMissingPaymentData;
            existing.CriticalAlerts = score.CriticalAlerts;
            existing.WarningAlerts = score.WarningAlerts;
            existing.NoticeAlerts = score.NoticeAlerts;
            existing.UpdatedAtUtc = nowUtc;
            changes++;
        }

        if (changes > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Compliance score snapshots updated for {TenantCount} tenants.", tenantIds.Count);
        }
    }

    private async Task ProcessComplianceDigestsAsync(
        IApplicationDbContext dbContext,
        IConfiguration configuration,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var profiles = await dbContext.CompanyProfiles
            .Where(x => x.ComplianceDigestEnabled && !string.IsNullOrWhiteSpace(x.ComplianceDigestEmail))
            .Select(x => new
            {
                x.TenantId,
                x.LegalName,
                x.ComplianceDigestEmail,
                x.ComplianceDigestFrequency,
                x.ComplianceDigestHourUtc,
                x.LastComplianceDigestSentAtUtc
            })
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return;
        }

        foreach (var profile in profiles)
        {
            if (!ShouldSendDigest(profile.ComplianceDigestFrequency, profile.ComplianceDigestHourUtc, profile.LastComplianceDigestSentAtUtc, nowUtc))
            {
                continue;
            }

            var score = await BuildComplianceScoreForTenantAsync(dbContext, profile.TenantId, cancellationToken);
            var topAlerts = await dbContext.ComplianceAlerts
                .Where(x => x.TenantId == profile.TenantId && !x.IsResolved)
                .OrderBy(x => x.DaysLeft)
                .ThenBy(x => x.EmployeeName)
                .Take(10)
                .Select(x => new ComplianceAlertDigestItem(x.EmployeeName, x.DocumentType, x.DaysLeft, x.Severity, x.ExpiryDate))
                .ToListAsync(cancellationToken);

            var digestBodyText = BuildDigestBodyText(profile.LegalName, nowUtc, score, topAlerts);
            var digestBodyHtml = BuildDigestBodyHtml(profile.LegalName, nowUtc, score, topAlerts);
            var sendResult = await TrySendEmailAsync(
                configuration,
                profile.ComplianceDigestEmail,
                $"Compliance Digest - {profile.LegalName} - {nowUtc:yyyy-MM-dd}",
                digestBodyText,
                digestBodyHtml,
                cancellationToken);

            if (sendResult.Sent)
            {
                var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == profile.TenantId, cancellationToken);
                if (company is not null)
                {
                    company.LastComplianceDigestSentAtUtc = nowUtc;
                    company.UpdatedAtUtc = nowUtc;
                }
                dbContext.AddEntity(new ComplianceDigestDelivery
                {
                    TenantId = profile.TenantId,
                    RecipientEmail = profile.ComplianceDigestEmail,
                    Subject = $"Compliance Digest - {profile.LegalName} - {nowUtc:yyyy-MM-dd}",
                    TriggerType = "Scheduled",
                    Frequency = string.IsNullOrWhiteSpace(profile.ComplianceDigestFrequency) ? "Weekly" : profile.ComplianceDigestFrequency,
                    Status = sendResult.Simulated ? "Simulated" : "Sent",
                    Simulated = sendResult.Simulated,
                    ErrorMessage = string.Empty,
                    Score = score.Score,
                    Grade = score.Grade,
                    SentAtUtc = nowUtc
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Compliance digest sent for tenant {TenantId} to {Email}.", profile.TenantId, profile.ComplianceDigestEmail);
            }
            else
            {
                dbContext.AddEntity(new ComplianceDigestDelivery
                {
                    TenantId = profile.TenantId,
                    RecipientEmail = profile.ComplianceDigestEmail,
                    Subject = $"Compliance Digest - {profile.LegalName} - {nowUtc:yyyy-MM-dd}",
                    TriggerType = "Scheduled",
                    Frequency = string.IsNullOrWhiteSpace(profile.ComplianceDigestFrequency) ? "Weekly" : profile.ComplianceDigestFrequency,
                    Status = "Failed",
                    Simulated = false,
                    ErrorMessage = sendResult.ErrorMessage ?? "Failed to send SMTP digest.",
                    Score = score.Score,
                    Grade = score.Grade,
                    SentAtUtc = null
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static bool ShouldSendDigest(string frequency, int hourUtc, DateTime? lastSentAtUtc, DateTime nowUtc)
    {
        if (nowUtc.Hour < Math.Clamp(hourUtc, 0, 23))
        {
            return false;
        }

        var normalizedFrequency = string.IsNullOrWhiteSpace(frequency) ? "Weekly" : frequency.Trim();
        if (string.Equals(normalizedFrequency, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            return !lastSentAtUtc.HasValue || lastSentAtUtc.Value.Date < nowUtc.Date;
        }

        if (nowUtc.DayOfWeek != DayOfWeek.Monday)
        {
            return false;
        }

        return !lastSentAtUtc.HasValue || lastSentAtUtc.Value.Date < nowUtc.Date;
    }

    private static string BuildDigestBodyText(
        string companyName,
        DateTime nowUtc,
        ComplianceScoreSnapshotData score,
        IReadOnlyCollection<ComplianceAlertDigestItem> topAlerts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Compliance Digest for {companyName}");
        sb.AppendLine($"Generated at (UTC): {nowUtc:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Score: {score.Score}/100 ({score.Grade})");
        sb.AppendLine($"Saudization: {score.SaudizationPercent:F1}%");
        sb.AppendLine($"WPS Company Ready: {score.WpsCompanyReady}");
        sb.AppendLine($"Employees Missing Payment Data: {score.EmployeesMissingPaymentData}");
        sb.AppendLine($"Open Alerts: Critical={score.CriticalAlerts}, Warning={score.WarningAlerts}, Notice={score.NoticeAlerts}");
        sb.AppendLine();
        sb.AppendLine("Top Alerts:");
        foreach (var alert in topAlerts)
        {
            sb.AppendLine($"- {alert.EmployeeName}: {alert.DocumentType} | {alert.Severity} | DaysLeft={alert.DaysLeft} | Expiry={alert.ExpiryDate:yyyy-MM-dd}");
        }

        if (topAlerts.Count == 0)
        {
            sb.AppendLine("- No open alerts.");
        }

        sb.AppendLine();
        sb.AppendLine("Action Priority: close critical in 7 days, warning in 30 days, and keep WPS/payment data complete.");
        return sb.ToString();
    }

    private static string BuildDigestBodyHtml(
        string companyName,
        DateTime nowUtc,
        ComplianceScoreSnapshotData score,
        IReadOnlyCollection<ComplianceAlertDigestItem> topAlerts)
    {
        var encodedCompany = WebUtility.HtmlEncode(companyName);
        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#14223b;\">");
        sb.Append($"<h2 style=\"margin-bottom:4px;\">Compliance Digest / ملخص الامتثال - {encodedCompany}</h2>");
        sb.Append($"<p style=\"margin-top:0;color:#4e6684;\">Generated (UTC): {nowUtc:yyyy-MM-dd HH:mm}</p>");
        sb.Append("<table style=\"border-collapse:collapse;width:100%;max-width:760px;\">");
        sb.Append("<tr><th style=\"text-align:left;border:1px solid #dbe6f2;padding:8px;background:#f4f8fd;\">Metric</th><th style=\"text-align:left;border:1px solid #dbe6f2;padding:8px;background:#f4f8fd;\">Value</th></tr>");
        sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Score / الدرجة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.Score}/100 ({WebUtility.HtmlEncode(score.Grade)})</td></tr>");
        sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Saudization / السعودة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.SaudizationPercent:F1}%</td></tr>");
        sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">WPS Ready / جاهزية WPS</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.WpsCompanyReady}</td></tr>");
        sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Missing Payment Data / نقص بيانات الدفع</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.EmployeesMissingPaymentData}</td></tr>");
        sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Open Alerts / التنبيهات المفتوحة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">Critical={score.CriticalAlerts}, Warning={score.WarningAlerts}, Notice={score.NoticeAlerts}</td></tr>");
        sb.Append("</table>");

        sb.Append("<h3 style=\"margin-top:18px;\">Top Alerts / أهم التنبيهات</h3>");
        if (topAlerts.Count == 0)
        {
            sb.Append("<p>No open alerts / لا توجد تنبيهات مفتوحة.</p>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var alert in topAlerts)
            {
                sb.Append("<li>");
                sb.Append(WebUtility.HtmlEncode($"{alert.EmployeeName}: {alert.DocumentType} | {alert.Severity} | DaysLeft={alert.DaysLeft} | Expiry={alert.ExpiryDate:yyyy-MM-dd}"));
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("<p style=\"margin-top:16px;color:#344b66;\">Action Priority: close critical in 7 days, warning in 30 days, and keep WPS/payment data complete.</p>");
        sb.Append("<p style=\"color:#344b66;\">أولوية العمل: إغلاق التنبيهات الحرجة خلال 7 أيام، والمتوسطة خلال 30 يوما، واستكمال بيانات WPS والدفع.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private async Task<EmailSendResult> TrySendEmailAsync(
        IConfiguration configuration,
        string toEmail,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"];
        var port = int.TryParse(configuration["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        var fromEmail = configuration["Smtp:FromEmail"] ?? configuration["Smtp:From"];
        var fromName = configuration["Smtp:FromName"] ?? "HR Payroll Compliance";
        var enableSsl = !string.Equals(configuration["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogInformation("SMTP is not configured. Digest prepared for {ToEmail} with subject '{Subject}'.", toEmail, subject);
            _logger.LogInformation("Digest body:\n{Body}", textBody);
            return new EmailSendResult(true, true, null);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(fromEmail, string.IsNullOrWhiteSpace(fromName) ? "HR Payroll Compliance" : fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(toEmail);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            client.Credentials = new NetworkCredential(username, password ?? string.Empty);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(mail, cancellationToken);
            return new EmailSendResult(true, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send compliance digest email to {ToEmail}.", toEmail);
            return new EmailSendResult(false, false, ex.Message);
        }
    }

    private static async Task<ComplianceScoreSnapshotData> BuildComplianceScoreForTenantAsync(
        IApplicationDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var company = await dbContext.CompanyProfiles
            .Where(x => x.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        var employeeItems = await dbContext.Employees
            .Where(x => x.TenantId == tenantId)
            .Select(x => new
            {
                x.IsSaudiNational,
                x.EmployeeNumber,
                x.BankName,
                x.BankIban
            })
            .ToListAsync(cancellationToken);

        var totalEmployees = employeeItems.Count;
        var saudiEmployees = employeeItems.Count(x => x.IsSaudiNational);
        var saudizationPercent = totalEmployees == 0 ? 0m : Math.Round(saudiEmployees * 100m / totalEmployees, 1);
        var saudizationTargetPercent = company is null
            ? 30m
            : Math.Clamp(company.NitaqatTargetPercent, 0m, 100m);

        var wpsCompanyReady = company is not null &&
                              !string.IsNullOrWhiteSpace(company.WpsCompanyBankName) &&
                              !string.IsNullOrWhiteSpace(company.WpsCompanyBankCode) &&
                              !string.IsNullOrWhiteSpace(company.WpsCompanyIban);

        var employeesMissingPaymentData = employeeItems.Count(x =>
            string.IsNullOrWhiteSpace(x.EmployeeNumber) ||
            string.IsNullOrWhiteSpace(x.BankName) ||
            string.IsNullOrWhiteSpace(x.BankIban));

        var openAlerts = await dbContext.ComplianceAlerts
            .Where(x => x.TenantId == tenantId && !x.IsResolved)
            .Select(x => new { x.Severity })
            .ToListAsync(cancellationToken);

        var criticalAlerts = openAlerts.Count(x => x.Severity == "Critical");
        var warningAlerts = openAlerts.Count(x => x.Severity == "Warning");
        var noticeAlerts = openAlerts.Count(x => x.Severity == "Notice");

        var score = 100m;
        if (!wpsCompanyReady)
        {
            score -= 20m;
        }

        score -= Math.Min(20m, employeesMissingPaymentData * 2m);
        score -= Math.Min(20m, criticalAlerts * 4m);
        score -= Math.Min(10m, warningAlerts * 2m);
        score -= Math.Min(5m, noticeAlerts);

        if (saudizationPercent < saudizationTargetPercent)
        {
            score -= Math.Min(20m, (saudizationTargetPercent - saudizationPercent) * 1.2m);
        }

        var finalScore = (int)Math.Clamp(Math.Round(score), 0m, 100m);
        var grade = finalScore >= 90 ? "A" :
            finalScore >= 80 ? "B" :
            finalScore >= 70 ? "C" : "D";

        return new ComplianceScoreSnapshotData(
            finalScore,
            grade,
            saudizationPercent,
            wpsCompanyReady,
            employeesMissingPaymentData,
            criticalAlerts,
            warningAlerts,
            noticeAlerts);
    }

    private static int UpsertComplianceAlert(
        IApplicationDbContext dbContext,
        Dictionary<string, ComplianceAlert> openByKey,
        HashSet<string> detectedKeys,
        Guid tenantId,
        Guid employeeId,
        string employeeName,
        bool isSaudiNational,
        string documentType,
        string documentNumber,
        DateOnly expiryDate,
        DateOnly today,
        int maxDays,
        DateTime nowUtc)
    {
        var daysLeft = expiryDate.DayNumber - today.DayNumber;
        if (daysLeft < 0 || daysLeft > maxDays)
        {
            return 0;
        }

        var key = BuildAlertKey(tenantId, employeeId, documentType, expiryDate);
        detectedKeys.Add(key);
        var severity = ResolveSeverity(daysLeft);

        if (openByKey.TryGetValue(key, out var existing))
        {
            existing.EmployeeName = employeeName;
            existing.IsSaudiNational = isSaudiNational;
            existing.DocumentNumber = documentNumber ?? string.Empty;
            existing.DaysLeft = daysLeft;
            existing.Severity = severity;
            existing.LastDetectedAtUtc = nowUtc;
            existing.UpdatedAtUtc = nowUtc;
            return 1;
        }

        var created = new ComplianceAlert
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            IsSaudiNational = isSaudiNational,
            DocumentType = documentType,
            DocumentNumber = documentNumber ?? string.Empty,
            ExpiryDate = expiryDate,
            DaysLeft = daysLeft,
            Severity = severity,
            IsResolved = false,
            ResolveReason = string.Empty,
            ResolvedByUserId = null,
            ResolvedAtUtc = null,
            LastDetectedAtUtc = nowUtc
        };

        dbContext.AddEntity(created);
        openByKey[key] = created;
        return 1;
    }

    private static string ResolveSeverity(int daysLeft)
    {
        if (daysLeft <= 7)
        {
            return "Critical";
        }

        if (daysLeft <= 30)
        {
            return "Warning";
        }

        return "Notice";
    }

    private static string BuildAlertKey(Guid tenantId, Guid employeeId, string documentType, DateOnly expiryDate)
        => $"{tenantId:N}:{employeeId:N}:{documentType}:{expiryDate:yyyyMMdd}";

    private sealed record ComplianceScoreSnapshotData(
        int Score,
        string Grade,
        decimal SaudizationPercent,
        bool WpsCompanyReady,
        int EmployeesMissingPaymentData,
        int CriticalAlerts,
        int WarningAlerts,
        int NoticeAlerts);
    private sealed record ComplianceAlertDigestItem(
        string EmployeeName,
        string DocumentType,
        int DaysLeft,
        string Severity,
        DateOnly ExpiryDate);
    private sealed record EmailSendResult(bool Sent, bool Simulated, string? ErrorMessage);

    private sealed record FinalSettlementPdfMetadata(
        DateOnly TerminationDate,
        int? Year,
        int? Month,
        decimal AdditionalManualDeduction,
        string? Notes);
}

