-- Canonicalize the TRANSFER mapping without touching ENTRY or EXIT.
-- This corrects upgraded databases where the 027 upsert encountered an
-- existing inactive or incorrectly ordered material_code mapping.
insert into core.note_fields(code, name_ar, default_sort_order, is_active, is_deleted)
values ('material_code', 'رمز المادة', 12, true, false)
on conflict (code) do update
set name_ar = excluded.name_ar,
    is_active = true,
    is_deleted = false,
    updated_at = now();

with transfer_type as (
    select id from core.note_types where code = 'TRANSFER' and not is_deleted
), material_field as (
    select id from core.note_fields where code = 'material_code' and not is_deleted
), material_name as (
    select ntf.sort_order
    from core.note_type_fields ntf
    join core.note_fields field on field.id = ntf.field_id
    join transfer_type transfer on transfer.id = ntf.note_type_id
    where field.code = 'material_name' and not ntf.is_deleted
    limit 1
)
insert into core.note_type_fields(note_type_id, field_id, sort_order, is_required, is_active, is_deleted)
select transfer.id, material.id, coalesce(name.sort_order + 1, 1), true, true, false
from transfer_type transfer
cross join material_field material
left join material_name name on true
on conflict (note_type_id, field_id) do update
set sort_order = excluded.sort_order,
    is_required = true,
    is_active = true,
    is_deleted = false,
    updated_at = now();

-- Preserve the relative order of every other TRANSFER indicator, then insert
-- material_code immediately after material_name and renumber deterministically.
with transfer_type as (
    select id from core.note_types where code = 'TRANSFER' and not is_deleted
), ranked_without_code as (
    select ntf.id,
           field.code,
           row_number() over (order by ntf.sort_order, ntf.id)::int as base_position
    from core.note_type_fields ntf
    join core.note_fields field on field.id = ntf.field_id
    join transfer_type transfer on transfer.id = ntf.note_type_id
    where not ntf.is_deleted
      and field.code <> 'material_code'
), material_name_position as (
    select coalesce(max(base_position) filter (where code = 'material_name'), 0) as position
    from ranked_without_code
), desired_positions as (
    select ranked.id,
           ranked.base_position
             + case when ranked.base_position > name.position then 1 else 0 end as desired_position
    from ranked_without_code ranked
    cross join material_name_position name
    union all
    select ntf.id, name.position + 1
    from core.note_type_fields ntf
    join core.note_fields field on field.id = ntf.field_id
    join transfer_type transfer on transfer.id = ntf.note_type_id
    cross join material_name_position name
    where field.code = 'material_code' and not ntf.is_deleted
)
update core.note_type_fields mapping
set sort_order = desired.desired_position,
    updated_at = now()
from desired_positions desired
where mapping.id = desired.id
  and mapping.sort_order is distinct from desired.desired_position;
