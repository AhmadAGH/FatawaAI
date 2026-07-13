# Diagnose Hybrid Search - Test vector and FTS separately
# Run this to see which component is returning irrelevant results

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "HYBRID SEARCH DIAGNOSIS" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test query
$query = "حكم الشراء باستخدام تابي و تمارا"
Write-Host "Test Query: $query" -ForegroundColor Yellow
Write-Host "(English: Ruling on buying using Tabby and Tamara - installment payments)" -ForegroundColor Yellow
Write-Host ""

# Step 1: Get the query embedding
Write-Host "[1/4] Getting query embedding from Ollama..." -ForegroundColor Green
$embedRequest = @{
    model = "nomic-embed-text"
    input = $query
} | ConvertTo-Json

$embedResponse = Invoke-RestMethod -Uri "http://localhost:11434/api/embed" -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($embedRequest)) -ContentType "application/json; charset=utf-8"
$embedding = $embedResponse.embeddings[0]
Write-Host "✓ Got embedding vector (length: $($embedding.Count))" -ForegroundColor Green
Write-Host ""

# Step 2: Test Vector Search Only
Write-Host "[2/4] Testing VECTOR SEARCH (top 10 results)..." -ForegroundColor Green
$vectorArray = "@ARRAY[" + ($embedding -join ",") + "]"

$vectorQuery = @"
SELECT 
    fatwa_id,
    title,
    left(question, 150) AS question_snippet,
    (
        0.7 * (embedding_title <=> '$vectorArray'::vector) +
        0.3 * (embedding_question <=> '$vectorArray'::vector)
    ) AS distance
FROM fatwas
WHERE embedding_title IS NOT NULL
  AND embedding_question IS NOT NULL
ORDER BY distance
LIMIT 10;
"@

try {
    $env:PGPASSWORD = "postgres"
    $vectorResults = & psql -h localhost -p 5432 -U postgres -d fatawa -c $vectorQuery -A -t -F"|"
    
    if ($vectorResults) {
        Write-Host "VECTOR SEARCH RESULTS:" -ForegroundColor Cyan
        $i = 1
        foreach ($line in $vectorResults) {
            if ($line.Trim()) {
                $parts = $line -split "\|"
                $id = $parts[0]
                $title = $parts[1]
                $snippet = $parts[2]
                $distance = $parts[3]
                
                Write-Host "$i. [ID: $id] Distance: $distance" -ForegroundColor White
                Write-Host "   Title: $title" -ForegroundColor Gray
                Write-Host "   Question: $snippet" -ForegroundColor DarkGray
                Write-Host ""
                $i++
            }
        }
    } else {
        Write-Host "⚠ No vector results found!" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Vector search failed: $_" -ForegroundColor Red
}

Write-Host ""

# Step 3: Test Full-Text Search Only  
Write-Host "[3/4] Testing FULL-TEXT SEARCH (top 10 results)..." -ForegroundColor Green

$ftsQuery = @"
SELECT 
    fatwa_id,
    title,
    left(question, 150) AS question_snippet,
    ts_rank_cd(
        to_tsvector('arabic', title || ' ' || question),
        plainto_tsquery('arabic', '$query')
    ) AS rank
FROM fatwas
WHERE to_tsvector('arabic', title || ' ' || question) @@ plainto_tsquery('arabic', '$query')
ORDER BY rank DESC
LIMIT 10;
"@

try {
    $ftsResults = & psql -h localhost -p 5432 -U postgres -d fatawa -c $ftsQuery -A -t -F"|"
    
    if ($ftsResults) {
        Write-Host "FULL-TEXT SEARCH RESULTS:" -ForegroundColor Cyan
        $i = 1
        foreach ($line in $ftsResults) {
            if ($line.Trim()) {
                $parts = $line -split "\|"
                $id = $parts[0]
                $title = $parts[1]
                $snippet = $parts[2]
                $rank = $parts[3]
                
                Write-Host "$i. [ID: $id] Rank: $rank" -ForegroundColor White
                Write-Host "   Title: $title" -ForegroundColor Gray
                Write-Host "   Question: $snippet" -ForegroundColor DarkGray
                Write-Host ""
                $i++
            }
        }
    } else {
        Write-Host "⚠ No FTS results found!" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ FTS search failed: $_" -ForegroundColor Red
}

Write-Host ""

# Step 4: Check what the API is actually using
Write-Host "[4/4] Testing actual API search..." -ForegroundColor Green

try {
    $apiRequest = @{
        query = $query
    } | ConvertTo-Json

    $apiResponse = Invoke-RestMethod -Uri "http://localhost:5123/api/search" -Method POST -Body ([Text.Encoding]::UTF8.GetBytes($apiRequest)) -ContentType "application/json; charset=utf-8" -TimeoutSec 120
    
    Write-Host "API SEARCH RESULTS:" -ForegroundColor Cyan
    Write-Host "Results returned: $($apiResponse.results.Count)" -ForegroundColor White
    
    if ($apiResponse.results.Count -gt 0) {
        $i = 1
        foreach ($result in $apiResponse.results) {
            Write-Host "$i. [ID: $($result.id)]" -ForegroundColor White
            Write-Host "   Title: $($result.title)" -ForegroundColor Gray
            Write-Host "   Question: $($result.questionSnippet)" -ForegroundColor DarkGray
            Write-Host ""
            $i++
        }
    } else {
        Write-Host "⚠ No results returned!" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ API search failed: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DIAGNOSIS COMPLETE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "ANALYSIS:" -ForegroundColor Yellow
Write-Host "- If Vector Search returns relevant results: Vector embeddings are good" -ForegroundColor White
Write-Host "- If FTS returns relevant results: Full-text search is working" -ForegroundColor White
Write-Host "- If both fail: Problem is with the data itself or query processing" -ForegroundColor White
Write-Host "- If API fails: Problem is in the hybrid search RRF or filtering" -ForegroundColor White

