create or replace function reporting.fn_note_accuracy(p_note_id uuid)
returns table(total_fields int, active_count int, inactive_count int, unknown_count int, accuracy_percentage numeric, error_percentage numeric)
language sql
stable
as $$
    select total_fields, active_count, inactive_count, unknown_count, accuracy_percentage, error_percentage
    from reporting.vw_note_details
    where note_id = p_note_id;
$$;

create or replace function reporting.fn_company_errors(p_company_id uuid, p_from date, p_to date)
returns table(note_id uuid, note_number varchar, writing_date date, total_errors int, total_unknown int)
language sql
stable
as $$
    select note_id, note_number, writing_date,
           count(field_id) filter (where field_value = 0)::int,
           count(field_id) filter (where field_value = 2)::int
    from reporting.vw_note_field_details
    where company_id = p_company_id
      and writing_date between p_from and p_to
    group by note_id, note_number, writing_date
    having count(field_id) filter (where field_value in (0,2)) > 0
    order by writing_date desc;
$$;

create or replace function reporting.fn_top_error_fields(p_from date, p_to date, p_limit int)
returns table(field_id uuid, field_name varchar, total_errors int, total_unknown int, error_rate numeric)
language sql
stable
as $$
    select field_id, field_name,
           count(field_id) filter (where field_value = 0)::int as total_errors,
           count(field_id) filter (where field_value = 2)::int as total_unknown,
           coalesce(round(count(field_id) filter (where field_value = 0)::numeric * 100 / nullif(count(field_id), 0), 2), 0) as error_rate
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to
      and field_value is not null
    group by field_id, field_name
    order by total_errors desc, total_unknown desc
    limit p_limit;
$$;

create or replace function reporting.fn_notes_summary(p_from date, p_to date)
returns table(total_notes int, total_errors int, total_unknown int, average_accuracy numeric)
language sql
stable
as $$
    select count(distinct note_id)::int,
           count(field_id) filter (where field_value = 0)::int,
           count(field_id) filter (where field_value = 2)::int,
           coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id), 0), 2), 0)
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to;
$$;

create or replace function reporting.fn_user_productivity(p_from date, p_to date)
returns table(user_id uuid, full_name varchar, created_notes int, updated_notes int, approved_notes int, last_activity timestamp)
language sql
stable
as $$
    select u.id,
           u.full_name,
           count(distinct n.id) filter (where n.created_by = u.id)::int,
           count(distinct n.id) filter (where n.updated_by = u.id)::int,
           count(distinct n.id) filter (where n.approved_by = u.id)::int,
           greatest(max(n.created_at), max(n.updated_at), max(n.approved_at)) as last_activity
    from auth.users u
    left join core.notes n on (n.created_by = u.id or n.updated_by = u.id or n.approved_by = u.id)
        and n.writing_date between p_from and p_to
    where not u.is_deleted
    group by u.id, u.full_name
    order by 3 desc, 4 desc;
$$;
