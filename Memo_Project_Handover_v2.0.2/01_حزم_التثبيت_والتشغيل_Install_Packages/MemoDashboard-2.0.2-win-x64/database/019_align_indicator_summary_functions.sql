create or replace function reporting.fn_company_errors(p_company_id uuid, p_from date, p_to date)
returns table(note_id uuid, note_number varchar, writing_date date, total_errors int, total_unknown int)
language sql
stable
as $$
    select note_id, note_number, writing_date,
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int
    from reporting.vw_note_field_details
    where company_id = p_company_id
      and writing_date between p_from and p_to
    group by note_id, note_number, writing_date
    having count(field_id) filter (where field_value between 2 and 6) > 0
    order by writing_date desc;
$$;

create or replace function reporting.fn_top_error_fields(p_from date, p_to date, p_limit int)
returns table(field_id uuid, field_name varchar, total_errors int, total_unknown int, error_rate numeric)
language sql
stable
as $$
    select field_id, field_name,
           count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
           count(field_id) filter (where field_value = 0)::int as total_unknown,
           coalesce(round(count(field_id) filter (where field_value between 2 and 6)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as error_rate
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
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int,
           coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0)
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to;
$$;
