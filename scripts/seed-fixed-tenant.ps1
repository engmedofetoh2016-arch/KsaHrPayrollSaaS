param(
    [string]$ApiBaseUrl = "",
    [string]$TenantName = "Managm Elzahb",
    [string]$TenantSlug = "managm-elzahb",
    [string]$CompanyLegalName = "Managm Elzahb",
    [string]$CurrencyCode = "SAR",
    [int]$DefaultPayDay = 25,
    [string]$OwnerFirstName = "Magdy",
    [string]$OwnerLastName = "Kago",
    [string]$OwnerEmail = "magdyKago@mangm.com",
    [string]$OwnerPassword = "magdy123456"
)

$ErrorActionPreference = "Stop"

$apiBase = if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl
}
elseif (-not [string]::IsNullOrWhiteSpace($env:HRPAYROLL_API_URL)) {
    $env:HRPAYROLL_API_URL
}
else {
    "http://localhost:5202"
}
$ApiBaseUrl = $apiBase.TrimEnd("/")

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
        $status = ""
        $message = $_.Exception.Message
        $responseBody = ""

        try { $status = $_.Exception.Response.StatusCode.value__ } catch { }
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

function Normalize-Slug {
    param([string]$Value)

    $slug = ($Value ?? "").Trim().ToLowerInvariant()
    $slug = $slug -replace "[^a-z0-9-]", "-"
    $slug = $slug -replace "-+", "-"
    $slug = $slug.Trim('-')

    if ([string]::IsNullOrWhiteSpace($slug)) {
        throw "TenantSlug is invalid after normalization. Use letters, numbers, or hyphen."
    }

    return $slug
}

Write-Host "Using API base URL: $ApiBaseUrl"

$health = Invoke-Api -Method "GET" -Url "$ApiBaseUrl/health"
if ($health.status -ne "ok") {
    throw "API health check failed."
}

$normalizedSlug = Normalize-Slug -Value $TenantSlug
if ($normalizedSlug -ne $TenantSlug) {
    Write-Host "TenantSlug normalized from '$TenantSlug' to '$normalizedSlug' (backend only allows a-z, 0-9, -)."
}

$tenantId = ""
$tenantCreated = $false

try {
    Write-Host "Creating tenant '$normalizedSlug'..."
    $tenant = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/tenants" -Body @{
        tenantName       = $TenantName
        slug             = $normalizedSlug
        companyLegalName = $CompanyLegalName
        currencyCode     = $CurrencyCode
        defaultPayDay    = $DefaultPayDay
        ownerFirstName   = $OwnerFirstName
        ownerLastName    = $OwnerLastName
        ownerEmail       = $OwnerEmail
        ownerPassword    = $OwnerPassword
    }

    $tenantId = [string]$tenant.id
    $tenantCreated = $true
}
catch {
    $err = $_.Exception.Message
    if ($err -match "Tenant slug already exists") {
        Write-Host "Tenant already exists. Will try login with provided credentials..."
    }
    else {
        throw
    }
}

Write-Host "Logging in owner..."
$login = Invoke-Api -Method "POST" -Url "$ApiBaseUrl/api/auth/login" -Body @{
    tenantSlug = $normalizedSlug
    email      = $OwnerEmail
    password   = $OwnerPassword
}

$accessToken = [string]$login.accessToken
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Login failed, token missing."
}

if ([string]::IsNullOrWhiteSpace($tenantId)) {
    $tenantId = [string]$login.user.tenantId
}

Write-Host ""
Write-Host "Seed account ready."
Write-Host "TenantCreated: $tenantCreated"
Write-Host "TenantId: $tenantId"
Write-Host "TenantSlug: $normalizedSlug"
Write-Host "OwnerEmail: $OwnerEmail"
Write-Host "OwnerPassword: $OwnerPassword"
