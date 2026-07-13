# Fix Applied - Restart Instructions

## What Was Fixed
The `SearchHybridAsync` method was using `dynamic` types which caused Dapper mapping errors. I've replaced them with properly typed classes:
- `VectorSearchResult` - for vector search results
- `FtsSearchResult` - for full-text search results

## To Apply the Fix

### Step 1: Stop the Current API
```powershell
Get-Process -Name dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
```

### Step 2: Restart the API
```powershell
cd FatawaAI.Api
dotnet run
```

### Step 3: Test Again
```powershell
$body = Get-Content test-query.json -Raw
Invoke-RestMethod -Uri "http://localhost:5123/api/search" -Method POST -Body $body -ContentType "application/json; charset=utf-8"
```

## Changes Made
**File**: `FatawaAI.Infrastructure/FatwaReadRepository.cs`

**Added**:
```csharp
private sealed class VectorSearchResult
{
    public long FatwaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string QuestionSnippet { get; set; } = string.Empty;
    public string AnswerSnippet { get; set; } = string.Empty;
    public double VectorScore { get; set; }
    public long VectorRank { get; set; }
}

private sealed class FtsSearchResult
{
    public long FatwaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string QuestionSnippet { get; set; } = string.Empty;
    public string AnswerSnippet { get; set; } = string.Empty;
    public double FtsScore { get; set; }
    public long FtsRank { get; set; }
}
```

**Changed**:
```csharp
// Before (caused errors):
var vectorResults = (await connection.QueryAsync<dynamic>(vectorSql, parameters)).ToList();
var ftsResults = (await connection.QueryAsync<dynamic>(ftsSql, parameters)).ToList();

// After (properly typed):
var vectorResults = (await connection.QueryAsync<VectorSearchResult>(vectorSql, parameters)).ToList();
var ftsResults = (await connection.QueryAsync<FtsSearchResult>(ftsSql, parameters)).ToList();
```

✅ **Build Status**: Successful (0 errors, 0 warnings)


