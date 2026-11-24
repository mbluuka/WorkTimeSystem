# End-to-end verification of FR7/FR13/FR14/FR15/FR16 against the Docker server.
# Requires: docker compose up -d server
$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:8080"

function Login($email, $password) {
    $body = @{ email = $email; password = $password } | ConvertTo-Json
    (Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $body -ContentType "application/json").token
}

$admin = Login "admin@worktime.local" "Admin@123"
$emp = Login "employee@worktime.local" "Employee@123"
$mgr = Login "manager@worktime.local" "Manager@123"
Write-Host "tokens: admin=$($admin.Length) employee=$($emp.Length) manager=$($mgr.Length)"

$H = @{ Authorization = "Bearer $admin" }
$HE = @{ Authorization = "Bearer $emp" }
$HM = @{ Authorization = "Bearer $mgr" }

# --- Manager must now be allowed to read the user list (was 403) ---
$mgrUsers = Invoke-RestMethod -Uri "$baseUrl/api/users" -Headers $HM
Write-Host "OK manager can list users: $($mgrUsers.Count) users"

# --- projects ---
$projects = Invoke-RestMethod -Uri "$baseUrl/api/projects" -Headers $HE
Write-Host "projects: $($projects.Count)"
$projectId = $projects[0].id

# --- device for employee ---
$devices = Invoke-RestMethod -Uri "$baseUrl/api/devices" -Headers $HE
$deviceId = $devices[0].id
$userId = $emp # placeholder
# resolve employee id from admin listing
$empUser = (Invoke-RestMethod -Uri "$baseUrl/api/users" -Headers $H) | Where-Object { $_.email -eq "employee@worktime.local" }
$userId = $empUser.id
Write-Host "employee id=$userId device id=$deviceId project=$projectId"

# --- post work events for the last 3 weekdays (yesterday..-3d) ---
$now = [DateTime]::UtcNow
for ($d = 1; $d -le 3; $d++) {
    $day = $now.Date.AddDays(-$d)
    $start = $day.AddHours(9)
    $brk = $day.AddHours(12)
    $back = $day.AddHours(13)
    $end = $day.AddHours(18)
    foreach ($ev in @(
        @{ type = "StartWork"; timestamp = $start },
        @{ type = "StartBreak"; timestamp = $brk },
        @{ type = "EndBreak"; timestamp = $back },
        @{ type = "EndWork"; timestamp = $end }
    )) {
        $payload = @{
            id = [Guid]::NewGuid().ToString()
            userId = $userId
            deviceId = $deviceId
            type = $ev.type
            timestamp = $ev.timestamp.ToString("o")
            createdAt = $now.ToString("o")
            deviceSeq = (Get-Random -Minimum 1000 -Maximum 99999)
            projectId = $projectId
        } | ConvertTo-Json
        Invoke-RestMethod -Uri "$baseUrl/api/WorkEvents" -Method Post -Headers $HE -Body $payload -ContentType "application/json" | Out-Null
    }
}
Write-Host "OK posted 12 work events"

# --- FR14: team report grouped by project ---
$from = $now.Date.AddDays(-5).ToString("o")
$to = $now.Date.AddDays(1).ToString("o")
$rep = Invoke-RestMethod -Uri "$baseUrl/api/reports/team/by-project?from=$([System.Web.HttpUtility]::UrlEncode($from))&to=$([System.Web.HttpUtility]::UrlEncode($to))" -Headers $HM
Write-Host "=== FR14 team by project (manager) ==="
Write-Host "totalTeamMinutes=$($rep.totalTeamMinutes)"
$rep.projects | ForEach-Object {
    Write-Host "  project '$($_.projectName)' minutes=$($_.totalWorkMinutes) employees=$($_.employeeCount)"
    $_.employees | ForEach-Object { Write-Host "    - $($_.userName) work=$($_.totalWorkMinutes) break=$($_.totalBreakMinutes) days=$($_.daysWorked)" }
}

# --- FR15: employee exports own report to PDF and XLSX ---
# -UseBasicParsing: иначе PowerShell спрашивает про движок IE и зависает
$pdf = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api/reports/export/pdf?userId=$userId&from=$from&to=$to" -Headers $HE
Write-Host "FR15 PDF export: $($pdf.StatusCode) content-type=$($pdf.Headers['Content-Type']) bytes=$($pdf.RawContentLength)"
$xlsx = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api/reports/export/xlsx?userId=$userId&from=$from&to=$to" -Headers $HE
Write-Host "FR15 XLSX export: $($xlsx.StatusCode) content-type=$($xlsx.Headers['Content-Type']) bytes=$($xlsx.RawContentLength)"

# --- FR7: employee cannot edit an event outside the edit window ---
# Create an event 10 days ago (outside the 7-day window) as admin,
# then the employee must be blocked from editing it.
$oldDay = $now.Date.AddDays(-10)
$oldPayload = @{
    id = [Guid]::NewGuid().ToString()
    userId = $userId
    deviceId = $deviceId
    type = "StartWork"
    timestamp = $oldDay.AddHours(9).ToString("o")
    createdAt = $now.ToString("o")
    deviceSeq = (Get-Random -Minimum 1000 -Maximum 99999)
    projectId = $projectId
} | ConvertTo-Json
$created = Invoke-RestMethod -Uri "$baseUrl/api/WorkEvents" -Method Post -Headers $H -Body $oldPayload -ContentType "application/json"
$targetId = $created.id
Write-Host "created 10-day-old event $targetId"

$upd = @{ timestamp = $now.ToString("o"); projectId = $projectId } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$baseUrl/api/WorkEvents/$targetId" -Method Put -Headers $HE -Body $upd -ContentType "application/json" | Out-Null
    Write-Host "FAIL FR7: employee edited an event older than the window"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    Write-Host "OK FR7 blocked employee edit of old event: HTTP $code"
}
# admin must be allowed
try {
    Invoke-RestMethod -Uri "$baseUrl/api/WorkEvents/$targetId" -Method Put -Headers $H -Body $upd -ContentType "application/json" | Out-Null
    Write-Host "OK FR7 admin may edit any period"
} catch {
    Write-Host "FAIL FR7 admin edit: $($_.Exception.Message)"
}

# --- FR16: PUT /api/users/{id} with isBlocked ---
$empUser = $mgrUsers | Where-Object email -eq "employee@worktime.local"
$empUserId = $empUser.id
# Block the employee (should succeed)
$updUser = @{ isBlocked = $true } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$baseUrl/api/users/$empUserId" -Method Put -Headers $H -Body $updUser -ContentType "application/json" | Out-Null
    Write-Host "OK PUT /api/users blocked employee"
} catch {
    Write-Host "FAIL PUT user block: $($_.Exception.Message)"
}
# Unblock the employee
$updUser = @{ isBlocked = $false } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$baseUrl/api/users/$empUserId" -Method Put -Headers $H -Body $updUser -ContentType "application/json" | Out-Null
    Write-Host "OK PUT /api/users unblocked employee"
} catch {
    Write-Host "FAIL PUT user unblock: $($_.Exception.Message)"
}

# --- FR16: cannot block own account (admin tries to block self -> 400, not 403) ---
$allUsers = Invoke-RestMethod -Uri "$baseUrl/api/users" -Headers $H
$adminUser = $allUsers | Where-Object role -eq "Admin" | Select-Object -First 1
try {
    $selfBlock = @{ isBlocked = $true } | ConvertTo-Json
    Invoke-RestMethod -Uri "$baseUrl/api/users/$($adminUser.id)" -Method Put -Headers $H -Body $selfBlock -ContentType "application/json" | Out-Null
    Write-Host "FAIL self-block succeeded (should be 400)"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -eq 400) {
        Write-Host "OK self-block blocked: HTTP $code"
    } else {
        Write-Host "FAIL self-block returned HTTP $code (expected 400)"
    }
}

# --- DELETE project with work events should return 400 ---
$proj = $projects[0]  # first project likely has work events from E2E test
try {
    Invoke-RestMethod -Uri "$baseUrl/api/projects/$($proj.id)" -Method Delete -Headers $H | Out-Null
    Write-Host "FAIL DELETE occupied project succeeded (should be 400)"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code -eq 400) {
        Write-Host "OK DELETE occupied project blocked: HTTP $code"
    } else {
        Write-Host "FAIL DELETE occupied project returned HTTP $code (expected 400)"
    }
}

Write-Host "`nE2E verification finished"
