# PowerShell script to test the new search algorithm
# Usage: .\test-search-algorithm.ps1

$apiUrl = "http://localhost:5000/api/search"

# Test cases
$testCases = @(
    @{
        name = "Test 1: Specific Modern Question (Tabby/Tamara)"
        query = "حكم شراء باستخدام تابي وتمارا"
        expectedTopic = "installment purchases, Tabby/Tamara"
    },
    @{
        name = "Test 2: Prayer at Home"
        query = "حكم صلاة الجمعة في المنزل"
        expectedTopic = "Friday prayer at home"
    },
    @{
        name = "Test 3: General Sale Question"
        query = "حكم البيع بالأجل"
        expectedTopic = "deferred payment sales"
    },
    @{
        name = "Test 4: Bank Loans"
        query = "حكم القرض من البنك"
        expectedTopic = "bank loans, riba"
    },
    @{
        name = "Test 5: Modern Topic (Should Return Empty)"
        query = "حكم استخدام الذكاء الاصطناعي في الفتاوى"
        expectedTopic = "empty results (topic not in corpus)"
    }
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Multi-Stage Search Algorithm" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if API is running
try {
    $null = Invoke-RestMethod -Uri "http://localhost:5000/api/search" -Method POST -Body '{"query":"test"}' -ContentType "application/json" -ErrorAction Stop
} catch {
    Write-Host "ERROR: API is not running at $apiUrl" -ForegroundColor Red
    Write-Host "Please start the API first:" -ForegroundColor Yellow
    Write-Host "  cd FatawaAI.Api" -ForegroundColor Yellow
    Write-Host "  dotnet run" -ForegroundColor Yellow
    exit 1
}

foreach ($test in $testCases) {
    Write-Host "----------------------------------------" -ForegroundColor Yellow
    Write-Host "Running: $($test.name)" -ForegroundColor Yellow
    Write-Host "Query: $($test.query)" -ForegroundColor White
    Write-Host "Expected: $($test.expectedTopic)" -ForegroundColor Gray
    Write-Host ""

    $body = @{
        query = $test.query
    } | ConvertTo-Json

    try {
        $startTime = Get-Date
        $response = Invoke-RestMethod -Uri $apiUrl -Method POST -Body $body -ContentType "application/json"
        $endTime = Get-Date
        $duration = ($endTime - $startTime).TotalSeconds

        Write-Host "✓ Response received in $([math]::Round($duration, 2))s" -ForegroundColor Green
        Write-Host ""

        if ($response.results.Count -eq 0) {
            Write-Host "Results: EMPTY (0 fatwas)" -ForegroundColor Magenta
            Write-Host "Disclaimer: $($response.disclaimer)" -ForegroundColor Gray
        } else {
            Write-Host "Results: $($response.results.Count) fatwas found" -ForegroundColor Green
            Write-Host ""
            
            for ($i = 0; $i -lt [Math]::Min(3, $response.results.Count); $i++) {
                $result = $response.results[$i]
                Write-Host "  [$($i+1)] Fatwa #$($result.fatwaId)" -ForegroundColor Cyan
                Write-Host "      Title: $($result.title)" -ForegroundColor White
                Write-Host "      Question: $($result.question.Substring(0, [Math]::Min(100, $result.question.Length)))..." -ForegroundColor Gray
                Write-Host ""
            }

            if ($response.results.Count -gt 3) {
                Write-Host "  ... and $($response.results.Count - 3) more results" -ForegroundColor Gray
                Write-Host ""
            }
        }

        if ($response.analysis) {
            Write-Host "Analysis:" -ForegroundColor Cyan
            Write-Host "  Fiqh Topic: $($response.analysis.fiqhTopic)" -ForegroundColor White
            Write-Host "  Keywords: $($response.analysis.keywords -join ', ')" -ForegroundColor White
            Write-Host ""
        }

    } catch {
        Write-Host "✗ ERROR: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
    }

    Start-Sleep -Seconds 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Testing Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Review the results above to verify:" -ForegroundColor Yellow
Write-Host "1. Queries return topically relevant fatwas" -ForegroundColor White
Write-Host "2. Response times are 2-5 seconds" -ForegroundColor White
Write-Host "3. Empty results for queries with no matches" -ForegroundColor White
Write-Host "4. Analysis extracts correct fiqh topics" -ForegroundColor White
Write-Host ""
Write-Host "Check the API console logs for:" -ForegroundColor Yellow
Write-Host "- Semantic filter retention rates" -ForegroundColor White
Write-Host "- Confidence levels (HIGH/LOW)" -ForegroundColor White
Write-Host "- Any error messages" -ForegroundColor White

