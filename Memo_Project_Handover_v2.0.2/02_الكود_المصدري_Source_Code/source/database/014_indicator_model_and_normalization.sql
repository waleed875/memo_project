-- Align legacy indicator values with the seven-state review model.
alter table core.note_field_values
    drop constraint if exists ck_note_field_values_value_reason;

update core.note_field_values
set field_value = case field_value
    when 0 then 3
    when 1 then 1
    when 2 then 4
    when 3 then 0
    else field_value
end;

alter table core.note_field_values
    drop constraint if exists ck_note_field_values_value_reason,
    add constraint ck_note_field_values_value_reason check (
        field_value in (0, 1, 2, 3, 4, 5, 6)
        and (
            field_value not in (2, 3)
            or inactive_reason is not null and length(trim(inactive_reason)) > 0
        )
    );

create or replace function core.validate_note_field_value()
returns trigger
language plpgsql
as $$
declare
    v_note_type_id uuid;
begin
    select note_type_id into v_note_type_id from core.notes where id = new.note_id;

    if v_note_type_id is null then
        raise exception 'المذكرة غير موجودة';
    end if;

    if new.field_value in (2, 3) and (new.inactive_reason is null or length(trim(new.inactive_reason)) = 0) then
        raise exception 'سبب الخطأ أو الفقد مطلوب';
    end if;

    if not exists (
        select 1 from core.note_type_fields ntf
        where ntf.note_type_id = v_note_type_id and ntf.field_id = new.field_id
          and ntf.is_active = true and ntf.is_deleted = false
    ) then
        raise exception 'هذا البند غير مرتبط بنوع المذكرة';
    end if;

    return new;
end;
$$;

update core.notes
set note_number_normalized = upper(regexp_replace(note_number, '[^[:alnum:]]', '', 'g'));

create or replace view reporting.vw_note_details as
select
    n.id as note_id, n.note_number, nt.id as note_type_id, nt.name_ar as note_type_name,
    c.id as company_id, c.name as company_name, n.writing_date, n.arrival_at, n.status, n.image_path,
    count(ntf.field_id)::int as total_fields,
    count(nfv.field_id) filter (where nfv.field_value = 1)::int as active_count,
    count(nfv.field_id) filter (where nfv.field_value in (2, 3, 5))::int as inactive_count,
    count(nfv.field_id) filter (where nfv.field_value = 4)::int as unknown_count,
    coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 1)::numeric * 100 /
        nullif(count(nfv.field_id) filter (where nfv.field_value not in (0, 6)), 0), 2), 0) as accuracy_percentage,
    coalesce(round(count(nfv.field_id) filter (where nfv.field_value in (2, 3, 5))::numeric * 100 /
        nullif(count(nfv.field_id) filter (where nfv.field_value not in (0, 6)), 0), 2), 0) as error_percentage
from core.notes n
join core.note_types nt on nt.id = n.note_type_id
join core.companies c on c.id = n.company_id
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = ntf.field_id
where not n.is_deleted
group by n.id, nt.id, nt.name_ar, c.id, c.name;

create or replace view reporting.vw_note_field_details as
select
    n.id as note_id, n.note_number, c.id as company_id, c.name as company_name,
    nt.id as note_type_id, nt.name_ar as note_type_name, nf.id as field_id, nf.name_ar as field_name,
    nfv.field_value,
    case nfv.field_value
        when 0 then 'لم تتم المراجعة'
        when 1 then 'صحيح'
        when 2 then 'غير صحيح'
        when 3 then 'مفقود'
        when 4 then 'غير واضح'
        when 5 then 'غير مكتمل'
        when 6 then 'لا ينطبق'
        else 'غير مدخل'
    end as field_value_label_ar,
    nfv.inactive_reason, n.writing_date, n.arrival_at, n.status
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where not n.is_deleted;
