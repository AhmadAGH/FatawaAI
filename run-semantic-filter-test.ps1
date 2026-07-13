# Test script for semantic filter with Tabby/Tamara query

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Semantic Filter Test - Tabby/Tamara  " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Ollama is running
Write-Host "Checking if Ollama is running..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -Method GET -TimeoutSec 5
    Write-Host "Ollama is running" -ForegroundColor Green
} catch {
    Write-Host "Ollama is NOT running!" -ForegroundColor Red
    Write-Host "Please start Ollama first: ollama serve" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Build and run the test
Write-Host "Building test project..." -ForegroundColor Yellow
dotnet build TestSemanticFilter\TestSemanticFilter.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Running semantic filter test..." -ForegroundColor Yellow
Write-Host "This may take a few minutes as the LLM processes the request..." -ForegroundColor Cyan
Write-Host ""

dotnet run --project TestSemanticFilter\TestSemanticFilter.csproj

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Test Complete  " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

