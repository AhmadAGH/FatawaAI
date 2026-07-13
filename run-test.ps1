Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Multi-Stage Search Algorithm" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Query: وقت اذكار الصباح و المساء" -ForegroundColor Yellow
Write-Host ""

$body = Get-Content test-query.json -Raw
$startTime = Get-Date

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5123/api/search" -Method POST -Body $body -ContentType "application/json; charset=utf-8"
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds
    
    Write-Host "Response received in $([math]::Round($duration, 2))s" -ForegroundColor Green
    Write-Host ""
    
    if ($response.results.Count -eq 0) {
        Write-Host "Results: EMPTY - No fatwas found" -ForegroundColor Magenta
        Write-Host ""
        Write-Host "Disclaimer:" -ForegroundColor Yellow
        Write-Host $response.disclaimer -ForegroundColor Gray
    } else {
        Write-Host "Results: $($response.results.Count) fatwas found" -ForegroundColor Green
        Write-Host ""
        
        for ($i = 0; $i -lt $response.results.Count; $i++) {
            $result = $response.results[$i]
            Write-Host "[$($i+1)] Fatwa #$($result.fatwaId)" -ForegroundColor Cyan
            Write-Host "    Collection: $($result.collectionType)" -ForegroundColor Gray
            Write-Host "    Title: $($result.title)" -ForegroundColor White
            $questionPreview = $result.question.Substring(0, [Math]::Min(150, $result.question.Length))
            Write-Host "    Question: $questionPreview..." -ForegroundColor Gray
            Write-Host ""
        }
    }
    
    if ($response.analysis) {
        Write-Host "Query Analysis:" -ForegroundColor Cyan
        Write-Host "  Fiqh Topic: $($response.analysis.fiqhTopic)" -ForegroundColor White
        Write-Host "  Keywords: $($response.analysis.keywords -join ', ')" -ForegroundColor White
        Write-Host ""
    }
    
    Write-Host "Source Attribution:" -ForegroundColor Cyan
    Write-Host $response.sourceAttribution -ForegroundColor White
    Write-Host ""
    
    Write-Host "Full Response JSON:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 10
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}


