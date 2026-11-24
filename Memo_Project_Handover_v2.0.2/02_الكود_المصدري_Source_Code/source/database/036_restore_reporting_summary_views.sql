-- Restores the reporting views removed by migration 023 and keeps the current
-- indicator contract: 0 = unreviewed, 1 = correct, 2..6 = error classifications.

create or replace view reporting.vw_company_error_summary as
select company_id,
       company_name,
       count(distinct note_id)::int as total_notes,
       count(field_id)::int as total_fields_checked,
       count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
       count(field_id) filter (where field_value = 0)::int as total_unknown,
       coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 /
         nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as average_accuracy,
       coalesce(round(count(field_id) filter (where field_value between 2 and 6)::numeric * 100 /
         nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as error_rate
from reporting.vw_note_field_details
where field_value is not null
group by company_id, company_name;

create or replace view reporting.vw_field_error_summary as
select field_id,
       field_name,
       count(field_id)::int as total_checked,
       count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
       count(field_id) filter (where field_value = 0)::int as total_unknown,
       coalesce(round(count(field_id) filter (where field_value between 2 and 6)::numeric * 100 /
         nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as error_rate
from reporting.vw_note_field_details
where field_value is not null
group by field_id, field_name;

create or replace view reporting.vw_note_type_summary as
select nt.id as note_type_id,
       nt.name_ar as note_type_name,
       count(n.id)::int as total_notes,
       count(n.id) filter (where n.status = 'COMPLETE')::int as complete_count,
       count(n.id) filter (where n.status = 'HAS_ERRORS')::int as has_errors_count,
       count(n.id) filter (where n.status = 'NEEDS_REVIEW')::int as needs_review_count,
       count(n.id) filter (where n.status = 'APPROVED')::int as approved_count
from core.note_types nt
left join core.notes n on n.note_type_id = nt.id and not n.is_deleted
where not nt.is_deleted
group by nt.id, nt.name_ar;

create or replace view reporting.vw_daily_notes_summary as
select writing_date as day,
       count(distinct note_id)::int as total_notes,
       count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
       count(field_id) filter (where field_value = 0)::int as total_unknown,
       coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 /
         nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as average_accuracy
from reporting.vw_note_field_details
where field_value is not null
group by writing_date;