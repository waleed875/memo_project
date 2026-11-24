-- Migration 066 (DRAFT for review - NOT applied to any database yet).
-- FK covering indexes, performance only. No data changes, no schema-shape changes.
--
-- Why no repair for 027: 027's non-idempotent "sort_order + 1" shift is fully
-- superseded by 035, which renormalizes every TRANSFER mapping deterministically
-- (row_number + IS DISTINCT FROM). Every database at >= 035 has therefore already
-- converged, so repeating that work here would be a dead no-op. This file only
-- adds what is still missing: btree indexes on FK/author columns that 002/012/028
-- declared without them (007/029 cover only a subset).
--
-- Safety: every statement is CREATE INDEX IF NOT EXISTS (idempotent, rerunnable).
-- Tables are small (single-digit thousands of rows); plain CREATE INDEX takes a
-- brief lock only, CONCURRENTLY is unnecessary and forbidden inside the runner
-- transaction. Applying this file moves DatabaseVersion 065 -> 066, which is a
-- release decision (see docs); do NOT apply to production without the mandated
-- pg_dump backup + restore rehearsal.

-- core.notes user references (review-queue composite in 012 covers status+reviewer lookups only)
create index if not exists ix_notes_created_by on core.notes(created_by);
create index if not exists ix_notes_updated_by on core.notes(updated_by);
create index if not exists ix_notes_approved_by on core.notes(approved_by);
create index if not exists ix_notes_reviewer on core.notes(reviewer_id);

-- core.note_field_values user references (007 covers note_id/field_id only)
create index if not exists ix_note_field_values_created_by on core.note_field_values(created_by);
create index if not exists ix_note_field_values_updated_by on core.note_field_values(updated_by);

-- review history author (012 covers note_id only)
create index if not exists ix_note_review_history_user on core.note_review_history(user_id);

-- duplicate incident dimensions (028 covers classification/company/matched_note;
-- 055 covers the unique company-duplicate key only)
create index if not exists ix_duplicate_incidents_factory on core.duplicate_incidents(factory_id);
create index if not exists ix_duplicate_incidents_production_line on core.duplicate_incidents(production_line_id);
create index if not exists ix_duplicate_incidents_note_type on core.duplicate_incidents(note_type_id);
create index if not exists ix_duplicate_incidents_decided_by on core.duplicate_incidents(decided_by);
create index if not exists ix_duplicate_incidents_created_note on core.duplicate_incidents(created_note_id);

-- updates publishing/audit authorship (046 covers status/installations only)
create index if not exists ix_updates_releases_published_by on updates.releases(published_by);
create index if not exists ix_updates_releases_created_by on updates.releases(created_by);
create index if not exists ix_updates_audit_log_user on updates.audit_log(user_id);
create index if not exists ix_updates_audit_log_release on updates.audit_log(release_id);

-- singleton settings authorship (042/043 declare the columns without indexes)
create index if not exists ix_database_settings_updated_by on system.database_settings(last_updated_by);
create index if not exists ix_report_identity_settings_updated_by on system.report_identity_settings(updated_by);

-- backup history author (007 covers started_at only; 029 covers audit.logs only)
create index if not exists ix_backup_history_created_by on system.backup_history(created_by);

-- entry-note sources author (063 covers type/exit_note only)
create index if not exists ix_note_sources_created_by on core.note_sources(created_by);
