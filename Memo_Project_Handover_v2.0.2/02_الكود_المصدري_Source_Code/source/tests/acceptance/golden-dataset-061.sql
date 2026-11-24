-- Deterministic owner-acceptance data for database version 061.
-- This file is test data only. It is never applied to production or shipped in export.

insert into core.companies(id, name, is_active, is_deleted)
values
    ('00000000-0000-0000-0000-000000000001', 'شركة الأمل', true, false),
    ('00000000-0000-0000-0000-000000000002', 'شركة النور', true, false),
    ('00000000-0000-0000-0000-000000000003', 'شركة بلا مذكرات', true, false),
    ('00000000-0000-0000-0000-000000000004', 'شركة السهل', true, false),
    ('00000000-0000-0000-0000-000000000005', 'شركة الرافدين', true, false),
    ('00000000-0000-0000-0000-000000000006', 'شركة الشام', true, false),
    ('00000000-0000-0000-0000-000000000007', 'شركة البناء', true, false),
    ('00000000-0000-0000-0000-000000000008', 'شركة المنارة', true, false);

insert into core.factories(id, name, company_id, is_active, is_deleted)
values
    ('00000000-0000-0000-0000-000000000101', 'مصنع الأمل الأول', '00000000-0000-0000-0000-000000000001', true, false),
    ('00000000-0000-0000-0000-000000000102', 'مصنع النور الأول', '00000000-0000-0000-0000-000000000002', true, false);

insert into core.production_lines(id, factory_id, name, is_active, is_deleted)
values
    ('00000000-0000-0000-0000-000000000201', '00000000-0000-0000-0000-000000000101', 'خط التعبئة 1', true, false);

insert into core.notes(
    id, note_number, note_number_normalized, note_type_id, company_id, factory_id, production_line_id,
    writing_date, arrival_at, status, created_at, updated_at, review_started_at, approved_at)
values
    ('00000000-0000-0000-0000-000000001001', 'N-0001', 'N0001', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', '2026-07-22', '2026-07-31', 'COMPLETE', '2026-07-20 08:00:00', '2026-07-31 08:00:00', '2026-07-20 08:00:00', null),
    ('00000000-0000-0000-0000-000000001002', 'N-0002', 'N0002', (select id from core.note_types where code = 'EXIT'), '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', '2026-07-24', '2026-08-01', 'APPROVED', '2026-07-24 09:00:00', '2026-08-02 09:00:00', null, '2026-08-02 09:00:00'),
    ('00000000-0000-0000-0000-000000001003', 'N-0004', 'N0004', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000102', null, null, '2026-08-03', 'DRAFT', '2026-08-03 08:00:00', '2026-08-03 08:00:00', null, null),
    ('00000000-0000-0000-0000-000000001004', 'N-0005', 'N0005', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', '2026-08-01', '2026-08-05', 'APPROVED', '2026-08-01 08:00:00', '2026-08-06 09:00:00', null, '2026-08-06 09:00:00'),
    ('00000000-0000-0000-0000-000000001005', 'N-0006', 'N0006', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000102', null, '2026-08-03', '2026-08-07', 'UNDER_REVIEW', '2026-07-31 08:00:00', '2026-08-07 08:00:00', '2026-07-31 08:00:00', null),
    ('00000000-0000-0000-0000-000000001006', 'N-0007', 'N0007', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000102', null, '2026-08-05', '2026-08-08', 'NEEDS_REVIEW', '2026-07-27 08:00:00', '2026-08-08 08:00:00', '2026-07-27 08:00:00', null),
    ('00000000-0000-0000-0000-000000001007', 'N-0003', 'N0003', (select id from core.note_types where code = 'ENTRY'), '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', '2026-08-09', '2026-08-10', 'RETURNED_FOR_CORRECTION', '2026-08-09 08:00:00', '2026-08-10 08:00:00', null, null);

-- All ENTRY fields default to correct. Overrides below exercise all seven states.
with entry_notes(note_id) as (
    values
        ('00000000-0000-0000-0000-000000001001'::uuid),
        ('00000000-0000-0000-0000-000000001003'::uuid),
        ('00000000-0000-0000-0000-000000001004'::uuid),
        ('00000000-0000-0000-0000-000000001005'::uuid),
        ('00000000-0000-0000-0000-000000001006'::uuid),
        ('00000000-0000-0000-0000-000000001007'::uuid)
), defaults as (
    select e.note_id, ntf.field_id, 1::smallint as field_value, null::text as inactive_reason
    from entry_notes e
    join core.notes n on n.id = e.note_id
    join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
), overrides(note_id, field_code, field_value, inactive_reason) as (
    values
        ('00000000-0000-0000-0000-000000001001'::uuid, 'factory', 2::smallint, 'اختبار خطأ مصنع'),
        ('00000000-0000-0000-0000-000000001001'::uuid, 'line', 3::smallint, 'اختبار بند مفقود'),
        ('00000000-0000-0000-0000-000000001001'::uuid, 'warehouse_code', 4::smallint, null::text),
        ('00000000-0000-0000-0000-000000001001'::uuid, 'memo_time', 5::smallint, null::text),
        ('00000000-0000-0000-0000-000000001003'::uuid, 'factory', 0::smallint, null::text),
        ('00000000-0000-0000-0000-000000001007'::uuid, 'memo_date', 6::smallint, null::text),
        ('00000000-0000-0000-0000-000000001006'::uuid, 'memo_number', 4::smallint, null::text)
)
insert into core.note_field_values(note_id, field_id, field_value, inactive_reason)
select d.note_id, d.field_id, coalesce(o.field_value, d.field_value), o.inactive_reason
from defaults d
join core.note_fields nf on nf.id = d.field_id
left join overrides o on o.note_id = d.note_id and o.field_code = nf.code;

with exit_note(note_id) as (values ('00000000-0000-0000-0000-000000001002'::uuid))
insert into core.note_field_values(note_id, field_id, field_value, inactive_reason)
select e.note_id, ntf.field_id, 1, null
from exit_note e
join core.notes n on n.id = e.note_id
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted;

insert into core.note_links(id, source_note_id, related_note_id)
values ('00000000-0000-0000-0000-000000000301', '00000000-0000-0000-0000-000000001001', '00000000-0000-0000-0000-000000001002');

insert into core.duplicate_incidents(
    id, classification, matched_note_id, created_note_id, company_id, factory_id, production_line_id,
    note_type_id, note_number, note_number_normalized, writing_date, metadata)
values
    ('00000000-0000-0000-0000-000000000401', 'COMPANY_RESEND', '00000000-0000-0000-0000-000000001001', null, '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', (select id from core.note_types where code = 'ENTRY'), 'N-0001', 'N0001', '2026-07-22', '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000402', 'COMPANY_DUPLICATE_NUMBER', '00000000-0000-0000-0000-000000001002', '00000000-0000-0000-0000-000000001001', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000201', (select id from core.note_types where code = 'ENTRY'), 'DUP-JUL', 'DUPJUL', '2026-07-24', '{}'::jsonb),
    ('00000000-0000-0000-0000-000000000403', 'COMPANY_DUPLICATE_NUMBER', '00000000-0000-0000-0000-000000001004', '00000000-0000-0000-0000-000000001007', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000101', null, (select id from core.note_types where code = 'ENTRY'), 'DUP-AUG', 'DUPAUG', '2026-08-09', '{}'::jsonb);
