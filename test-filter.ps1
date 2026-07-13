$body = @{
    query = "حكم الشراء باستخدام تابي و تمارا"
} | ConvertTo-Json -Depth 10

$response = Invoke-RestMethod -Uri "http://localhost:5123/api/search" -Method POST -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

Write-Host "========== SEARCH RESPONSE =========="
$response | ConvertTo-Json -Depth 10
Write-Host ""
Write-Host "Number of results: $($response.results.Count)"

