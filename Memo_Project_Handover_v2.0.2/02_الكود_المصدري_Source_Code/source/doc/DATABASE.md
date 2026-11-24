<div dir="rtl">

# قاعدة البيانات

- PostgreSQL (الإنتاج محلي `127.0.0.1:5432`، قاعدة `audit_memo`).
- الترحيلات `database/001` → `065` تسلسلية عبر `MigrationRunner`
  (قفل استشاري + بصمات + كشف تعديل ترحيل مطبق). آخر ترحيل بيانات `064`،
  و`065` بيانات وصفية (توافق `1.0.7`) فقط.
- الجداول المحورية: `core.notes` (+`note_number_value` الرقمية و`note_year`)،
  `core.duplicate_incidents` (+أرشيف)، `core.note_links`، `core.note_sources`
  (+`exit_refs`/`invoices`/`loot`)، `core.note_field_values`، `audit.logs`،
  `auth.*`، `system.*`.
- دوال التقارير: `reporting.fn_dashboard_snapshot_v2(from,to,…,date_basis,cutoff)`،
  `fn_company_metrics_snapshot_v4(_filtered)`، `fn_company_month_series_v4`.
- **1.1.0**: لا ترحيل جديد — البقاء على `065`. كل استعلامات الويب الجديدة
  (طابور المراجعة، الأشهر، التقرير التفصيلي) قراءة فقط على كائنات `065`
  القائمة. مسودة `066` (فهارس) مؤجلة بقرار المالك وغير مطبقة على أي قاعدة.

## سياسة النسخ (إلزامية للإنتاج)

1. نسخة `pg_dump -Fc` قبل أي ترقية + التحقق بـ`pg_restore --list`.
2. اختبار استعادة على قاعدة تجريبية أولًا.
3. الترقية من تطبيق 1.0.7 بحساب ADMIN (تقترح نسخة تلقائيًا).
4. **ممنوع** حفظ النسخ تحت `bin/` أو `obj/` — تُمسح مع التنظيف! المسار
   الموصى: مجلد نسخ خارج الشجرة (مثال `D:\Memo\Backups`).

</div>
