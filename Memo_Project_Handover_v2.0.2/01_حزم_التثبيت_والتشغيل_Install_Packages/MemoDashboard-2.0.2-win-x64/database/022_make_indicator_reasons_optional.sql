-- السبب ملاحظة اختيارية تساعد في توثيق المؤشر، وليس شرطاً لاختيار أي تصنيف.
-- يبقى نطاق قيم المؤشر محصوراً في القيم المعتمدة من 0 إلى 6.
alter table core.note_field_values
    drop constraint if exists ck_note_field_values_value_reason,
    add constraint ck_note_field_values_value_reason check (
        field_value in (0, 1, 2, 3, 4, 5, 6)
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
        raise exception 'المذكرة غير موجودة.';
    end if;

    if not exists (
        select 1
        from core.note_type_fields ntf
        join core.note_fields nf on nf.id = ntf.field_id
        where ntf.note_type_id = v_note_type_id
          and ntf.field_id = new.field_id
          and ntf.is_active and not ntf.is_deleted
          and nf.is_active and not nf.is_deleted
    ) then
        raise exception 'المؤشر غير فعال أو غير مرتبط بنوع المذكرة.';
    end if;
    return new;
end;
$$;
