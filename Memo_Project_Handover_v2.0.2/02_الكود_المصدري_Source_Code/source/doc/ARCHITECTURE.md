<div dir="rtl">

# البنية والمكونات

## المكونات

| المكون | التقنية | الدور |
|---|---|---|
| `AuditMemoSystem.App` | WinForms + DevExpress 24.1 (`net8.0-windows`) | الإدخال، التدقيق، التقارير، الإعدادات، الترقية |
| `Core` | .NET 8 | النماذج، التحقق، `ReportDateBasis`، `BusinessDateScope` |
| `Data` | Npgsql + Dapper | المستودعات، `MigrationRunner`، النسخ الاحتياطي |
| `Reports` | DevExpress XtraReports | `CompanyDetailedReport` والتقارير المطبوعة |
| `MemoDashboard.Web/Backend` | ASP.NET Core 8 | لقطة اللوحة، مقاييس الشركات، التحديث، الإعداد |
| `MemoDashboard.Web/Frontend` | React 18 + Chart.js + Tailwind | اللوحات + صفحة تقرير الشركة (`?report=company`) |
| `MemoUpdater/Service/Center` | .NET 8 | تنزيل حزم التحديث الموقعة وتثبيتها |

## التدفقات الأساسية

1. **دورة المذكرة**: مسودة (إدخال) ← إرسال ← تدقيق (بدء/اعتماد/إعادة) ← أرشفة.
   تعديل المدير يحفظ الحالة وحقول المسار (`preserve_workflow_state`) مع تدقيق فرق.
   التزامن متفائل عبر `updated_at`.
2. **التكرار**: `FindDuplicateNotesAsync` بالمفتاح (شركة + نوع + سنة الكتابة + رقم)،
   والقفل الاستشاري يشمل السنة. التصنيفات: `COMPANY_RESEND` (حادثة بلا حفظ)،
   `COMPANY_DUPLICATE_NUMBER` (حفظ + حادثة)، `DATA_ENTRY_DUPLICATE_ATTEMPT` (تدقيق فقط).
3. **المصادر (L6)**: مذكرة الإدخال لها نوع مصدر واحد: مراجع إخراج متعددة، أو
   فواتير متعددة بمرفقات، أو غنائم، أو وصف حر. التوافق القديم عبر
   `core.v_note_links_compat`.
4. **التقارير**: `ReportService` (مكتبي) و`DashboardQueryService` (ويب) يقرآن نفس
   العقود (`fn_dashboard_snapshot_v2`، `fn_company_metrics_snapshot_v4`).
5. **التحديث**: manifest موقّع (RSA) + فحص SHA-256 والحجم قبل التثبيت؛ عنوان
   خدمة التحديث مستقل عن عنوان اللوحة.

</div>
