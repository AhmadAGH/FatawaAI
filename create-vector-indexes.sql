-- Create Vector Indexes for Fast Similarity Search
-- Run this AFTER embeddings are populated in the database
-- These indexes use IVFFlat (Inverted File with Flat compression) for approximate nearest neighbor search

-- IVFFlat parameters:
-- - lists: Number of clusters (optimal = sqrt(row_count))
-- - For ~18K fatwas: sqrt(18000) ≈ 134, we use 100 for simplicity
-- - vector_cosine_ops: Use cosine distance (same as <=> operator)

\echo 'Creating vector indexes for embedding_title...'
CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_title_ivfflat
  ON fatwas USING ivfflat (embedding_title vector_cosine_ops) 
  WITH (lists = 100);

\echo 'Creating vector indexes for embedding_question...'
CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_question_ivfflat
  ON fatwas USING ivfflat (embedding_question vector_cosine_ops) 
  WITH (lists = 100);

\echo 'Vector indexes created successfully!'
\echo ''
\echo 'Analyzing table to update statistics...'
ANALYZE fatwas;

\echo ''
\echo '✓ Done! Vector searches should now be ~100x faster.'
\echo ''
\echo 'To verify indexes were created, run:'
\echo '  SELECT indexname, indexdef FROM pg_indexes WHERE tablename = ''fatwas'' AND indexname LIKE ''%ivfflat%'';'

