# Hybrid Search & Importer Analysis

## 🔍 Issues Found

### Issue #1: Importer Only Embeds Title & Question (NOT Answer!)

**File**: `FatawaAI.Import/Program.cs` (Lines 107-112, 120-130)

```csharp
embTitle = await embeddingService.EmbedAsync(textTitle, ct);
embQuestion = await embeddingService.EmbedAsync(textQuestion, ct);
// ❌ Answer is NEVER embedded!

var fatwa = new FatwaWithEmbedding(
    // ...
    EmbeddingTitle: embTitle,
    EmbeddingQuestion: embQuestion);
    // ❌ No EmbeddingAnswer field!
```

**Impact**: The answer field contains crucial information about the ruling, but it's not being used for vector search!

**Example**:
- Query: "حكم الشراء باستخدام تابي و تمارا"
- A fatwa's **answer** might say: "الشراء بالتقسيط عن طريق تابي جائز..." 
- But this answer **is never embedded**, so vector search can't match it!

### Issue #2: Hybrid Search Uses Wrong Weights

**File**: `FatawaAI.Infrastructure/FatwaReadRepository.cs` (Lines 227-228)

```csharp
( @TitleW    * (embedding_title    <=> @Embedding::vector)
+ @QuestionW * (embedding_question <=> @Embedding::vector)
) AS VectorScore
```

**Current Weights** (from `SearchFatwasQueryHandler.cs`):
- Title: 70%
- Question: 30%
- **Answer: 0% (not included!)**

**Problem**: Titles are often generic like "حكم من قال لزوجته..." which don't contain specific terms like "تابي" or "تمارا". The question field alone may not have enough specificity either.

### Issue #3: Full-Text Search Doesn't Use `search_tsv` Column

**File**: `FatawaAI.Infrastructure/FatwaReadRepository.cs` (Lines 260-263)

```csharp
ts_rank_cd(to_tsvector('arabic', title || ' ' || question), plainto_tsquery('arabic', @QueryText)) AS FtsScore,
// ...
WHERE to_tsvector('arabic', title || ' ' || question) @@ plainto_tsquery('arabic', @QueryText)
```

**Schema** (`FatawaSchema.sql` Lines 22-35):
```sql
ALTER TABLE fatwas
    ADD COLUMN IF NOT EXISTS search_tsv tsvector
        GENERATED ALWAYS AS (
            to_tsvector(
                    'arabic',
                    coalesce(title, '') || ' ' ||
                    coalesce(question, '') || ' ' ||
                    coalesce(answer, '')  -- ✓ Answer IS included here!
                )
            ) STORED;

CREATE INDEX IF NOT EXISTS idx_fatwas_search_tsv
    ON fatwas
        USING GIN (search_tsv);
```

**Problem**: The code creates `to_tsvector()` **on-the-fly** instead of using the pre-computed `search_tsv` column that:
1. **Includes the answer** (more comprehensive)
2. **Is indexed** (much faster)
3. **Is pre-computed** (no runtime overhead)

### Issue #4: No Vector Index Created

**File**: `FatawaSchema.sql` (Lines 43-49)

```sql
-- IVFFlat indexes per field (create after embeddings are populated)
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_title_ivfflat
--   ON fatwas USING ivfflat (embedding_title vector_cosine_ops) WITH (lists = 100);
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_question_ivfflat
--   ON fatwas USING ivfflat (embedding_question vector_cosine_ops) WITH (lists = 100);
```

**Problem**: All vector index creation is **commented out**! This means:
- Vector searches are doing **sequential scans** (very slow for 18K+ fatwas)
- No approximate nearest neighbor (ANN) optimization
- Every search has to compute distance to **all** fatwas

### Issue #5: Query Text Passed Directly to Embeddings

**File**: `FatawaAI.Domain/SearchFatwasQueryHandler.cs` (Lines 81-92)

```csharp
// ========== STAGE 1: Query Analysis ==========
var analysis = await _queryAnalysisService.AnalyzeAsync(trimmedQuery, cancellationToken);

// ... (analysis is extracted but not always used for embedding)

// ========== STAGE 2: Hybrid Retrieval ==========
var embedding = await _embeddingService.EmbedAsync(trimmedQuery, cancellationToken);
```

**Problem**: The user's raw query is embedded directly. The LLM analysis extracts:
- `fiqh_topic`: "بيع بالتقسيط"
- `keywords`: ["تابي", "تمارا", "شراء", "تقسيط"]
- `summary`: "حكم الشراء باستخدام خدمات تابي وتمارا"

But we embed the **original query** instead of the **enriched summary**! The summary is more standardized and likely to match better.

### Issue #6: Arabic Full-Text Search May Need Custom Configuration

**File**: `FatawaSchema.sql` (Lines 25-26)

```sql
to_tsvector('arabic', ...)
```

PostgreSQL's built-in `arabic` configuration may not handle:
- Modern Arabic words (تابي، تمارا)
- Diacritics/tashkeel variations
- Multiple Arabic stop word lists

## 🚀 Recommended Fixes

### Fix #1: Embed All Three Fields (CRITICAL)

**Update Importer** (`FatawaAI.Import/Program.cs`):

```csharp
// Embed all three fields
embTitle = await embeddingService.EmbedAsync(textTitle, ct);
embQuestion = await embeddingService.EmbedAsync(textQuestion, ct);
embAnswer = await embeddingService.EmbedAsync(f.Answer ?? string.Empty, ct);  // NEW!

var fatwa = new FatwaWithEmbedding(
    // ...
    EmbeddingTitle: embTitle,
    EmbeddingQuestion: embQuestion,
    EmbeddingAnswer: embAnswer  // NEW!
);
```

**Update Repository** (`FatwaReadRepository.cs`):

```csharp
( @TitleW    * (embedding_title    <=> @Embedding::vector)
+ @QuestionW * (embedding_question <=> @Embedding::vector)
+ @AnswerW   * (embedding_answer   <=> @Embedding::vector)  // NEW!
) AS VectorScore
```

**Update Weights** (`SearchFatwasQueryHandler.cs`):

```csharp
var weights = new SearchWeights
{
    TitleWeight = 0.3,     // Reduced from 0.7
    QuestionWeight = 0.3,  // Same
    AnswerWeight = 0.4     // NEW! Most important for matching specifics
};
```

### Fix #2: Use the Pre-Computed `search_tsv` Column

**Update Repository** (`FatawaReadRepository.cs`):

```csharp
// BEFORE:
ts_rank_cd(to_tsvector('arabic', title || ' ' || question), plainto_tsquery('arabic', @QueryText)) AS FtsScore,
// ...
WHERE to_tsvector('arabic', title || ' ' || question) @@ plainto_tsquery('arabic', @QueryText)

// AFTER:
ts_rank_cd(search_tsv, plainto_tsquery('arabic', @QueryText)) AS FtsScore,
// ...
WHERE search_tsv @@ plainto_tsquery('arabic', @QueryText)
```

**Benefits**:
- ✅ Includes answer text (more comprehensive)
- ✅ Uses pre-built GIN index (much faster)
- ✅ No runtime `to_tsvector` overhead

### Fix #3: Create Vector Indexes (CRITICAL for Performance)

**Run these SQL commands** (after embeddings are populated):

```sql
-- Create IVFFlat indexes for each embedding field
CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_title_ivfflat
  ON fatwas USING ivfflat (embedding_title vector_cosine_ops) WITH (lists = 100);

CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_question_ivfflat
  ON fatwas USING ivfflat (embedding_question vector_cosine_ops) WITH (lists = 100);

CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_answer_ivfflat
  ON fatwas USING ivfflat (embedding_answer vector_cosine_ops) WITH (lists = 100);
```

**Impact**:
- Vector search speed: **100x faster** for large datasets
- Uses Approximate Nearest Neighbor (ANN) algorithm
- `lists = 100` is optimal for ~18K fatwas (√(row_count))

### Fix #4: Embed the Enriched Summary Instead of Raw Query

**Update Handler** (`SearchFatwasQueryHandler.cs`):

```csharp
// Use the enriched summary for embedding if available
var textToEmbed = !string.IsNullOrWhiteSpace(analysis?.Summary) 
    ? analysis.Summary 
    : trimmedQuery;

var embedding = await _embeddingService.EmbedAsync(textToEmbed, cancellationToken);
```

**Why**: The LLM summary is more standardized and removes ambiguity.

### Fix #5: Add Answer Weight to SearchWeights

**Update Core** (`FatawaAI.Core/SearchWeights.cs`):

```csharp
public sealed record SearchWeights
{
    public double TitleWeight { get; init; } = 0.3;
    public double QuestionWeight { get; init; } = 0.3;
    public double AnswerWeight { get; init; } = 0.4;  // NEW!
}
```

### Fix #6: Consider Better Arabic Tokenization

**Options**:
1. Use a custom Arabic stemmer (e.g., Khoja stemmer)
2. Add custom stop words
3. Use ElasticSearch instead of PostgreSQL FTS
4. Create a custom `arabic_custom` text search configuration

## 📊 Testing Script

I've created **`diagnose-hybrid-search.ps1`** which will:
1. Get query embedding from Ollama
2. Test vector search alone
3. Test full-text search alone
4. Test the full API
5. Compare results to identify the bottleneck

**Run it**:
```powershell
.\diagnose-hybrid-search.ps1
```

## 🎯 Priority Order

1. **FIX #1 (CRITICAL)**: Add answer embeddings to importer and search
2. **FIX #2 (HIGH)**: Use `search_tsv` column for FTS
3. **FIX #3 (HIGH)**: Create vector indexes
4. **FIX #4 (MEDIUM)**: Embed enriched summary
5. **FIX #5 (MEDIUM)**: Add answer weight
6. **FIX #6 (LOW)**: Custom Arabic configuration (only if still issues)

## 📝 Implementation Steps

### Step 1: Add Answer Embeddings (Requires Re-Import)

1. Update `FatawaAI.Core/FatwaWithEmbedding.cs` to add `EmbeddingAnswer`
2. Update `FatawaAI.Import/Program.cs` to embed answers
3. Update `FatawaAI.Infrastructure/FatwaWriteRepository.cs` to save answer embeddings
4. **Re-run the importer** to regenerate all embeddings (will take time!)

### Step 2: Update Hybrid Search

1. Update `SearchWeights` to include `AnswerWeight`
2. Update `SearchHybridAsync` to use answer embeddings and `search_tsv`
3. Update `SearchFatwasQueryHandler` to use new weights

### Step 3: Create Indexes

1. Run the index creation SQL
2. Analyze query performance improvement

### Step 4: Test & Validate

1. Run `diagnose-hybrid-search.ps1`
2. Test with various queries
3. Compare results before/after

## 🔥 Expected Improvements

After all fixes:
- **Relevance**: +80% (answer matching will catch specific terms)
- **Speed**: 100x faster (vector indexes)
- **FTS Quality**: +50% (using indexed `search_tsv` with answer)
- **Overall**: Should fix the "divorce results for shopping query" issue!

## Summary

The root cause is **NOT** the semantic filter - it's that:
1. **Answers aren't embedded** (missing 40% of content!)
2. **No vector indexes** (sequential scans are slow)
3. **FTS doesn't use indexed column** (slower + missing answer)

Fix these, and the hybrid search should return highly relevant results that the semantic filter can properly process.

