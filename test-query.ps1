# Test the search algorithm with a specific query
$apiUrl = "http://localhost:5000/api/search"
$query = "وقت اذكار الصباح و المساء"

Write-Host "Testing query: $query" -ForegroundColor Cyan
Write-Host ""

try {
    $body = @{
        query = $query
    } | ConvertTo-Json -Depth 10

    $startTime = Get-Date
    $response = Invoke-RestMethod -Uri $apiUrl -Method POST -Body $body -ContentType "application/json"
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds

    Write-Host "✓ Response received in $([math]::Round($duration, 2))s" -ForegroundColor Green
    Write-Host ""

    if ($response.results.Count -eq 0) {
        Write-Host "Results: EMPTY (0 fatwas)" -ForegroundColor Magenta
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
            Write-Host "    Question: $($result.question.Substring(0, [Math]::Min(150, $result.question.Length)))..." -ForegroundColor Gray
            Write-Host "    Answer: $($result.answer.Substring(0, [Math]::Min(200, $result.answer.Length)))..." -ForegroundColor Gray
            Write-Host ""
        }
    }

    if ($response.analysis) {
        Write-Host "Query Analysis:" -ForegroundColor Cyan
        Write-Host "  Fiqh Topic: $($response.analysis.fiqhTopic)" -ForegroundColor White
        Write-Host "  Keywords: $($response.analysis.keywords -join ', ')" -ForegroundColor White
        Write-Host "  Summary: $($response.analysis.summary)" -ForegroundColor White
        Write-Host ""
    }

    Write-Host "Source Attribution:" -ForegroundColor Cyan
    Write-Host $response.sourceAttribution -ForegroundColor White

} catch {
    Write-Host "✗ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Make sure the API is running:" -ForegroundColor Yellow
    Write-Host "  cd FatawaAI.Api" -ForegroundColor White
    Write-Host "  dotnet run" -ForegroundColor White
}

