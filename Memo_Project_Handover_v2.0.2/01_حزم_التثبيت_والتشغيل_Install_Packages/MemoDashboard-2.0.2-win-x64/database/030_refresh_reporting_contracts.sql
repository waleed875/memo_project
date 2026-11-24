create or replace view reporting.vw_note_details as
select n.id as note_id, n.note_number, nt.id as note_type_id, nt.name_ar as note_type_name,
       c.id as company_id, c.name as company_name, n.factory_id, f.name as factory_name,
       n.production_line_id, pl.name as production_line_name,
       n.writing_date, n.arrival_at, n.status, n.image_path,
       n.created_at, n.created_by, creator.full_name as created_by_name,
       n.review_started_at, n.approved_at, n.approved_by, approver.full_name as approved_by_name,
       count(ntf.field_id)::int as total_fields,
       count(nfv.field_id) filter (where nfv.field_value = 1)::int as active_count,
       count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::int as inactive_count,
       count(nfv.field_id) filter (where nfv.field_value = 0)::int as unknown_count,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 1)::numeric * 100 / nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as accuracy_percentage,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::numeric * 100 / nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as error_percentage
from core.notes n
join core.note_types nt on nt.id = n.note_type_id
join core.companies c on c.id = n.company_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
left join auth.users creator on creator.id = n.created_by
left join auth.users approver on approver.id = n.approved_by
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = ntf.field_id
where not n.is_deleted
group by n.id, nt.id, nt.name_ar, c.id, c.name, f.id, f.name, pl.id, pl.name,
         creator.full_name, approver.full_name;

create or replace view reporting.vw_note_field_details as
select n.id as note_id, n.note_number, c.id as company_id, c.name as company_name,
       n.factory_id, f.name as factory_name, n.production_line_id, pl.name as production_line_name,
       nt.id as note_type_id, nt.name_ar as note_type_name, nf.id as field_id, nf.name_ar as field_name,
       nfv.field_value,
       case nfv.field_value when 0 then 'لم تتم المراجعة' when 1 then 'صحيح' when 2 then 'غير صحيح' when 3 then 'مفقود' when 4 then 'غير واضح' when 5 then 'غير مكتمل' when 6 then 'لا ينطبق' else 'غير مدخل' end as field_value_label_ar,
       nfv.inactive_reason, n.writing_date, n.arrival_at, n.status,
       n.created_at, n.review_started_at, n.approved_at
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where not n.is_deleted;

create or replace function reporting.fn_note_accuracy(p_note_id uuid)
returns table(total_fields int, active_count int, inactive_count int, unknown_count int, accuracy_percentage numeric, error_percentage numeric)
language sql stable as $$
    select total_fields, active_count, inactive_count, unknown_count, accuracy_percentage, error_percentage
    from reporting.vw_note_details where note_id = p_note_id;
$$;

create or replace function reporting.fn_company_errors(p_company_id uuid, p_from date, p_to date)
returns table(note_id uuid, note_number varchar, writing_date date, total_errors int, total_unknown int)
language sql stable as $$
    select note_id, note_number, writing_date,
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int
    from reporting.vw_note_field_details
    where company_id = p_company_id and writing_date between p_from and p_to
    group by note_id, note_number, writing_date
    having count(field_id) filter (where field_value between 2 and 6) > 0
    order by writing_date desc;
$$;

create or replace function reporting.fn_top_error_fields(p_from date, p_to date, p_limit int)
returns table(field_id uuid, field_name varchar, total_errors int, total_unknown int, error_rate numeric)
language sql stable as $$
    select field_id, field_name,
           count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
           count(field_id) filter (where field_value = 0)::int as total_unknown,
           coalesce(round(count(field_id) filter (where field_value between 2 and 6)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as error_rate
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to and field_value is not null
    group by field_id, field_name
    order by 3 desc, 4 desc
    limit p_limit;
$$;

create or replace function reporting.fn_notes_summary(p_from date, p_to date)
returns table(total_notes int, total_errors int, total_unknown int, average_accuracy numeric)
language sql stable as $$
    select count(distinct note_id)::int,
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int,
           coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0)
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to;
$$;

create or replace function reporting.fn_user_productivity(p_from date, p_to date)
returns table(user_id uuid, full_name varchar, created_notes int, updated_notes int, approved_notes int, last_activity timestamp)
language sql stable as $$
    select u.id, u.full_name,
           count(distinct n.id) filter (where n.created_by = u.id)::int,
           count(distinct n.id) filter (where n.updated_by = u.id)::int,
           count(distinct n.id) filter (where n.approved_by = u.id)::int,
           greatest(max(n.created_at), max(n.updated_at), max(n.approved_at))
    from auth.users u
    left join core.notes n on (n.created_by = u.id or n.updated_by = u.id or n.approved_by = u.id)
        and n.writing_date between p_from and p_to
    where not u.is_deleted
    group by u.id, u.full_name
    order by 3 desc, 4 desc;
$$;
