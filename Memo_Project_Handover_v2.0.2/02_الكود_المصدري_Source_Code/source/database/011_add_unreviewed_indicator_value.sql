alter table core.note_field_values
    drop constraint if exists ck_note_field_values_value_reason,
    add constraint ck_note_field_values_value_reason check (
        field_value in (0, 1, 2, 3)
        and (
            field_value <> 0
            or inactive_reason is not null and length(trim(inactive_reason)) > 0
        )
    );

create or replace view reporting.vw_note_field_details as
select
    n.id as note_id,
    n.note_number,
    c.id as company_id,
    c.name as company_name,
    nt.id as note_type_id,
    nt.name_ar as note_type_name,
    nf.id as field_id,
    nf.name_ar as field_name,
    nfv.field_value,
    case nfv.field_value
        when 1 then 'موجود'
        when 0 then 'غير موجود'
        when 2 then 'مجهول'
        when 3 then 'لم تتم المراجعة'
        else 'غير مدخل'
    end as field_value_label_ar,
    nfv.inactive_reason,
    n.writing_date,
    n.arrival_at,
    n.status
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where not n.is_deleted;
