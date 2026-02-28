using FluentValidation;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Api.Validation;

public class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.TenantName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(120).Matches("^[a-zA-Z0-9-]+$");
        RuleFor(x => x.CompanyLegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.DefaultPayDay).InclusiveBetween(1, 31);
        RuleFor(x => x.OwnerFirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.OwnerLastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(8);
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x)
            .Must(x =>
                (x.TenantId.HasValue && x.TenantId.Value != Guid.Empty) ||
                !string.IsNullOrWhiteSpace(x.TenantSlug))
            .WithMessage("Either tenantId or tenantSlug is required.");
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.TenantSlug).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.TenantSlug).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public class UpdateCompanyProfileRequestValidator : AbstractValidator<UpdateCompanyProfileRequest>
{
    public UpdateCompanyProfileRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.DefaultPayDay).InclusiveBetween(1, 31);
        RuleFor(x => x.EosFirstFiveYearsMonthFactor).InclusiveBetween(0m, 2m);
        RuleFor(x => x.EosAfterFiveYearsMonthFactor).InclusiveBetween(0m, 2m);
        RuleFor(x => x.WpsCompanyBankName).MaximumLength(120);
        RuleFor(x => x.WpsCompanyBankCode).MaximumLength(50);
        RuleFor(x => x.WpsCompanyIban).MaximumLength(34);
        RuleFor(x => x.ComplianceDigestEmail).MaximumLength(200);
        RuleFor(x => x.ComplianceDigestHourUtc).InclusiveBetween(0, 23);
        RuleFor(x => x.NitaqatActivity).MaximumLength(120);
        RuleFor(x => x.NitaqatSizeBand).MaximumLength(60);
        RuleFor(x => x.NitaqatTargetPercent).InclusiveBetween(0m, 100m);
        RuleFor(x => x.ComplianceDigestFrequency)
            .Must(x => string.IsNullOrWhiteSpace(x) || string.Equals(x, "Daily", StringComparison.OrdinalIgnoreCase) || string.Equals(x, "Weekly", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Compliance digest frequency must be Daily or Weekly.");
        RuleFor(x => x.ComplianceDigestEmail)
            .EmailAddress()
            .When(x => x.ComplianceDigestEnabled && !string.IsNullOrWhiteSpace(x.ComplianceDigestEmail));
        RuleFor(x => x.ComplianceDigestEmail)
            .NotEmpty()
            .When(x => x.ComplianceDigestEnabled)
            .WithMessage("Compliance digest email is required when digest is enabled.");
    }
}

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public class AdminResetUserPasswordRequestValidator : AbstractValidator<AdminResetUserPasswordRequest>
{
    public AdminResetUserPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BaseSalary).GreaterThan(0);
        RuleFor(x => x.GosiBasicWage).GreaterThanOrEqualTo(0);
        RuleFor(x => x.GosiHousingAllowance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EmployeeNumber).MaximumLength(50);
        RuleFor(x => x.BankName).MaximumLength(120);
        RuleFor(x => x.BankIban).MaximumLength(34);
        RuleFor(x => x.IqamaNumber).MaximumLength(50);
        RuleFor(x => x.IqamaExpiryDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("iqamaExpiryDate is invalid.");
        RuleFor(x => x.WorkPermitExpiryDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("workPermitExpiryDate is invalid.");
        RuleFor(x => x.ContractEndDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("contractEndDate is invalid.");
        RuleFor(x => x)
            .Must(x => !x.IsGosiEligible || x.GosiBasicWage > 0)
            .WithMessage("GOSI basic wage must be greater than 0 when employee is GOSI eligible.");
    }
}

public class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BaseSalary).GreaterThan(0);
        RuleFor(x => x.GosiBasicWage).GreaterThanOrEqualTo(0);
        RuleFor(x => x.GosiHousingAllowance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EmployeeNumber).MaximumLength(50);
        RuleFor(x => x.BankName).MaximumLength(120);
        RuleFor(x => x.BankIban).MaximumLength(34);
        RuleFor(x => x.IqamaNumber).MaximumLength(50);
        RuleFor(x => x.IqamaExpiryDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("iqamaExpiryDate is invalid.");
        RuleFor(x => x.WorkPermitExpiryDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("workPermitExpiryDate is invalid.");
        RuleFor(x => x.ContractEndDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("contractEndDate is invalid.");
        RuleFor(x => x)
            .Must(x => !x.IsGosiEligible || x.GosiBasicWage > 0)
            .WithMessage("GOSI basic wage must be greater than 0 when employee is GOSI eligible.");
    }
}

public class CreateEmployeeLoginRequestValidator : AbstractValidator<CreateEmployeeLoginRequest>
{
    public CreateEmployeeLoginRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public class EstimateEosRequestValidator : AbstractValidator<EstimateEosRequest>
{
    public EstimateEosRequestValidator()
    {
        RuleFor(x => x.TerminationDate)
            .Must(x => x is null || x.Value >= DateOnly.FromDateTime(new DateTime(2000, 1, 1)))
            .WithMessage("terminationDate is invalid.");
    }
}

public class FinalSettlementEstimateRequestValidator : AbstractValidator<FinalSettlementEstimateRequest>
{
    public FinalSettlementEstimateRequestValidator()
    {
        RuleFor(x => x.TerminationDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(new DateTime(2000, 1, 1)));

        RuleFor(x => x)
            .Must(x => x.Year.HasValue == x.Month.HasValue)
            .WithMessage("year and month must be provided together.");

        RuleFor(x => x.Year!.Value)
            .InclusiveBetween(2000, 2100)
            .When(x => x.Year.HasValue);

        RuleFor(x => x.Month!.Value)
            .InclusiveBetween(1, 12)
            .When(x => x.Month.HasValue);

        RuleFor(x => x.AdditionalManualDeduction)
            .GreaterThanOrEqualTo(0m);

        RuleFor(x => x.Notes)
            .MaximumLength(300);
    }
}

public class ResolveComplianceAlertRequestValidator : AbstractValidator<ResolveComplianceAlertRequest>
{
    public ResolveComplianceAlertRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(200);
    }
}

public class ComplianceAiBriefRequestValidator : AbstractValidator<ComplianceAiBriefRequest>
{
    public ComplianceAiBriefRequestValidator()
    {
        RuleFor(x => x.Prompt).MaximumLength(500);
        RuleFor(x => x.Language).MaximumLength(10);
    }
}

public class SmartAlertAcknowledgeRequestValidator : AbstractValidator<SmartAlertAcknowledgeRequest>
{
    public SmartAlertAcknowledgeRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public class SmartAlertSnoozeRequestValidator : AbstractValidator<SmartAlertSnoozeRequest>
{
    public SmartAlertSnoozeRequestValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(1, 30);
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public class ResolveComplianceAlertsBulkRequestValidator : AbstractValidator<ResolveComplianceAlertsBulkRequest>
{
    public ResolveComplianceAlertsBulkRequestValidator()
    {
        RuleFor(x => x.AlertIds)
            .NotNull()
            .Must(x => x.Count > 0)
            .WithMessage("At least one alert id is required.")
            .Must(x => x.Count <= 200)
            .WithMessage("Maximum 200 alert ids per request.");

        RuleForEach(x => x.AlertIds)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .MaximumLength(200);
    }
}

public class UpsertAttendanceInputRequestValidator : AbstractValidator<UpsertAttendanceInputRequest>
{
    public UpsertAttendanceInputRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.DaysPresent).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DaysAbsent).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OvertimeHours).GreaterThanOrEqualTo(0);
    }
}

public class CreatePayrollPeriodRequestValidator : AbstractValidator<CreatePayrollPeriodRequest>
{
    public CreatePayrollPeriodRequestValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.PeriodEndDate).GreaterThanOrEqualTo(x => x.PeriodStartDate);
    }
}

public class CreateLeaveRequestRequestValidator : AbstractValidator<CreateLeaveRequestRequest>
{
    public CreateLeaveRequestRequestValidator()
    {
        RuleFor(x => x.LeaveType).Must(t => t is LeaveType.Annual or LeaveType.Sick or LeaveType.Unpaid);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class LeaveBalancePreviewRequestValidator : AbstractValidator<LeaveBalancePreviewRequest>
{
    public LeaveBalancePreviewRequestValidator()
    {
        RuleFor(x => x.LeaveType).Must(t => t is LeaveType.Annual or LeaveType.Sick or LeaveType.Unpaid);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).NotEmpty().GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public class UpsertLeaveBalanceRequestValidator : AbstractValidator<UpsertLeaveBalanceRequest>
{
    public UpsertLeaveBalanceRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.LeaveType).Must(t => t is LeaveType.Annual or LeaveType.Sick or LeaveType.Unpaid);
        RuleFor(x => x.AllocatedDays).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.UsedDays).GreaterThanOrEqualTo(0m);
        RuleFor(x => x)
            .Must(x => x.UsedDays <= x.AllocatedDays)
            .WithMessage("usedDays cannot exceed allocatedDays.");
    }
}

public class ApproveLeaveRequestRequestValidator : AbstractValidator<ApproveLeaveRequestRequest>
{
    public ApproveLeaveRequestRequestValidator()
    {
        RuleFor(x => x.Comment).MaximumLength(500);
    }
}

public class RejectLeaveRequestRequestValidator : AbstractValidator<RejectLeaveRequestRequest>
{
    public RejectLeaveRequestRequestValidator()
    {
        RuleFor(x => x.RejectionReason).NotEmpty().MaximumLength(500);
    }
}

public class CreatePayrollAdjustmentRequestValidator : AbstractValidator<CreatePayrollAdjustmentRequest>
{
    public CreatePayrollAdjustmentRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Type).Must(t => t == PayrollAdjustmentType.Allowance || t == PayrollAdjustmentType.Deduction);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class CalculatePayrollRunRequestValidator : AbstractValidator<CalculatePayrollRunRequest>
{
    public CalculatePayrollRunRequestValidator()
    {
        RuleFor(x => x.PayrollPeriodId).NotEmpty();
    }
}

public class CreateEmployeeLoanRequestValidator : AbstractValidator<CreateEmployeeLoanRequest>
{
    public CreateEmployeeLoanRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.LoanType).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PrincipalAmount).GreaterThan(0m);
        RuleFor(x => x.InstallmentAmount).GreaterThan(0m);
        RuleFor(x => x.StartYear).InclusiveBetween(2000, 2100);
        RuleFor(x => x.StartMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.TotalInstallments).InclusiveBetween(1, 120);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x)
            .Must(x => x.InstallmentAmount * x.TotalInstallments >= x.PrincipalAmount)
            .WithMessage("Installment plan total must cover principal amount.");
        RuleFor(x => x)
            .Must(x => x.TotalInstallments == 1 || x.InstallmentAmount * (x.TotalInstallments - 1) < x.PrincipalAmount)
            .WithMessage("Installment amount is too high for selected number of installments.");
    }
}

public class RescheduleEmployeeLoanRequestValidator : AbstractValidator<RescheduleEmployeeLoanRequest>
{
    public RescheduleEmployeeLoanRequestValidator()
    {
        RuleFor(x => x.StartYear).InclusiveBetween(2000, 2100);
        RuleFor(x => x.StartMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public class SkipEmployeeLoanInstallmentRequestValidator : AbstractValidator<SkipEmployeeLoanInstallmentRequest>
{
    public SkipEmployeeLoanInstallmentRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public class SettleEmployeeLoanRequestValidator : AbstractValidator<SettleEmployeeLoanRequest>
{
    public SettleEmployeeLoanRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0m)
            .When(x => x.Amount.HasValue);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public class CreateEmployeeOffboardingRequestValidator : AbstractValidator<CreateEmployeeOffboardingRequest>
{
    public CreateEmployeeOffboardingRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.EffectiveDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(new DateTime(2000, 1, 1)));
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ChecklistRoleName).MaximumLength(80);
    }
}

public class CreateOffboardingChecklistTemplateRequestValidator : AbstractValidator<CreateOffboardingChecklistTemplateRequest>
{
    public CreateOffboardingChecklistTemplateRequestValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(50).Matches("^[A-Z0-9_\\-a-z]+$");
        RuleFor(x => x.ItemLabel).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).InclusiveBetween(1, 1000);
    }
}

public class ApplyOffboardingChecklistTemplateRequestValidator : AbstractValidator<ApplyOffboardingChecklistTemplateRequest>
{
    public ApplyOffboardingChecklistTemplateRequestValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().MaximumLength(80);
    }
}

public class CreateOffboardingChecklistItemRequestValidator : AbstractValidator<CreateOffboardingChecklistItemRequest>
{
    public CreateOffboardingChecklistItemRequestValidator()
    {
        RuleFor(x => x.ItemCode).NotEmpty().MaximumLength(50).Matches("^[A-Z0-9_\\-]+$");
        RuleFor(x => x.ItemLabel).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).InclusiveBetween(1, 1000);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class OffboardingChecklistApprovalRequestValidator : AbstractValidator<OffboardingChecklistApprovalRequest>
{
    public OffboardingChecklistApprovalRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class OffboardingChecklistEsignRequestValidator : AbstractValidator<OffboardingChecklistEsignRequest>
{
    public OffboardingChecklistEsignRequestValidator()
    {
        RuleFor(x => x.DocumentName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DocumentUrl).NotEmpty().MaximumLength(500);
    }
}

public class CompleteOffboardingChecklistItemRequestValidator : AbstractValidator<CompleteOffboardingChecklistItemRequest>
{
    public CompleteOffboardingChecklistItemRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class CreateShiftRuleRequestValidator : AbstractValidator<CreateShiftRuleRequest>
{
    public CreateShiftRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.StandardDailyHours).InclusiveBetween(1m, 24m);
        RuleFor(x => x.OvertimeMultiplierWeekday).InclusiveBetween(1m, 5m);
        RuleFor(x => x.OvertimeMultiplierWeekend).InclusiveBetween(1m, 5m);
        RuleFor(x => x.OvertimeMultiplierHoliday).InclusiveBetween(1m, 5m);
        RuleFor(x => x.WeekendDaysCsv).NotEmpty().MaximumLength(40);
    }
}

public class UpsertTimesheetEntryRequestValidator : AbstractValidator<UpsertTimesheetEntryRequest>
{
    public UpsertTimesheetEntryRequestValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.WorkDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(new DateTime(2000, 1, 1)));
        RuleFor(x => x.HoursWorked).InclusiveBetween(0m, 24m);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class ApproveTimesheetEntryRequestValidator : AbstractValidator<ApproveTimesheetEntryRequest>
{
    public ApproveTimesheetEntryRequestValidator()
    {
        RuleFor(x => x.ApprovedOvertimeHours)
            .InclusiveBetween(0m, 24m)
            .When(x => x.ApprovedOvertimeHours.HasValue);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class ManualBookingRequestValidator : AbstractValidator<ManualBookingRequest>
{
    public ManualBookingRequestValidator()
    {
        RuleFor(x => x.WorkDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(new DateTime(2000, 1, 1)));
        RuleFor(x => x.HoursWorked)
            .InclusiveBetween(0m, 24m)
            .When(x => x.HoursWorked.HasValue);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class UpsertAllowancePolicyRequestValidator : AbstractValidator<UpsertAllowancePolicyRequest>
{
    public UpsertAllowancePolicyRequestValidator()
    {
        RuleFor(x => x.PolicyName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.JobTitle).MaximumLength(120);
        RuleFor(x => x.MonthlyAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.EffectiveFrom).GreaterThanOrEqualTo(DateOnly.FromDateTime(new DateTime(2000, 1, 1)));
        RuleFor(x => x.EffectiveTo)
            .GreaterThanOrEqualTo(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue);
    }
}

public class ApprovePayrollRunOverrideRequestValidator : AbstractValidator<ApprovePayrollRunOverrideRequest>
{
    private static readonly string[] AllowedCategories =
    [
        "DataCorrection",
        "TimingAdjustment",
        "ExceptionalPayment",
        "PolicyException",
        "EmergencyClosure",
        "Other"
    ];

    public ApprovePayrollRunOverrideRequestValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(x => Array.IndexOf(AllowedCategories, x) >= 0)
            .WithMessage("Invalid override category.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(300);

        RuleFor(x => x.ReferenceId)
            .NotEmpty()
            .MaximumLength(80)
            .Matches("^OVR-[0-9]{6}-[0-9]{4}$")
            .WithMessage("Reference id must match OVR-YYYYMM-####.");
    }
}

public class CreateIntegrationSyncJobRequestValidator : AbstractValidator<CreateIntegrationSyncJobRequest>
{
    public CreateIntegrationSyncJobRequestValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .MaximumLength(50)
            .Must(x =>
                string.Equals(x, "Qiwa", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x, "Mudad", StringComparison.OrdinalIgnoreCase))
            .WithMessage("provider must be Qiwa or Mudad.");
        RuleFor(x => x.Operation).NotEmpty().MaximumLength(80);
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(80);
        RuleFor(x => x.RequestPayloadJson).MaximumLength(20000);
        RuleFor(x => x.IdempotencyKey).MaximumLength(200);
        RuleFor(x => x.MaxAttempts)
            .InclusiveBetween(1, 10)
            .When(x => x.MaxAttempts.HasValue);
    }
}
