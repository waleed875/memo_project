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
from core.notes n join core.note_types nt on nt.id=n.note_type_id join core.companies c on c.id=n.company_id
join core.note_type_fields ntf on ntf.note_type_id=n.note_type_id and ntf.is_active and not ntf.is_deleted
left join core.note_field_values nfv on nfv.note_id=n.id and nfv.field_id=ntf.field_id
where not n.is_deleted group by n.id,nt.id,nt.name_ar,c.id,c.name;
