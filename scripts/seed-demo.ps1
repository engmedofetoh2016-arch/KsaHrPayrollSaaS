param(
    [string]$ApiBaseUrl = "http://localhost:5202",
    [string]$OwnerPassword = "Owner1234",
    [int]$Year = (Get-Date).Year,
    [int]$Month = (Get-Date).Month,
    [switch]$LockRun,
    [switch]$QueueExports
)

$ErrorActionPreference = "Stop"

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Url,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    try {
        if ($null -ne $Body) {
            return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 10)
        }

        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $message = $_.Exception.Message
        $responseBody = ""
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            $reader.Close()
        }
        catch {
            $responseBody = ""
        }

        if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
            throw "API call failed [$Method $Url] status=$status message=$message body=$responseBody"
        }

        throw "API call failed [$Method $Url] status=$status message=$message"
    }
}

function As-Array {
    param([object]$Value)

    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return $Value }
    return @($Value)
}

function Extract-Items {
    param([object]$Value)

    if ($null -eq $Value) { return @() }
    if ($Value.PSObject.Properties.Name -contains "items") {
        return As-Array $Value.items
    }
    return As-Array $Value
}

Write-Host "Checking API health at $ApiBaseUrl..."
$health = Invoke-Api -Method "GET" -Url "$ApiBaseUrl/health"
if ($health.status -ne "ok") {
    throw "API health check failed."
}

$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
$slug = "demo-$suffix"
$ownerEmail = "owner+$slug@demo.co"

Write-Host "Creating tenant $slug..."
$tenant = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/tenants" -Body @{
    tenantName       = "Demo Company $suffix"
    slug             = $slug
    companyLegalName = "Demo Company $suffix LLC"
    currencyCode     = "SAR"
    defaultPayDay    = 25
    ownerFirstName   = "Demo"
    ownerLastName    = "Owner"
    ownerEmail       = $ownerEmail
    ownerPassword    = $OwnerPassword
}

$tenantId = [string]$tenant.id
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    throw "Tenant creation did not return tenant id."
}

Write-Host "Logging in owner..."
$login = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/auth/login" -Body @{
    tenantSlug = $slug
    email    = $ownerEmail
    password = $OwnerPassword
}

$accessToken = [string]$login.accessToken
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Login failed, token missing."
}

$headers = @{
    Authorization = "Bearer $accessToken"
    "X-Tenant-Id" = $tenantId
}

Write-Host "Configuring company WPS settings..."
Invoke-Api -Method "PUT" -Url "$ApiBaseUrl/api/company-profile" -Headers $headers -Body @{
    legalName = "Demo Company $suffix LLC"
    currencyCode = "SAR"
    defaultPayDay = 25
    eosFirstFiveYearsMonthFactor = 0.5
    eosAfterFiveYearsMonthFactor = 1.0
    wpsCompanyBankName = "Al Rajhi Bank"
    wpsCompanyBankCode = "RJHISARI"
    wpsCompanyIban = "SA0380000000608010167519"
} | Out-Null

Write-Host "Seeding employees..."
$existingEmployees = Extract-Items (Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/employees?page=1&pageSize=200" -Headers $headers)
$existingEmails = @{}
foreach ($e in $existingEmployees) {
    if ($null -eq $e) { continue }
    $email = [string]$e.email
    if ([string]::IsNullOrWhiteSpace($email)) { continue }
    $existingEmails[$email.ToLowerInvariant()] = $true
}

$seedEmployees = @(
    @{
        startDate = (Get-Date).AddYears(-6).ToString("yyyy-MM-dd")
        firstName = "Ahmed"
        lastName = "Alharbi"
        email = "ahmed.$slug@demo.co"
        jobTitle = "HR Specialist"
        baseSalary = 8200
        isSaudiNational = $true
        isGosiEligible = $true
        gosiBasicWage = 6500
        gosiHousingAllowance = 1200
        employeeNumber = "EMP-1001"
        bankName = "Al Rajhi Bank"
        bankIban = "SA0380000000608010167519"
        iqamaNumber = ""
        iqamaExpiryDate = $null
        workPermitExpiryDate = $null
    },
    @{
        startDate = (Get-Date).AddYears(-3).ToString("yyyy-MM-dd")
        firstName = "Sara"
        lastName = "Alghamdi"
        email = "sara.$slug@demo.co"
        jobTitle = "Accountant"
        baseSalary = 9800
        isSaudiNational = $true
        isGosiEligible = $true
        gosiBasicWage = 7800
        gosiHousingAllowance = 1500
        employeeNumber = "EMP-1002"
        bankName = "Saudi National Bank"
        bankIban = "SA4420000001234567891234"
        iqamaNumber = ""
        iqamaExpiryDate = $null
        workPermitExpiryDate = $null
    },
    @{
        startDate = (Get-Date).AddYears(-8).ToString("yyyy-MM-dd")
        firstName = "Omar"
        lastName = "Nasser"
        email = "omar.$slug@demo.co"
        jobTitle = "Engineer"
        baseSalary = 12500
        isSaudiNational = $false
        isGosiEligible = $true
        gosiBasicWage = 9800
        gosiHousingAllowance = 2200
        employeeNumber = "EMP-1003"
        bankName = "Riyad Bank"
        bankIban = "SA6520000009876543210001"
        iqamaNumber = "2456789012"
        iqamaExpiryDate = (Get-Date).AddDays(42).ToString("yyyy-MM-dd")
        workPermitExpiryDate = (Get-Date).AddDays(58).ToString("yyyy-MM-dd")
    },
    @{
        startDate = (Get-Date).AddYears(-2).ToString("yyyy-MM-dd")
        firstName = "Mina"
        lastName = "Ibrahim"
        email = "mina.$slug@demo.co"
        jobTitle = "Operations Coordinator"
        baseSalary = 7300
        isSaudiNational = $false
        isGosiEligible = $false
        gosiBasicWage = 0
        gosiHousingAllowance = 0
        employeeNumber = "EMP-1004"
        bankName = "Banque Saudi Fransi"
        bankIban = "SA5515000060045678901234"
        iqamaNumber = "2987654321"
        iqamaExpiryDate = (Get-Date).AddDays(18).ToString("yyyy-MM-dd")
        workPermitExpiryDate = (Get-Date).AddDays(33).ToString("yyyy-MM-dd")
    }
)

foreach ($employee in $seedEmployees) {
    $emailKey = $employee.email.ToLowerInvariant()
    if (-not $existingEmails.ContainsKey($emailKey)) {
        Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/employees" -Headers $headers -Body $employee | Out-Null
    }
}

$employees = Extract-Items (Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/employees?page=1&pageSize=200" -Headers $headers)
if ($employees.Count -eq 0) {
    throw "Employee seeding failed."
}

Write-Host "Seeding attendance for $Year-$Month..."
foreach ($employee in $employees) {
    $daysPresent = 22
    $daysAbsent = 0
    $overtimeHours = 6

    if ($employee.email -like "ahmed.*") {
        $overtimeHours = 10
    }
    elseif ($employee.email -like "sara.*") {
        $daysPresent = 21
        $daysAbsent = 1
        $overtimeHours = 4
    }
    elseif ($employee.email -like "omar.*") {
        $daysPresent = 20
        $daysAbsent = 2
        $overtimeHours = 12
    }

    Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/attendance-inputs" -Headers $headers -Body @{
        employeeId    = $employee.id
        year          = $Year
        month         = $Month
        daysPresent   = $daysPresent
        daysAbsent    = $daysAbsent
        overtimeHours = $overtimeHours
    } | Out-Null
}

Write-Host "Creating payroll period..."
$periods = Extract-Items (Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/payroll/periods" -Headers $headers)
$period = $periods | Where-Object { $_.year -eq $Year -and $_.month -eq $Month } | Select-Object -First 1

if ($null -eq $period) {
    $startDate = Get-Date -Year $Year -Month $Month -Day 1
    $endDate = $startDate.AddMonths(1).AddDays(-1)
    $period = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/periods" -Headers $headers -Body @{
        year            = $Year
        month           = $Month
        periodStartDate = $startDate.ToString("yyyy-MM-dd")
        periodEndDate   = $endDate.ToString("yyyy-MM-dd")
    }
}

$periodId = [string]$period.id
if ([string]::IsNullOrWhiteSpace($periodId)) {
    throw "Payroll period missing id."
}

Write-Host "Seeding one sample allowance..."
$adjustments = Extract-Items (Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/payroll/adjustments?year=$Year&month=$Month" -Headers $headers)
foreach ($employee in $employees) {
    $allowanceAmount = 0
    $deductionAmount = 0

    if ($employee.email -like "ahmed.*") {
        $allowanceAmount = 600
    }
    elseif ($employee.email -like "sara.*") {
        $allowanceAmount = 450
        $deductionAmount = 120
    }
    elseif ($employee.email -like "omar.*") {
        $allowanceAmount = 900
    }
    elseif ($employee.email -like "mina.*") {
        $deductionAmount = 200
    }

    if ($allowanceAmount -gt 0) {
        $alreadyHasAllowance = $adjustments | Where-Object { $_.employeeId -eq $employee.id -and $_.type -eq 1 } | Select-Object -First 1
        if ($null -eq $alreadyHasAllowance) {
            Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/adjustments" -Headers $headers -Body @{
                employeeId = $employee.id
                year       = $Year
                month      = $Month
                type       = 1
                amount     = $allowanceAmount
                notes      = "Demo monthly allowance"
            } | Out-Null
        }
    }

    if ($deductionAmount -gt 0) {
        $alreadyHasDeduction = $adjustments | Where-Object { $_.employeeId -eq $employee.id -and $_.type -eq 2 } | Select-Object -First 1
        if ($null -eq $alreadyHasDeduction) {
            Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/adjustments" -Headers $headers -Body @{
                employeeId = $employee.id
                year       = $Year
                month      = $Month
                type       = 2
                amount     = $deductionAmount
                notes      = "Demo manual deduction"
            } | Out-Null
        }
    }
}

Write-Host "Seeding one approved unpaid leave request..."
$leaveStart = (Get-Date -Year $Year -Month $Month -Day 1).AddDays(10).ToString("yyyy-MM-dd")
$leaveEnd = (Get-Date -Year $Year -Month $Month -Day 1).AddDays(12).ToString("yyyy-MM-dd")
$leaveRequests = Extract-Items (Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/leave/requests?year=$Year&month=$Month" -Headers $headers)
$leaveEmployee = $employees | Where-Object { $_.email -like "ahmed.*" } | Select-Object -First 1
if ($null -ne $leaveEmployee) {
    $existingApprovedUnpaid = $leaveRequests | Where-Object {
        $_.employeeId -eq $leaveEmployee.id -and $_.leaveType -eq 3 -and $_.status -eq 2
    } | Select-Object -First 1

    if ($null -eq $existingApprovedUnpaid) {
        $createdLeave = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/leave/requests" -Headers $headers -Body @{
            employeeId = $leaveEmployee.id
            leaveType = 3
            startDate = $leaveStart
            endDate = $leaveEnd
            reason = "Demo unpaid leave for payroll deduction"
        }

        Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/leave/requests/$($createdLeave.id)/approve" -Headers $headers -Body @{} | Out-Null
    }
}

Write-Host "Calculating payroll run..."
$calc = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/calculate" -Headers $headers -Body @{
    payrollPeriodId = $periodId
}

$runId = [string]$calc.runId
if ([string]::IsNullOrWhiteSpace($runId)) {
    throw "Payroll calculation did not return runId."
}

if ($LockRun.IsPresent) {
    Write-Host "Approving and locking payroll run..."
    Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/$runId/approve" -Headers $headers -Body @{} | Out-Null
    Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/$runId/lock" -Headers $headers -Body @{} | Out-Null
}

$run = Invoke-Api -Method "GET" -Url "$ApiBaseUrl/api/payroll/runs/$runId" -Headers $headers
$lineCount = @($run.lines).Count
$totalNet = (@($run.lines) | Measure-Object -Property netAmount -Sum).Sum
$totalGosiEmployee = (@($run.lines) | Measure-Object -Property gosiEmployeeContribution -Sum).Sum
$totalGosiEmployer = (@($run.lines) | Measure-Object -Property gosiEmployerContribution -Sum).Sum
$totalUnpaidLeaveDeduction = (@($run.lines) | Measure-Object -Property unpaidLeaveDeduction -Sum).Sum

$registerExportId = $null
$gosiExportId = $null
$payslipExportId = $null

if ($QueueExports.IsPresent) {
    Write-Host "Queueing export jobs..."
    $registerExport = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/$runId/exports/register-csv" -Headers $headers -Body @{}
    $registerExportId = [string]$registerExport.id

    $gosiExport = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/$runId/exports/gosi-csv" -Headers $headers -Body @{}
    $gosiExportId = [string]$gosiExport.id

    $firstEmployee = $employees | Select-Object -First 1
    if ($null -ne $firstEmployee) {
        $payslipExport = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/payroll/runs/$runId/exports/payslip/$($firstEmployee.id)/pdf" -Headers $headers -Body @{}
        $payslipExportId = [string]$payslipExport.id
    }
}

Write-Host ""
Write-Host "Seed complete."
Write-Host "TenantId: $tenantId"
Write-Host "TenantSlug: $slug"
Write-Host "OwnerEmail: $ownerEmail"
Write-Host "OwnerPassword: $OwnerPassword"
Write-Host "PayrollRunId: $runId"
Write-Host "LineCount: $lineCount"
Write-Host ("TotalNet: {0:N2}" -f $totalNet)
Write-Host ("TotalGosiEmployee: {0:N2}" -f $totalGosiEmployee)
Write-Host ("TotalGosiEmployer: {0:N2}" -f $totalGosiEmployer)
Write-Host ("TotalUnpaidLeaveDeduction: {0:N2}" -f $totalUnpaidLeaveDeduction)
if ($QueueExports.IsPresent) {
    Write-Host "RegisterExportId: $registerExportId"
    Write-Host "GosiExportId: $gosiExportId"
    Write-Host "PayslipExportId: $payslipExportId"
}
