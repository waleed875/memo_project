insert into auth.roles(code, name_ar) values
('ADMIN', 'مدير النظام'),
('DATA_ENTRY', 'مدخل بيانات'),
('REVIEWER', 'مراجع'),
('VIEWER', 'قارئ')
on conflict (code) do update set name_ar = excluded.name_ar;

insert into auth.users(username, password_hash, full_name, role_id)
select 'admin', 'BOOTSTRAP_BCRYPT_ADMIN123', 'مدير النظام', r.id
from auth.roles r
where r.code = 'ADMIN'
on conflict (username) do nothing;

insert into core.note_types(code, name_ar, description) values
('ENTRY', 'مذكرة إدخال', 'بنود مذكرة الإدخال'),
('EXIT', 'مذكرة إخراج', 'بنود مذكرة الإخراج'),
('DELIVERY', 'مذكرة تسليم', 'بنود مذكرة التسليم'),
('TRANSFER', 'مذكرة مناقلة', 'بنود مذكرة المناقلة')
on conflict (code) do update set name_ar = excluded.name_ar, description = excluded.description;

insert into core.note_fields(code, name_ar, default_sort_order) values
('factory', 'مصنع', 1),
('line', 'خط', 2),
('warehouse_code', 'رمز مستودع', 3),
('memo_time', 'الوقت', 4),
('memo_date', 'التاريخ', 5),
('memo_number', 'رقم المذكرة', 6),
('entry_note_label', 'مذكرة إدخال', 7),
('exit_note_label', 'مذكرة إخراج', 8),
('source_exit_memo_number', 'رقم مذكرة الإخراج من الجهة المسلمة', 9),
('receiving_entity', 'الجهة المستلمة', 10),
('material_name', 'اسم المادة', 11),
('material_code', 'رمز المادة', 12),
('quantity', 'الكمية', 13),
('unit', 'الوحدة', 14),
('deliverer_name_signature', 'اسم المسلم والتوقيع', 15),
('receiver_name_signature', 'اسم المستلم والتوقيع', 16),
('delivered_to_brother', 'تم تسليم الأخ', 17),
('entity', 'الجهة', 18),
('department', 'القسم', 19),
('product_name_code', 'اسم / رمز المنتج', 20),
('delivering_entity', 'الجهة المسلمة', 21)
on conflict (code) do update
set name_ar = excluded.name_ar,
    default_sort_order = excluded.default_sort_order;

with mappings(note_type_code, field_code, sort_order) as (
    values
    ('ENTRY','factory',1),('ENTRY','line',2),('ENTRY','warehouse_code',3),('ENTRY','memo_time',4),('ENTRY','memo_date',5),('ENTRY','memo_number',6),('ENTRY','entry_note_label',7),('ENTRY','source_exit_memo_number',8),('ENTRY','receiving_entity',9),('ENTRY','material_name',10),('ENTRY','material_code',11),('ENTRY','quantity',12),('ENTRY','unit',13),('ENTRY','deliverer_name_signature',14),('ENTRY','receiver_name_signature',15),
    ('EXIT','factory',1),('EXIT','line',2),('EXIT','warehouse_code',3),('EXIT','memo_time',4),('EXIT','memo_date',5),('EXIT','memo_number',6),('EXIT','exit_note_label',7),('EXIT','receiving_entity',8),('EXIT','material_name',9),('EXIT','material_code',10),('EXIT','quantity',11),('EXIT','unit',12),('EXIT','deliverer_name_signature',13),('EXIT','receiver_name_signature',14),
    ('DELIVERY','factory',1),('DELIVERY','line',2),('DELIVERY','warehouse_code',3),('DELIVERY','memo_time',4),('DELIVERY','memo_date',5),('DELIVERY','memo_number',6),('DELIVERY','delivered_to_brother',7),('DELIVERY','entity',8),('DELIVERY','department',9),('DELIVERY','product_name_code',10),('DELIVERY','quantity',11),('DELIVERY','unit',12),('DELIVERY','deliverer_name_signature',13),('DELIVERY','receiver_name_signature',14),
    ('TRANSFER','factory',1),('TRANSFER','line',2),('TRANSFER','warehouse_code',3),('TRANSFER','memo_time',4),('TRANSFER','memo_date',5),('TRANSFER','memo_number',6),('TRANSFER','delivering_entity',7),('TRANSFER','receiving_entity',8),('TRANSFER','material_name',9),('TRANSFER','quantity',10),('TRANSFER','unit',11),('TRANSFER','deliverer_name_signature',12),('TRANSFER','receiver_name_signature',13)
)
insert into core.note_type_fields(note_type_id, field_id, sort_order)
select nt.id, nf.id, m.sort_order
from mappings m
join core.note_types nt on nt.code = m.note_type_code
join core.note_fields nf on nf.code = m.field_code
on conflict (note_type_id, field_id) do update
set sort_order = excluded.sort_order,
    is_active = true,
    is_deleted = false;

insert into system.app_settings(key, value) values
('APP_MODE', 'SERVER'),
('ACTIVE_CONNECTION_PROFILE_ID', null),
('LAST_BACKUP_AT', null)
on conflict (key) do nothing;
