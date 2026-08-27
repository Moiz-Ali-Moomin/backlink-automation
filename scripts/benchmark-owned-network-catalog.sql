\set ON_ERROR_STOP on
\timing on

BEGIN;

INSERT INTO projects (id, name, primary_domain, description, status, created_at, updated_at)
VALUES ('10000000-0000-7000-8000-000000000001', 'Owned catalog benchmark',
        'owned-benchmark.invalid', 'Rollback-only controlled benchmark fixture', 'Active', now(), now());

INSERT INTO owned_network_profiles
    (id, project_id, name, description, ownership_status, automation_permitted, optional_network_tag,
     default_identity_pool_id, default_template_pool_id, max_concurrency, per_domain_concurrency,
     per_domain_delay_milliseconds, enabled, created_at, updated_at)
VALUES
    ('10000000-0000-7000-8000-000000000002', '10000000-0000-7000-8000-000000000001',
     'Owned benchmark network', 'Rollback-only controlled benchmark fixture', 'Owned', true, 'benchmark',
     NULL, NULL, 100, 2, 1000, true, now(), now());

\echo Generating :row_count submission-source rows
INSERT INTO submission_sources
    (id, project_id, owned_network_profile_id, source_import_id, original_url, normalized_url, domain, host,
     platform, cms_type, opportunity_type, adapter_name, ownership_status, automation_permitted,
     technical_compatibility, validation_status, validation_reason, detection_reason, requires_browser,
     requires_authentication, requires_manual_action, supports_wordpress_comment, supports_owned_wordpress_api,
     supports_owned_property_placement, post_id, comment_endpoint, detected_form_action, page_title, canonical_url,
     last_http_status, last_content_type, last_content_length, redirect_chain, tag, last_validated_at,
     last_submission_at, last_successful_submission_at, success_count, failure_count, pending_moderation_count,
     verified_count, lost_count, enabled, created_at, updated_at)
SELECT
    md5('source-' || value::text)::uuid,
    '10000000-0000-7000-8000-000000000001'::uuid,
    '10000000-0000-7000-8000-000000000002'::uuid,
    NULL,
    'https://site' || (value % 1000)::text || '.owned-benchmark.invalid/post/' || value::text,
    'https://site' || (value % 1000)::text || '.owned-benchmark.invalid/post/' || value::text,
    'site' || (value % 1000)::text || '.owned-benchmark.invalid',
    'site' || (value % 1000)::text || '.owned-benchmark.invalid',
    'WordPress', 'WordPress', 'WordPressComment', 'OwnedWordPressCommentAdapter', 'Owned', true,
    'Compatible', 'Valid', 'benchmark fixture', 'benchmark fixture', false, false, false, true, false, false,
    value, 'https://site' || (value % 1000)::text || '.owned-benchmark.invalid/wp-comments-post.php',
    'https://site' || (value % 1000)::text || '.owned-benchmark.invalid/wp-comments-post.php',
    'Benchmark post ' || value::text, NULL, 200, 'text/html', 1024, ARRAY[]::text[],
    CASE WHEN value % 2 = 0 THEN 'even' ELSE 'odd' END,
    now(), NULL, NULL, 0, 0, 0, 0, 0, true,
    '2026-01-01 00:00:00+00'::timestamptz + value * interval '1 microsecond',
    '2026-01-01 00:00:00+00'::timestamptz + value * interval '1 microsecond'
FROM generate_series(1, :row_count) AS generated(value);

ANALYZE submission_sources;

\echo Catalog relation and index sizes
SELECT pg_total_relation_size('submission_sources') AS total_bytes,
       pg_relation_size('submission_sources') AS table_bytes,
       pg_indexes_size('submission_sources') AS index_bytes,
       pg_size_pretty(pg_total_relation_size('submission_sources')) AS total_pretty,
       pg_size_pretty(pg_relation_size('submission_sources')) AS table_pretty,
       pg_size_pretty(pg_indexes_size('submission_sources')) AS indexes_pretty;

\echo Campaign selection: first keyset page
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
SELECT id, created_at
FROM submission_sources
WHERE project_id = '10000000-0000-7000-8000-000000000001'
  AND owned_network_profile_id = '10000000-0000-7000-8000-000000000002'
  AND automation_permitted
  AND technical_compatibility = 'Compatible'
  AND validation_status = 'Valid'
  AND enabled
ORDER BY created_at, id
LIMIT 500;

\echo Campaign selection: deep keyset page
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
SELECT id, created_at
FROM submission_sources
WHERE project_id = '10000000-0000-7000-8000-000000000001'
  AND owned_network_profile_id = '10000000-0000-7000-8000-000000000002'
  AND automation_permitted
  AND technical_compatibility = 'Compatible'
  AND validation_status = 'Valid'
  AND enabled
  AND created_at > '2026-01-01 00:00:00+00'::timestamptz + (:row_count * 9 / 10) * interval '1 microsecond'
ORDER BY created_at, id
LIMIT 500;

\echo Validation candidate selection
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
SELECT id, created_at
FROM submission_sources
WHERE project_id = '10000000-0000-7000-8000-000000000001'
  AND owned_network_profile_id = '10000000-0000-7000-8000-000000000002'
  AND enabled
ORDER BY created_at, id
LIMIT 500;

\echo Project-scoped normalized URL deduplication
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
SELECT id
FROM submission_sources
WHERE project_id = '10000000-0000-7000-8000-000000000001'
  AND normalized_url = 'https://site1.owned-benchmark.invalid/post/1';

\echo Representative filtered source query
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
SELECT id, normalized_url, domain
FROM submission_sources
WHERE project_id = '10000000-0000-7000-8000-000000000001'
  AND owned_network_profile_id = '10000000-0000-7000-8000-000000000002'
  AND platform = 'WordPress'
  AND cms_type = 'WordPress'
  AND tag = 'even'
  AND automation_permitted
  AND enabled
ORDER BY created_at, id
LIMIT 100;

ROLLBACK;
