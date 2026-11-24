create or replace function core.validate_note_factory_company()
returns trigger
language plpgsql
as $$
begin
    if new.factory_id is not null and not exists (
        select 1
        from core.factories f
        where f.id = new.factory_id
          and f.company_id = new.company_id
          and f.is_active = true
          and f.is_deleted = false
    ) then
        raise exception 'المصنع المختار يجب أن يكون نشطاً ويتبع للشركة المحددة';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_notes_validate_factory_company on core.notes;
create trigger trg_notes_validate_factory_company
before insert or update of company_id, factory_id on core.notes
for each row execute function core.validate_note_factory_company();
