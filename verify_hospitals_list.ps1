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

# Get Hospitals List
Write-Host "`nFetching hospitals list..."
try {
    $hospitals = Invoke-RestMethod -Uri "$baseUrl/api/v1/hospitals?page=1&limit=10" -Method Get -Headers $headers
    
    # Verify Structure
    if ($hospitals.data) { Write-Host "Data array found. Count: $($hospitals.data.Count)" -ForegroundColor Green } else { Write-Host "Data MISSING" -ForegroundColor Red }
    
    if ($hospitals.pagination) {
        Write-Host "Pagination object found." -ForegroundColor Green
        Write-Host "  CurrentPage: $($hospitals.pagination.currentPage)" -ForegroundColor Cyan
        Write-Host "  TotalPages: $($hospitals.pagination.totalPages)" -ForegroundColor Cyan
        Write-Host "  TotalItems: $($hospitals.pagination.totalItems)" -ForegroundColor Cyan
        Write-Host "  ItemsPerPage: $($hospitals.pagination.itemsPerPage)" -ForegroundColor Cyan
    }
    else {
        Write-Host "Pagination object MISSING." -ForegroundColor Red
    }
}
catch {
    Write-Host "Failed to fetch hospitals list. $($_.Exception.Message)" -ForegroundColor Red
    exit
}
