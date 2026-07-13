# Simple test script - just build and run

Write-Host "Building test project..." -ForegroundColor Yellow
dotnet build TestSemanticFilter\TestSemanticFilter.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Running semantic filter test..." -ForegroundColor Cyan
Write-Host "Note: This may take a few minutes for the LLM to process" -ForegroundColor Yellow
Write-Host ""

dotnet run --project TestSemanticFilter\TestSemanticFilter.csproj

