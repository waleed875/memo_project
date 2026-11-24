create unique index if not exists ux_notes_business_key_active
    on core.notes(note_number, note_type_id, company_id)
    where is_deleted = false;

alter table core.notes
    drop constraint if exists ck_notes_status,
    add constraint ck_notes_status check (status in ('DRAFT','COMPLETE','HAS_ERRORS','NEEDS_REVIEW','APPROVED'));

alter table core.note_field_values
    drop constraint if exists ck_note_field_values_value_reason,
    add constraint ck_note_field_values_value_reason check (
        field_value in (0, 1, 2)
        and (
            field_value <> 0
            or inactive_reason is not null and length(trim(inactive_reason)) > 0
        )
    );

alter table system.connection_profiles
    drop constraint if exists ck_connection_profiles_type,
    add constraint ck_connection_profiles_type check (profile_type in ('SERVER', 'LOCAL'));

alter table system.backup_history
    drop constraint if exists ck_backup_history_type,
    add constraint ck_backup_history_type check (backup_type in ('SERVER_TO_FILE', 'FILE_TO_LOCAL'));

alter table system.backup_history
    drop constraint if exists ck_backup_history_status,
    add constraint ck_backup_history_status check (status in ('RUNNING', 'SUCCESS', 'FAILED'));

create or replace function common.set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

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

    if new.field_value = 0 and (new.inactive_reason is null or length(trim(new.inactive_reason)) = 0) then
        raise exception 'سبب الخطأ مطلوب عند اختيار غير موجود';
    end if;

    if not exists (
        select 1
        from core.note_type_fields ntf
        where ntf.note_type_id = v_note_type_id
          and ntf.field_id = new.field_id
          and ntf.is_active = true
          and ntf.is_deleted = false
    ) then
        raise exception 'هذا البند غير مرتبط بنوع المذكرة';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_note_field_values_validate on core.note_field_values;
create trigger trg_note_field_values_validate
before insert or update on core.note_field_values
for each row execute function core.validate_note_field_value();

do $$
declare
    r record;
begin
    for r in
        select table_schema, table_name
        from information_schema.columns
        where column_name = 'updated_at'
          and table_schema in ('auth','core','system')
        group by table_schema, table_name
    loop
        execute format('drop trigger if exists trg_%I_set_updated_at on %I.%I', r.table_name, r.table_schema, r.table_name);
        execute format(
            'create trigger trg_%I_set_updated_at before update on %I.%I for each row execute function common.set_updated_at()',
            r.table_name, r.table_schema, r.table_name
        );
    end loop;
end $$;
