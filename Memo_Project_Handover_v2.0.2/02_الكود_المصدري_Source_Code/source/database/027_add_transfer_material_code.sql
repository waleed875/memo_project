with transfer_type as (
    select id from core.note_types where code = 'TRANSFER' and not is_deleted
), material_code as (
    select id from core.note_fields where code = 'material_code' and not is_deleted
), material_name as (
    select ntf.sort_order from core.note_type_fields ntf
    join core.note_fields nf on nf.id = ntf.field_id
    join transfer_type tt on tt.id = ntf.note_type_id
    where nf.code = 'material_name' and not ntf.is_deleted
    limit 1
)
insert into core.note_type_fields(note_type_id, field_id, sort_order, is_required, is_active, is_deleted)
select tt.id, mc.id, coalesce(mn.sort_order + 1, 1), true, true, false
from transfer_type tt cross join material_code mc left join material_name mn on true
on conflict (note_type_id, field_id) do update
set is_active = true, is_deleted = false, is_required = true,
    updated_at = now();

update core.note_type_fields later
set sort_order = later.sort_order + 1, updated_at = now()
where later.note_type_id = (select id from core.note_types where code = 'TRANSFER' and not is_deleted limit 1)
  and later.field_id <> (select id from core.note_fields where code = 'material_code' and not is_deleted limit 1)
  and later.sort_order >= coalesce((
      select sort_order from core.note_type_fields ntf
      join core.note_fields nf on nf.id = ntf.field_id
      where ntf.note_type_id = later.note_type_id and nf.code = 'material_code'
  ), 1);
