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

# Get Hospitals List to find an ID
Write-Host "`nFetching hospitals list..."
try {
    $listResponse = Invoke-RestMethod -Uri "$baseUrl/api/v1/hospitals" -Method Get -Headers $headers
    $hospitalId = $listResponse.data[0].id
    Write-Host "Found hospital ID: $hospitalId" -ForegroundColor Green
}
catch {
    Write-Host "Failed to fetch hospitals list. $($_.Exception.Message)" -ForegroundColor Red
    exit
}

# Get Hospital Details
Write-Host "`nFetching hospital details for ID: $hospitalId..."
try {
    $details = Invoke-RestMethod -Uri "$baseUrl/api/v1/hospitals/$hospitalId" -Method Get -Headers $headers
    
    # Verify Stats structure
    if ($details.stats) {
        Write-Host "Stats object found." -ForegroundColor Green
        
        if ($details.stats.uniquePatients.daily.Count -ge 0) {
           Write-Host "Stats.UniquePatients.Daily array exists (Count: $($details.stats.uniquePatients.daily.Count))" -ForegroundColor Green
        } else {
           Write-Host "Stats.UniquePatients.Daily array MISSING" -ForegroundColor Red
        }

        if ($details.stats.appointments.monthly.Count -ge 0) {
           Write-Host "Stats.Appointments.Monthly array exists (Count: $($details.stats.appointments.monthly.Count))" -ForegroundColor Green
        } else {
           Write-Host "Stats.Appointments.Monthly array MISSING" -ForegroundColor Red
        }
    }
    else {
        Write-Host "Stats object MISSING in response." -ForegroundColor Red
    }
}
catch {
    Write-Host "Failed to fetch hospital details. $($_.Exception.Message)" -ForegroundColor Red
    exit
}
