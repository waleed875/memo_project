-- Supersedes the reporting contracts from 036 without modifying that immutable migration.
-- CREATE OR REPLACE VIEW cannot rename or insert columns in the existing
-- contract.  Recreate these two internal reporting views so a fresh database
-- and an upgrade from 036 expose the same column order and names.
drop view if exists reporting.vw_note_type_summary;
create or replace view reporting.vw_note_type_summary as
select nt.id as note_type_id, nt.name_ar as note_type_name,
       count(n.id)::int as total_notes,
       count(n.id) filter (where n.status = 'DRAFT')::int as draft_count,
       count(n.id) filter (where n.status = 'UNDER_REVIEW')::int as under_review_count,
       count(n.id) filter (where n.status = 'RETURNED_FOR_CORRECTION')::int as returned_for_correction_count,
       count(n.id) filter (where n.status = 'COMPLETE')::int as complete_count,
       count(n.id) filter (where n.status = 'HAS_ERRORS')::int as has_errors_count,
       count(n.id) filter (where n.status = 'NEEDS_REVIEW')::int as needs_review_count,
       count(n.id) filter (where n.status = 'APPROVED')::int as approved_count
from core.note_types nt left join core.notes n on n.note_type_id = nt.id and not n.is_deleted
where not nt.is_deleted group by nt.id, nt.name_ar;

drop view if exists reporting.vw_daily_notes_summary;
create or replace view reporting.vw_daily_notes_summary as
select coalesce(n.writing_date, n.arrival_at::date) as day,
       count(n.id)::int as total_notes,
       count(v.field_id) filter (where v.field_value between 2 and 6)::int as total_errors,
       count(v.field_id) filter (where v.field_value = 0)::int as total_unknown,
       coalesce(round(count(v.field_id) filter (where v.field_value = 1)::numeric * 100 / nullif(count(v.field_id) filter (where v.field_value <> 0), 0), 2), 0) as average_accuracy
from core.notes n left join core.note_field_values v on v.note_id = n.id
where not n.is_deleted group by coalesce(n.writing_date, n.arrival_at::date);
