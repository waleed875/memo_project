<div dir="rtl">

# نظام مذكرات التدقيق — الإصدار 1.0.7

## ما هذا المشروع؟

نظام داخلي لإدارة مذكرات التدقيق: إدخال، تدقيق واعتماد، تقارير أداء الشركات،
ولوحة ويب. مكوّنان: تطبيق مكتبي (Windows) ولوحة ويب (ASP.NET Core + React).

## الحالة المعتمدة (2026-09-06)

| البند | القيمة |
|---|---|
| إصدار البرنامج | `1.0.7` (`Directory.Build.props`) |
| قاعدة الإنتاج `audit_memo` | ترحيل `065` / توافق `1.0.7` |
| قاعدة الاختبار `audit_memo_l6_test_20260831` | ترحيل `064` |
| قاعدة الذهب `audit_memo_integration_golden_20260816` | ترحيل `064`، 7 مذكرات |
| الإنتاج: مذكرات/نشطة/حوادث/روابط | 2956 / 2953 / 800 / 693 |

## بنية الشجرة

```text
1.0.7/
  AuditMemoSystem.sln
  Directory.Build.props      ← رقم الإصدار (1.0.7)
  global.json                ← يتطلب .NET SDK 9
  database/                  ← ترحيلات 001 إلى 065 (لا تُعدّل المطبق منها أبدًا)
  docs/                      ← runbook القاعدة + التزويد الأمني
  doc/                       ← هذا التوثيق (واقع المشروع)
  scripts/                   ← تثبيت اللوحة (Windows/Ubuntu) + التزويد
  src/
    AuditMemoSystem.App      ← تطبيق سطح المكتب (WinForms + DevExpress)
    AuditMemoSystem.Core     ← المجال والتحقق والعقود
    AuditMemoSystem.Data     ← المستودعات + MigrationRunner
    AuditMemoSystem.Reports  ← تعاريف تقارير DevExpress
    MemoDashboard.Web/Backend← خدمة اللوحة والتحديث (net8.0)
    MemoDashboard.Web/Frontend ← واجهة React
    MemoUpdater/Service/Center/Contracts ← منظومة التحديث
  tests/                     ← وحدات + تكامل (Core/Reports/Backend/Frontend)
```

## البناء والتشغيل

- المتطلبات: .NET SDK 9، Node 22 على `PATH` (ضروري لبناء الواجهة).
- بناء كامل: `dotnet build .\AuditMemoSystem.sln -c Release` من الجذر.
- المكتبي: `src\AuditMemoSystem.App\bin\Release\net8.0-windows\AuditMemoSystem.App.exe`
  (أول دخول بحساب ADMIN؛ الترقية تقترح نسخة احتياطية ثم تطبق الترحيلات).
- اللوحة: `dotnet run --project src\MemoDashboard.Web\Backend` ثم `/setup`
  من نفس الجهاز (الإعداد الأولي مفتوح لـ`localhost` دون رموز)، وبعد الحفظ
  يلزم دخول مدير. التفاصيل: `doc/DEPLOYMENT.md`.

## العقود الحاكمة (لا تكسرها)

- `DEC-01` أساس التاريخ صريح: كتابة (افتراضي) أو وصول + قطع وصول مستقل.
- `DEC-02` تصدير التكرار سطر لكل حادثة من `duplicate_incidents`.
- `DEC-03` عناوين منفصلة للوحة والتحديث؛ HTTP داخلي بوضع `InternalLanHttp` فقط.
- أرقام المذكرات الجديدة أعداد صحيحة موجبة؛ السنة من تاريخ الكتابة.
- لا تعديل ترحيل مطبق، ولا ترحيل إنتاج بلا نسخة مجربة الاستعادة.

</div>
