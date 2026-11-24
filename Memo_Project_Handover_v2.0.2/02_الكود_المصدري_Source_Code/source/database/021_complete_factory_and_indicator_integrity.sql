-- Complete the integrity rules introduced by migrations 018-020 without
-- rewriting migrations that may already be recorded as applied.

do $$
begin
    if exists (
        select 1
        from core.notes n
        join core.factories f on f.id = n.factory_id
        where not n.is_deleted
          and n.factory_id is not null
          and (f.company_id is distinct from n.company_id or f.is_deleted)
    ) then
        raise exception 'توجد مذكرات مرتبطة بمصنع لا يتبع للشركة أو مصنع محذوف. صحح البيانات قبل إكمال الترقية.';
    end if;
end $$;

create or replace function core.protect_used_factory_relation()
returns trigger
language plpgsql
as $$
begin
    if exists (
        select 1
        from core.notes n
        where n.factory_id = old.id
          and not n.is_deleted
    ) and (
        new.company_id is distinct from old.company_id
        or (new.is_deleted and not old.is_deleted)
    ) then
        raise exception 'لا يمكن نقل أو حذف مصنع مستخدم في مذكرات';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_factories_protect_used_relation on core.factories;
create trigger trg_factories_protect_used_relation
before update of company_id, is_deleted on core.factories
for each row execute function core.protect_used_factory_relation();

create or replace function core.protect_company_soft_delete()
returns trigger
language plpgsql
as $$
begin
    if new.is_deleted and not old.is_deleted and (
        exists(select 1 from core.notes n where n.company_id = old.id and not n.is_deleted)
        or exists(select 1 from core.factories f where f.company_id = old.id and not f.is_deleted)
    ) then
        raise exception 'لا يمكن حذف شركة مرتبطة بمذكرات أو مصانع';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_companies_protect_soft_delete on core.companies;
create trigger trg_companies_protect_soft_delete
before update of is_deleted on core.companies
for each row execute function core.protect_company_soft_delete();

create or replace function core.validate_note_field_value()
returns trigger
language plpgsql
as $$
declare
    v_note_type_id uuid;
begin
    select note_type_id into v_note_type_id
    from core.notes
    where id = new.note_id;

    if v_note_type_id is null then
        raise exception 'المذكرة غير موجودة';
    end if;

    if new.field_value in (2, 3)
       and (new.inactive_reason is null or length(trim(new.inactive_reason)) = 0) then
        raise exception 'سبب الخطأ أو الفقد مطلوب';
    end if;

    if not exists (
        select 1
        from core.note_type_fields ntf
        join core.note_fields nf on nf.id = ntf.field_id
        where ntf.note_type_id = v_note_type_id
          and ntf.field_id = new.field_id
          and ntf.is_active
          and not ntf.is_deleted
          and nf.is_active
          and not nf.is_deleted
    ) then
        raise exception 'هذا البند غير فعال أو غير مرتبط بنوع المذكرة';
    end if;

    return new;
end;
$$;

create or replace view reporting.vw_note_details as
select n.id as note_id, n.note_number, nt.id as note_type_id, nt.name_ar as note_type_name,
       c.id as company_id, c.name as company_name, n.writing_date, n.arrival_at, n.status, n.image_path,
       count(ntf.field_id)::int as total_fields,
       count(nfv.field_id) filter (where nfv.field_value = 1)::int as active_count,
       count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::int as inactive_count,
       count(nfv.field_id) filter (where nfv.field_value = 0)::int as unknown_count,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 1)::numeric * 100 /
         nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as accuracy_percentage,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::numeric * 100 /
         nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as error_percentage
from core.notes n
join core.note_types nt on nt.id = n.note_type_id
join core.companies c on c.id = n.company_id
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = ntf.field_id
where not n.is_deleted
group by n.id, nt.id, nt.name_ar, c.id, c.name;

create or replace view reporting.vw_note_field_details as
select n.id as note_id, n.note_number, c.id as company_id, c.name as company_name,
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
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where not n.is_deleted;

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
