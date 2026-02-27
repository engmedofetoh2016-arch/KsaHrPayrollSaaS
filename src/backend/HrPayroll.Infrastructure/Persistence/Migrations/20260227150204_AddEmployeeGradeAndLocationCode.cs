using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeGradeAndLocationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LoanDeduction",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "GradeCode",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LocationCode",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AllowancePolicyMatrixSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyName = table.Column<string>(type: "text", nullable: false),
                    GradeCode = table.Column<string>(type: "text", nullable: false),
                    LocationCode = table.Column<string>(type: "text", nullable: false),
                    HousingAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    MealAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    ProrationMethod = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsTaxable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowancePolicyMatrixSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllowancePolicySet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyName = table.Column<string>(type: "text", nullable: false),
                    JobTitle = table.Column<string>(type: "text", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsTaxable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowancePolicySet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceRuleEventSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceRuleEventSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceRuleSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleCode = table.Column<string>(type: "text", nullable: false),
                    RuleName = table.Column<string>(type: "text", nullable: false),
                    RuleCategory = table.Column<string>(type: "text", nullable: false),
                    RuleConfigJson = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceRuleSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataQualityFixBatchSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchReference = table.Column<string>(type: "text", nullable: false),
                    TriggeredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    SuccessItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataQualityFixBatchSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataQualityIssueSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueCode = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueStatus = table.Column<string>(type: "text", nullable: false),
                    IssueMessage = table.Column<string>(type: "text", nullable: false),
                    FixActionCode = table.Column<string>(type: "text", nullable: false),
                    FixPayloadJson = table.Column<string>(type: "text", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataQualityIssueSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLoanInstallmentSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeLoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeductedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoanInstallmentSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLoanSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanType = table.Column<string>(type: "text", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    RemainingBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    InstallmentAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    StartYear = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    TotalInstallments = table.Column<int>(type: "integer", nullable: false),
                    PaidInstallments = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLoanSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeOffboardingSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOffboardingSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSelfServiceRequestSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSelfServiceRequestSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSyncJobSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestPayloadJson = table.Column<string>(type: "text", nullable: false),
                    ResponsePayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadlineAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSyncJobSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationQueueSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientType = table.Column<string>(type: "text", nullable: false),
                    RecipientValue = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    TemplateCode = table.Column<string>(type: "text", nullable: false),
                    RelatedEntityType = table.Column<string>(type: "text", nullable: false),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationQueueSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplateSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateCode = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplateSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffboardingChecklistApprovalSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingChecklistApprovalSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffboardingChecklistItemSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    ItemLabel = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingChecklistItemSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffboardingChecklistSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OffboardingId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingChecklistSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffboardingChecklistTemplateSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleName = table.Column<string>(type: "text", nullable: false),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    ItemLabel = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresEsign = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingChecklistTemplateSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffboardingEsignDocumentSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChecklistItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentName = table.Column<string>(type: "text", nullable: false),
                    DocumentUrl = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffboardingEsignDocumentSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollApprovalActionSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "text", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    ActionStatus = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<string>(type: "text", nullable: false),
                    RolledBackActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollApprovalActionSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollApprovalMatrixSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollScope = table.Column<string>(type: "text", nullable: false),
                    StageCode = table.Column<string>(type: "text", nullable: false),
                    StageName = table.Column<string>(type: "text", nullable: false),
                    StageOrder = table.Column<int>(type: "integer", nullable: false),
                    ApproverRole = table.Column<string>(type: "text", nullable: false),
                    AllowRollback = table.Column<bool>(type: "boolean", nullable: false),
                    AutoApproveEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SlaEscalationHours = table.Column<int>(type: "integer", nullable: false),
                    EscalationRole = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollApprovalMatrixSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollForecastResultSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastYear = table.Column<int>(type: "integer", nullable: false),
                    ForecastMonth = table.Column<int>(type: "integer", nullable: false),
                    ProjectedPayrollCost = table.Column<decimal>(type: "numeric", nullable: false),
                    ProjectedHeadcount = table.Column<int>(type: "integer", nullable: false),
                    ProjectedSaudizationPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    ComplianceRiskScore = table.Column<int>(type: "integer", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollForecastResultSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollForecastScenarioSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioName = table.Column<string>(type: "text", nullable: false),
                    BasePayrollRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlannedSaudiHires = table.Column<int>(type: "integer", nullable: false),
                    PlannedNonSaudiHires = table.Column<int>(type: "integer", nullable: false),
                    PlannedAttrition = table.Column<int>(type: "integer", nullable: false),
                    PlannedSalaryDeltaPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AssumptionsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollForecastScenarioSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftRuleSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StandardDailyHours = table.Column<decimal>(type: "numeric", nullable: false),
                    OvertimeMultiplierWeekday = table.Column<decimal>(type: "numeric", nullable: false),
                    OvertimeMultiplierWeekend = table.Column<decimal>(type: "numeric", nullable: false),
                    OvertimeMultiplierHoliday = table.Column<decimal>(type: "numeric", nullable: false),
                    WeekendDaysCsv = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftRuleSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimesheetEntrySet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric", nullable: false),
                    ApprovedOvertimeHours = table.Column<decimal>(type: "numeric", nullable: false),
                    IsWeekend = table.Column<bool>(type: "boolean", nullable: false),
                    IsHoliday = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimesheetEntrySet", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSet_TenantId_GradeCode_LocationCode",
                table: "EmployeeSet",
                columns: new[] { "TenantId", "GradeCode", "LocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowancePolicyMatrixSet_TenantId_GradeCode_LocationCode_Ef~",
                table: "AllowancePolicyMatrixSet",
                columns: new[] { "TenantId", "GradeCode", "LocationCode", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllowancePolicyMatrixSet_TenantId_IsActive_GradeCode_Locati~",
                table: "AllowancePolicyMatrixSet",
                columns: new[] { "TenantId", "IsActive", "GradeCode", "LocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowancePolicySet_TenantId_IsActive_JobTitle",
                table: "AllowancePolicySet",
                columns: new[] { "TenantId", "IsActive", "JobTitle" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowancePolicySet_TenantId_PolicyName",
                table: "AllowancePolicySet",
                columns: new[] { "TenantId", "PolicyName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRuleEventSet_TenantId_RuleId_Status",
                table: "ComplianceRuleEventSet",
                columns: new[] { "TenantId", "RuleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRuleEventSet_TenantId_Status_TriggeredAtUtc",
                table: "ComplianceRuleEventSet",
                columns: new[] { "TenantId", "Status", "TriggeredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRuleSet_TenantId_RuleCode",
                table: "ComplianceRuleSet",
                columns: new[] { "TenantId", "RuleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityFixBatchSet_TenantId_BatchReference",
                table: "DataQualityFixBatchSet",
                columns: new[] { "TenantId", "BatchReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityIssueSet_TenantId_EntityType_EntityId",
                table: "DataQualityIssueSet",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DataQualityIssueSet_TenantId_IssueStatus_Severity_DetectedA~",
                table: "DataQualityIssueSet",
                columns: new[] { "TenantId", "IssueStatus", "Severity", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoanInstallmentSet_TenantId_EmployeeId_Year_Month_S~",
                table: "EmployeeLoanInstallmentSet",
                columns: new[] { "TenantId", "EmployeeId", "Year", "Month", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoanInstallmentSet_TenantId_EmployeeLoanId_Year_Mon~",
                table: "EmployeeLoanInstallmentSet",
                columns: new[] { "TenantId", "EmployeeLoanId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLoanSet_TenantId_EmployeeId_Status",
                table: "EmployeeLoanSet",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOffboardingSet_TenantId_EmployeeId_CreatedAtUtc",
                table: "EmployeeOffboardingSet",
                columns: new[] { "TenantId", "EmployeeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOffboardingSet_TenantId_Status_EffectiveDate",
                table: "EmployeeOffboardingSet",
                columns: new[] { "TenantId", "Status", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSelfServiceRequestSet_TenantId_EmployeeId_RequestTy~",
                table: "EmployeeSelfServiceRequestSet",
                columns: new[] { "TenantId", "EmployeeId", "RequestType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSelfServiceRequestSet_TenantId_Status_CreatedAtUtc",
                table: "EmployeeSelfServiceRequestSet",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncJobSet_TenantId_DeadlineAtUtc_Status",
                table: "IntegrationSyncJobSet",
                columns: new[] { "TenantId", "DeadlineAtUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncJobSet_TenantId_IdempotencyKey",
                table: "IntegrationSyncJobSet",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSyncJobSet_TenantId_Provider_Status_NextAttemptA~",
                table: "IntegrationSyncJobSet",
                columns: new[] { "TenantId", "Provider", "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationQueueSet_TenantId_Channel_Status",
                table: "NotificationQueueSet",
                columns: new[] { "TenantId", "Channel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationQueueSet_TenantId_Status_ScheduledAtUtc",
                table: "NotificationQueueSet",
                columns: new[] { "TenantId", "Status", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplateSet_TenantId_TemplateCode_Channel",
                table: "NotificationTemplateSet",
                columns: new[] { "TenantId", "TemplateCode", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistApprovalSet_TenantId_ChecklistItemId_St~",
                table: "OffboardingChecklistApprovalSet",
                columns: new[] { "TenantId", "ChecklistItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistItemSet_TenantId_ChecklistId_ItemCode",
                table: "OffboardingChecklistItemSet",
                columns: new[] { "TenantId", "ChecklistId", "ItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistItemSet_TenantId_ChecklistId_SortOrder",
                table: "OffboardingChecklistItemSet",
                columns: new[] { "TenantId", "ChecklistId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistSet_TenantId_OffboardingId",
                table: "OffboardingChecklistSet",
                columns: new[] { "TenantId", "OffboardingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistTemplateSet_TenantId_RoleName_IsActive",
                table: "OffboardingChecklistTemplateSet",
                columns: new[] { "TenantId", "RoleName", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingChecklistTemplateSet_TenantId_RoleName_ItemCode",
                table: "OffboardingChecklistTemplateSet",
                columns: new[] { "TenantId", "RoleName", "ItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffboardingEsignDocumentSet_TenantId_ChecklistItemId_Status",
                table: "OffboardingEsignDocumentSet",
                columns: new[] { "TenantId", "ChecklistItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollApprovalActionSet_TenantId_PayrollRunId_ActionAtUtc",
                table: "PayrollApprovalActionSet",
                columns: new[] { "TenantId", "PayrollRunId", "ActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollApprovalActionSet_TenantId_StageCode_ActionStatus",
                table: "PayrollApprovalActionSet",
                columns: new[] { "TenantId", "StageCode", "ActionStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollApprovalMatrixSet_TenantId_PayrollScope_StageCode",
                table: "PayrollApprovalMatrixSet",
                columns: new[] { "TenantId", "PayrollScope", "StageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollForecastResultSet_TenantId_ScenarioId_ForecastYear_F~",
                table: "PayrollForecastResultSet",
                columns: new[] { "TenantId", "ScenarioId", "ForecastYear", "ForecastMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollForecastScenarioSet_TenantId_CreatedAtUtc",
                table: "PayrollForecastScenarioSet",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftRuleSet_TenantId_Name",
                table: "ShiftRuleSet",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntrySet_TenantId_EmployeeId_WorkDate",
                table: "TimesheetEntrySet",
                columns: new[] { "TenantId", "EmployeeId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetEntrySet_TenantId_Status_WorkDate",
                table: "TimesheetEntrySet",
                columns: new[] { "TenantId", "Status", "WorkDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowancePolicyMatrixSet");

            migrationBuilder.DropTable(
                name: "AllowancePolicySet");

            migrationBuilder.DropTable(
                name: "ComplianceRuleEventSet");

            migrationBuilder.DropTable(
                name: "ComplianceRuleSet");

            migrationBuilder.DropTable(
                name: "DataQualityFixBatchSet");

            migrationBuilder.DropTable(
                name: "DataQualityIssueSet");

            migrationBuilder.DropTable(
                name: "EmployeeLoanInstallmentSet");

            migrationBuilder.DropTable(
                name: "EmployeeLoanSet");

            migrationBuilder.DropTable(
                name: "EmployeeOffboardingSet");

            migrationBuilder.DropTable(
                name: "EmployeeSelfServiceRequestSet");

            migrationBuilder.DropTable(
                name: "IntegrationSyncJobSet");

            migrationBuilder.DropTable(
                name: "NotificationQueueSet");

            migrationBuilder.DropTable(
                name: "NotificationTemplateSet");

            migrationBuilder.DropTable(
                name: "OffboardingChecklistApprovalSet");

            migrationBuilder.DropTable(
                name: "OffboardingChecklistItemSet");

            migrationBuilder.DropTable(
                name: "OffboardingChecklistSet");

            migrationBuilder.DropTable(
                name: "OffboardingChecklistTemplateSet");

            migrationBuilder.DropTable(
                name: "OffboardingEsignDocumentSet");

            migrationBuilder.DropTable(
                name: "PayrollApprovalActionSet");

            migrationBuilder.DropTable(
                name: "PayrollApprovalMatrixSet");

            migrationBuilder.DropTable(
                name: "PayrollForecastResultSet");

            migrationBuilder.DropTable(
                name: "PayrollForecastScenarioSet");

            migrationBuilder.DropTable(
                name: "ShiftRuleSet");

            migrationBuilder.DropTable(
                name: "TimesheetEntrySet");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSet_TenantId_GradeCode_LocationCode",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "LoanDeduction",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "GradeCode",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "LocationCode",
                table: "EmployeeSet");
        }
    }
}
