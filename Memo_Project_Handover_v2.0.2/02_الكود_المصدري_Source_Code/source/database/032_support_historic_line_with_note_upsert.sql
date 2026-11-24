-- SaveNote uses INSERT ... ON CONFLICT UPDATE. PostgreSQL runs BEFORE INSERT
-- before resolving the conflict, so recognize an existing note that keeps the
-- same factory and production line as a historical update, not a new choice.
create or replace function core.validate_note_factory_production_line()
returns trigger language plpgsql as $$
begin
    if new.production_line_id is null then
        return new;
    end if;

    if new.factory_id is null then
        raise exception 'لا يمكن اختيار خط إنتاج دون تحديد مصنع.';
    end if;

    if tg_op = 'UPDATE'
       and new.production_line_id is not distinct from old.production_line_id
       and new.factory_id is not distinct from old.factory_id then
        return new;
    end if;

    if tg_op = 'INSERT' and exists (
        select 1
        from core.notes existing
        where existing.id = new.id
          and existing.production_line_id is not distinct from new.production_line_id
          and existing.factory_id is not distinct from new.factory_id
    ) then
        return new;
    end if;

    if not exists (
        select 1
        from core.production_lines pl
        where pl.id = new.production_line_id
          and pl.factory_id = new.factory_id
          and pl.is_active
          and not pl.is_deleted
    ) then
        raise exception 'خط الإنتاج المحدد غير فعال أو لا يتبع للمصنع المختار.';
    end if;

    return new;
end;
$$;
