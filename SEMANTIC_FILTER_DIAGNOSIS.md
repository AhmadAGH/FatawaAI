# Semantic Filter Diagnosis - Complete Analysis

## Test Query
**Arabic**: "حكم الشراء باستخدام تابي و تمارا"  
**English**: "Ruling on buying using Tabby and Tamara"  
**Topic**: Financial transactions (installment payments)

## 🔴 Critical Issues Found

### Issue #1: Hybrid Search Returning Irrelevant Results

The hybrid search (RRF combining vector + full-text search) is retrieving **completely unrelated fatwas**:

**Query Topic**: Financial transactions (Tabby/Tamara shopping)  
**Retrieved Fatwas**: ALL about **divorce (طلاق)** and **zakat**

**Sample Retrieved Fatwas**:
- Fatwa #711: "ما الحكم إذا طلبت المرأةُ الطلاق وأبى زوجها؟" (Divorce)
- Fatwa #15801: "حكم من طلق زوجته إن هي تلفظت بما يكره" (Divorce)  
- Fatwa #11263: "حكم إخراج زكاة الفطر نقدًا" (Zakat)

**Result**: 0% relevance to the query!

###  Issue #2: LLM Context Window Exceeded

When testing with 30 fatwas:
```
"prompt_eval_count":4096  ← Hit the 4K token limit!
```

The `llama3.1:8b` model has a **4K context limit**. When we send:
- System prompt (~1.6K chars)
- 30 fatwas with titles + question/answer snippets

The LLM **truncates the input** and returns empty JSON `{}`.

### Issue #3: Semantic Filter Timeouts

Even with 15 fatwas, the semantic filter times out after **100 seconds**.

This suggests the LLM is struggling to process the input, likely because:
1. The fatwas are so irrelevant that the model is "confused"
2. The model is still hitting token limits
3. The prompt complexity is too high

### Issue #4: `format: "json"` Not Working as Expected

Despite setting `format: "json"` in Ollama, the LLM still returns:
- Empty objects `{}`
- Doesn't properly classify each fatwa as RELEVANT/PARTIAL/IRRELEVANT

## 📊 Test Results Summary

| Test | Batch Size | Result | Observations |
|------|-----------|---------|-------------|
| 1 | 100 fatwas | Timeout (100s) | Too many tokens |
| 2 | 30 fatwas | Empty JSON `{}` | Hit 4096 token limit |
| 3 | 15 fatwas | Timeout (100s) | Still struggling |

## 🎯 Root Cause Analysis

### Primary Problem: Hybrid Search Quality

The semantic filter is **working as designed** - the problem is **UPSTREAM** in the hybrid search.

**The hybrid search is returning garbage results!**

When you search for "Tabby/Tamara" (financial transactions), you should get fatwas about:
- Installment sales (البيع بالتقسيط)
- Consumer loans (القروض الاستهلاكية)
- Deferred payments (البيع الآجل)
- Usury in transactions (الربا في المعاملات)

Instead, it's returning fatwas about **divorce** and **zakat**.

### Why Is Hybrid Search Failing?

Possible causes:
1. **Vector embeddings quality**: The `nomic-embed-text` model may not understand Arabic well
2. **Full-text search configuration**: The tsvector may not be properly configured for Arabic
3. **RRF weights**: The weighting between vector and FTS may be off
4. **Query analysis**: The LLM query analyzer might not be extracting good search terms

## 🔍 Recommended Solutions

### Solution 1: Fix the Hybrid Search (HIGHEST PRIORITY)

**Check the vector search** quality:
```sql
-- Test vector search directly
SELECT 
    fatwa_id,
    title,
    embedding <=> '[vector for "tabby tamara"]' AS distance
FROM fatwas
ORDER BY distance
LIMIT 20;
```

**Check the full-text search** quality:
```sql
-- Test FTS directly  
SELECT 
    fatwa_id,
    title,
    ts_rank(fts_ar, to_tsquery('arabic', 'تابي | تمارا | بيع | تقسيط')) AS rank
FROM fatwas
WHERE fts_ar @@ to_tsquery('arabic', 'تابي | تمارا | بيع | تقسيط')
ORDER BY rank DESC
LIMIT 20;
```

### Solution 2: Reduce Semantic Filter Batch Size

Current: 15 fatwas  
Recommended: **10 fatwas** or less

This ensures we stay well under the 4K token limit.

### Solution 3: Simplify the Semantic Filter Prompt

The current prompt is very detailed (1.6K chars). Consider a simpler version:

```
Classify each fatwa as RELEVANT or IRRELEVANT to the user's question.

RELEVANT = Same topic/ruling
IRRELEVANT = Different topic

Output JSON only:
{"classifications": [{"fatwa_id": 123, "classification": "RELEVANT"}, ...]}
```

### Solution 4: Change the Model

Consider switching to a model with a larger context window:
- `mistral:7b` (8K context)
- `qwen2.5:7b` (32K context)  
- `llama3.1:70b` (128K context, but slower)

### Solution 5: Skip Semantic Filter for Small Result Sets

If hybrid search returns < 20 candidates, skip the semantic filter entirely and proceed directly to re-ranking.

## 🚀 Immediate Action Plan

1. **First**: Investigate why hybrid search is returning irrelevant results
2. **Second**: Reduce semantic filter batch size to 10
3. **Third**: Test with a different model (e.g., `qwen2.5:7b`)
4. **Fourth**: Simplify the semantic filter prompt

## 📝 Code Changes Made So Far

1. ✅ Added `format: "json"` to force JSON output
2. ✅ Reduced batch size from 120 → 30 → 15
3. ✅ Added extensive logging to debug the issue
4. ❌ **Still not working** - root cause is upstream in hybrid search

## 🎓 Key Learnings

1. **Token limits matter**: Always check `prompt_eval_count` in Ollama responses
2. **Garbage in, garbage out**: No amount of LLM filtering can fix bad retrieval
3. **Test each stage independently**: Vector search, FTS, RRF, semantic filter, re-ranking
4. **Model selection matters**: `llama3.1:8b` with 4K context is too small for this task

## Next Steps

**User should**:
1. Check the hybrid search SQL queries in `FatwaReadRepository.SearchHybridAsync`
2. Test vector and FTS quality separately
3. Consider using a model with larger context window
4. Verify the Arabic text processing is working correctly (tokenization, stemming)

