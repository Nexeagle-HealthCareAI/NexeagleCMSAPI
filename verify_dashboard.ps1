$baseUrl = "http://localhost:5176"

# Login
Write-Host "Logging in..."
$loginBody = @{
    email = "admin@cms.com"
    password = "password123"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
    $token = $response.token
    Write-Host "Login successful." -ForegroundColor Green
}
catch {
    Write-Host "Login failed. $($_.Exception.Message)" -ForegroundColor Red
    exit
}

$headers = @{
    Authorization = "Bearer $token"
}

# Get Dashboard Stats
Write-Host "`nFetching dashboard stats..."
try {
    $stats = Invoke-RestMethod -Uri "$baseUrl/api/v1/dashboard/stats" -Method Get -Headers $headers
    
    # Verify Structure
    if ($stats.totalHospitals) { Write-Host "TotalHospitals found. Value: $($stats.totalHospitals.value)" -ForegroundColor Green } else { Write-Host "TotalHospitals MISSING" -ForegroundColor Red }
    if ($stats.totalDoctors) { Write-Host "TotalDoctors found. Value: $($stats.totalDoctors.value)" -ForegroundColor Green } else { Write-Host "TotalDoctors MISSING" -ForegroundColor Red }
    if ($stats.totalPatients) { Write-Host "TotalPatients found. Value: $($stats.totalPatients.value)" -ForegroundColor Green } else { Write-Host "TotalPatients MISSING" -ForegroundColor Red }
    if ($stats.totalUsers) { Write-Host "TotalUsers found. Value: $($stats.totalUsers.value)" -ForegroundColor Green } else { Write-Host "TotalUsers MISSING" -ForegroundColor Red }

    if ($stats.charts) {
        Write-Host "Charts object found." -ForegroundColor Green
        
        if ($stats.charts.hospitals.monthly.Count -ge 0) { Write-Host "Charts.Hospitals.Monthly exists (Count: $($stats.charts.hospitals.monthly.Count))" -ForegroundColor Green }
        if ($stats.charts.doctors.yearly.Count -ge 0) { Write-Host "Charts.Doctors.Yearly exists (Count: $($stats.charts.doctors.yearly.Count))" -ForegroundColor Green }
        if ($stats.charts.patients.weekly.Count -ge 0) { Write-Host "Charts.Patients.Weekly exists (Count: $($stats.charts.patients.weekly.Count))" -ForegroundColor Green }
        if ($stats.charts.users.daily.Count -ge 0) { Write-Host "Charts.Users.Daily exists (Count: $($stats.charts.users.daily.Count))" -ForegroundColor Green }
    }
    else {
        Write-Host "Charts object MISSING." -ForegroundColor Red
    }
}
catch {
    Write-Host "Failed to fetch dashboard stats. $($_.Exception.Message)" -ForegroundColor Red
    exit
}
