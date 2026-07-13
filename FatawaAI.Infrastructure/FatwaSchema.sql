CREATE TABLE IF NOT EXISTS fatwas (
    fatwa_id        BIGSERIAL PRIMARY KEY,
    collection_type TEXT        NOT NULL,
    source_id       INTEGER     NOT NULL,
    title           TEXT        NOT NULL,
    question        TEXT        NOT NULL,
    answer          TEXT        NOT NULL,
    source_url      TEXT        NOT NULL,
    audio_url       TEXT,
    categories      TEXT[]      NOT NULL,
    embedding       VECTOR(768),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ
);

ALTER TABLE fatwas
    ADD CONSTRAINT IF NOT EXISTS uq_fatwas_collection_source
        UNIQUE (collection_type, source_id);

CREATE EXTENSION IF NOT EXISTS vector;

ALTER TABLE fatwas
    ADD COLUMN IF NOT EXISTS search_tsv tsvector
        GENERATED ALWAYS AS (
            to_tsvector(
                    'arabic',
                    coalesce(title, '') || ' ' ||
                    coalesce(question, '') || ' ' ||
                    coalesce(answer, '')
                )
            ) STORED;

CREATE INDEX IF NOT EXISTS idx_fatwas_search_tsv
    ON fatwas
        USING GIN (search_tsv);

-- Per-field embeddings
ALTER TABLE fatwas
    ADD COLUMN IF NOT EXISTS embedding_title vector(768),
    ADD COLUMN IF NOT EXISTS embedding_question vector(768),
    ADD COLUMN IF NOT EXISTS embedding_answer vector(768);

-- IVFFlat indexes per field (create after embeddings are populated)
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_title_ivfflat
--   ON fatwas USING ivfflat (embedding_title vector_cosine_ops) WITH (lists = 100);
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_question_ivfflat
--   ON fatwas USING ivfflat (embedding_question vector_cosine_ops) WITH (lists = 100);
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_answer_ivfflat
--   ON fatwas USING ivfflat (embedding_answer vector_cosine_ops) WITH (lists = 100);

-- NOTE: create the IVFFlat index only after embeddings are populated
-- to avoid heavy index maintenance during import.
-- Example:
-- CREATE INDEX IF NOT EXISTS idx_fatwas_embedding_ivfflat
--     ON fatwas
--         USING ivfflat (embedding vector_cosine_ops)
--         WITH (lists = 100);


