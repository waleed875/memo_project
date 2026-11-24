create or replace view reporting.vw_note_details as
select
    n.id as note_id,
    n.note_number,
    nt.id as note_type_id,
    nt.name_ar as note_type_name,
    c.id as company_id,
    c.name as company_name,
    n.writing_date,
    n.arrival_at,
    n.status,
    n.image_path,
    count(ntf.field_id)::int as total_fields,
    count(nfv.field_id) filter (where nfv.field_value = 1)::int as active_count,
    count(nfv.field_id) filter (where nfv.field_value = 0)::int as inactive_count,
    count(nfv.field_id) filter (where nfv.field_value = 2)::int as unknown_count,
    coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 1)::numeric * 100 / nullif(count(ntf.field_id), 0), 2), 0) as accuracy_percentage,
    coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 0)::numeric * 100 / nullif(count(ntf.field_id), 0), 2), 0) as error_percentage
from core.notes n
join core.note_types nt on nt.id = n.note_type_id
join core.companies c on c.id = n.company_id
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = ntf.field_id
where not n.is_deleted
group by n.id, nt.id, nt.name_ar, c.id, c.name;

create or replace view reporting.vw_note_field_details as
select
    n.id as note_id,
    n.note_number,
    c.id as company_id,
    c.name as company_name,
    nt.id as note_type_id,
    nt.name_ar as note_type_name,
    nf.id as field_id,
    nf.name_ar as field_name,
    nfv.field_value,
    case nfv.field_value when 1 then 'موجود' when 0 then 'غير موجود' when 2 then 'مجهول' else 'غير مدخل' end as field_value_label_ar,
    nfv.inactive_reason,
    n.writing_date,
    n.arrival_at,
    n.status
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where not n.is_deleted;

create or replace view reporting.vw_company_error_summary as
select
    company_id,
    company_name,
    count(distinct note_id)::int as total_notes,
    count(field_id)::int as total_fields_checked,
    count(field_id) filter (where field_value = 0)::int as total_errors,
    count(field_id) filter (where field_value = 2)::int as total_unknown,
    coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id), 0), 2), 0) as average_accuracy,
    coalesce(round(count(field_id) filter (where field_value = 0)::numeric * 100 / nullif(count(field_id), 0), 2), 0) as error_rate
from reporting.vw_note_field_details
where field_value is not null
group by company_id, company_name;

create or replace view reporting.vw_field_error_summary as
select
    field_id,
    field_name,
    count(field_id)::int as total_checked,
    count(field_id) filter (where field_value = 0)::int as total_errors,
    count(field_id) filter (where field_value = 2)::int as total_unknown,
    coalesce(round(count(field_id) filter (where field_value = 0)::numeric * 100 / nullif(count(field_id), 0), 2), 0) as error_rate
from reporting.vw_note_field_details
where field_value is not null
group by field_id, field_name;

create or replace view reporting.vw_note_type_summary as
select
    nt.id as note_type_id,
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
select
    writing_date as day,
    count(distinct note_id)::int as total_notes,
    count(field_id) filter (where field_value = 0)::int as total_errors,
    count(field_id) filter (where field_value = 2)::int as total_unknown,
    coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id), 0), 2), 0) as average_accuracy
from reporting.vw_note_field_details
where field_value is not null
group by writing_date;
