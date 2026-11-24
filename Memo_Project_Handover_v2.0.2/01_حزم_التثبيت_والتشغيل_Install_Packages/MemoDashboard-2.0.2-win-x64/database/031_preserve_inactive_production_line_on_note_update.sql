-- يسمح بتعديل بيانات مذكرة قديمة مع إبقاء خطها المعطّل نفسه،
-- لكنه يمنع اختيار خط معطّل لمذكرة جديدة أو تغيير المذكرة إليه.
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
