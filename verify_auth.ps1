$baseUrl = "http://localhost:5176"

# 1. Access Protected Endpoint without Token
Write-Host "1. Testing access to protected endpoint without token..."
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/dashboard/stats" -Method Get -ErrorAction Stop
    Write-Host "FAILED: Should have returned 401 Unauthorized" -ForegroundColor Red
}
catch {
    if ($_.Exception.Response.StatusCode -eq [System.Net.HttpStatusCode]::Unauthorized) {
        Write-Host "PASSED: Returned 401 Unauthorized" -ForegroundColor Green
    }
    else {
        Write-Host "FAILED: Returned $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}

# 2. Login to Get Token
Write-Host "`n2. Logging in..."
$loginBody = @{
    email = "admin@cms.com"
    password = "password123"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/auth/login" -Method Post -Body $loginBody -ContentType "application/json"
    $token = $response.token
    if ($token) {
        Write-Host "PASSED: Login successful. Token received." -ForegroundColor Green
    }
    else {
        Write-Host "FAILED: Login successful but no token received." -ForegroundColor Red
        exit
    }
}
catch {
    Write-Host "FAILED: Login failed. $($_.Exception.Message)" -ForegroundColor Red
    exit
}

# 3. Access Protected Endpoint with Token
Write-Host "`n3. Testing access to protected endpoint with token..."
try {
    $headers = @{
        Authorization = "Bearer $token"
    }
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/dashboard/stats" -Method Get -Headers $headers
    Write-Host "PASSED: Access successful." -ForegroundColor Green
}
catch {
    Write-Host "FAILED: Access failed. $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Response: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
}
