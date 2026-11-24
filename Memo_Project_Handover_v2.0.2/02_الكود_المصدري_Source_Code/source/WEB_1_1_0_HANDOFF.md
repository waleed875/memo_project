<div dir="rtl">

# خطة حزمة الويب 1.1.0 — ملف تسليم لمحادثة جديدة

> **كيف تستخدم هذا الملف:** الصقه كاملاً في بداية محادثة جديدة مع Claude، ثم نفّذ المراحل بالترتيب.
> قبل أي كود واجهات، حمّل سكيلز التصميم من القسم (8).
> مسار المشروع على الجهاز: `D:\Memo\Memo 1.0.7` — لا تعمل خارج هذا المجلد إلا للنسخ الاحتياطي.

## 0. القرارات المحسومة مسبقاً (لا تعيد سؤالها)

| البند | القرار |
|---|---|
| تقرير DevExpress الذي يتحول للويب | **التقرير التفصيلي للشركة `CompanyDetailedReport`** بنفس الفلاتر والبرامترات |
| شارتات تقرير الشركات المخصص | **3 فقط:** التطور الشهري + توزيع الحالات + التأخير والالتزام |
| تابات المركزية الجديدة | **4 تابات:** مدير عام + مدير برنامج + شركات + مراجعة (طابور التدقيق) |
| `install.sh` لأوبونتو | **مؤجل** — يُبنى فقط بعد تجهيز نسخة الويب واختبارها (مرحلة 6 مشروطة) |
| النسخ بين `Web/` القديم و`src/` | **ممنوع أي نسخ** — المقارنة للفهم فقط |

---

## 1. البرنامج الحالي (الإصدار 1.0.7)

### 1.1 ما هو؟
نظام داخلي لإدارة مذكرات التدقيق (عربي RTL بالكامل): إدخال، تدقيق واعتماد، تقارير أداء الشركات، ولوحة ويب.
مكوّنان: تطبيق مكتبي Windows + لوحة ويب (`ASP.NET Core 8 + React 18`).

### 1.2 مشاريع الحل `AuditMemoSystem.sln` (كلها `.NET 8`)

| المشروع | الدور | ملاحظة للمرحلة الجديدة |
|---|---|---|
| `AuditMemoSystem.App` | WinForms + DevExpress 24.1 — الإدخال والتدقيق والتقارير | مصدر تقرير `CompanyDetailed` الأصلي — لا تعدّله |
| `AuditMemoSystem.Core` | الدومين والعقود (`NoteModel`, `CompanyMetrics` غير موجودة هنا بل في الويب) | مرجع القراءة فقط |
| `AuditMemoSystem.Data` | مستودعات `Npgsql+Dapper` + `MigrationRunner` + نسخ | مصدر دوال التقارير SQL |
| `AuditMemoSystem.Reports` | `ReportFactory` (17 تقريراً) + `CompanyDetailedReport` + `DashboardSummaryReport` + تصدير Excel يدوي | **المرجع الذهبي** لمنطق التقرير المحوَّل |
| `MemoDashboard.Web/Backend` | خدمة اللوحة + التحديثات (`net8.0`) | ستتوسع بنقاط التقارير الجديدة |
| `MemoDashboard.Web/Frontend` | React 18 + Vite 5 + Tailwind 3 + Chart.js 4 + `i18n (ar/en)` | مسرح العمل الأساسي |
| `MemoUpdate*` (4 مشاريع) | منظومة التحديث الموقّع | خارج النطاق — لا تلمسها |

### 1.3 العقود الحاكمة (لا تكسرها — `DEC`)
- `DEC-01` أساس التاريخ صريح: كتابة (افتراضي) أو وصول + قطع وصول مستقل `entryCutoffDate`.
- `DEC-02` تصدير التكرار سطر لكل حادثة من `duplicate_incidents`.
- `DEC-03` عنوانان منفصلان للوحة والتحديث؛ `HTTP` داخلي بوضع `InternalLanHttp` فقط.
- أرقام المذكرات الجديدة أعداد صحيحة موجبة؛ السنة من تاريخ الكتابة.
- **لا تعديل ترحيل مطبق أبداً، ولا ترحيل إنتاج بلا نسخة `pg_dump -Fc` مجربة الاستعادة.**

### 1.4 حالة قاعدة البيانات
- الترحيلات `database/001 → 065`، الإنتاج على `065` / توافق `1.0.7`.
- توجد مسودة **`database/066_add_missing_fk_covering_indexes.sql` (DRAFT للمراجعة، غير مطبقة على أي قاعدة)** — 20 فهرس `IF NOT EXISTS` فقط، وموثق فيها أن `027` لا يحتاج إصلاحاً (لأن `035` طبّع الترتيب حتمياً).
- **تحذير:** مجرد وجود الملف يجعل تطبيقاً مكتبياً يُفتح على قاعدة `065` يعرض الترقية — لا تفتح التطبيق على الإنتاج قبل الاعتماد.
- دوال التقارير الحاكمة: `reporting.fn_dashboard_snapshot_v2`، `fn_company_metrics_snapshot_v4(_filtered)`، `fn_company_month_series_v4` (راجع `doc/DATABASE.md`).

### 1.5 نقاط النهاية الحالية (Backend — `src/MemoDashboard.Web/Backend`)
`GET /api/dashboard/snapshot` (فلاتر مفتوحة + `X-Correlation-ID`)، `GET .../company-metrics`، `GET .../lookups`، `GET /health`، `GET /api/admin/csrf`، `POST /api/admin/login|logout`، `POST /api/admin/updates/...`، `GET /api/updates/...`، `GET|POST /api/setup/...`.
عقد الفلاتر الموحد `FilterQuery` (`Models/DashboardDtos.cs:408`):
`From?, To?, DateBasis (WritingDate|ArrivalDate), CompanyId?, FactoryId?, ProductionLineId?, NoteTypeId?, Status?, EntryCutoffDate?`.

---

## 2. الداشبورد القديمة (`Web/MemoDashboard.Web` — إصدار 1.0.5، بتاريخ 11-08)

### 2.1 البنية (احتفظ بأفكارها — المستخدم يفضل تفاصيلها وتقسيماتها)
- `Backend` (26 ملفاً): `Program.cs` صغير (جلسة بسيطة + `HttpClient` لخدمة تحديث **خارجية**)، `DashboardQueryService` (901 سطر: `vw_..._v2` + `fn_dashboard_snapshot`، الفترة **إجبارية** بحد 366 يوم، بلا أساس مزدوج)، `SetupAccessService` (رمز إجباري + `loopback` فقط)، مصادقة بسيطة `username/password→bool`.
- `Frontend`: `DashboardRouter` **يبدّل فعلياً بين V1/V2/V3** (زر تدوير) — هذه ميزة محبوبة مفقودة في الحالية.
  - `dashboard/legacy/` (V1: بطاقات + رسوم + تنقل أقسام)، `dashboard/modern/` (V2: مركز تنفيذي)، `dashboard/v3/` (placeholder)، `dashboard/company/CompanyDashboard` (صغير).
  - `FilterBar` بسيط (`localStorage` فقط بلا تحقق)، `useDashboardData` باستطلاع كل 10 ثوانٍ، `SetupPage` تتضمن اختيار إصدار الواجهة + رمز إلزامي.
- `deploy/`: `publish.ps1` + `install-service.ps1` (بدائي) + `create_db_reader_user.sql` (يمنح `SELECT` واسعاً + **كلمة مرور داخل الملف** — عيب أمني مصلح في الجديدة).

### 2.2 ما يُؤخذ منها إلى التصميم الجديد
تقسيمات العرض الثلاثة (تنفيذي/حديث/شركات)، مبدّل الإصدارات كفكرة (يعود كتابات)، بساطة `FilterBar`، وعرض الشركة المنفصل.

## 3. الداشبورد الحالية (`src/MemoDashboard.Web` — إصدار 1.4.0، بتاريخ 08-09)

### 3.1 ما أُضيف (ابنِ عليه ولا تعده)
- عقد `Views` الثلاثي (`DashboardContractDtos + DashboardContractBuilder`): تنفيذي/عمليات/شركة.
- `UnifiedUpdateService` (التحديثات مدمجة) + 6 نقاط تحديث + CSRF إجباري + جلسة 8 ساعات + `mustChangePassword` + `audit_log`.
- `dateBasis/entryCutoffDate` من الـendpoint حتى SQL (`BuildSnapshotSql`) حتى الفلاتر.
- الفترة السابقة والمقارنة (`PreviousCompanyMetrics/Comparison`)، هويات صريحة (`CompanyId/FieldId/...`) بدل `Label`.
- صفحات: `CompanyReportPage (?report=company)` + `DashboardLive` (4 عروض) + `Preview` + خدمة `kpi.ts` (آليات غير مربوطة/موزون/مركب).
- `FilterBar` محقق (UUID/تاريخ + مزامنة URL + تنقية ضد lookups)، `DashboardSnapshotError` + `X-Correlation-ID`.
- `SetupPage`: بانر شبكة داخلية + `sslMode` + رمز اختياري + قارئ DB بأقل امتياز وبلا كلمة مخزنة.
- **كسر عقود مقصود:** `TopCompanies → Companies` وحقول الهوية — اختبر أي عميل قديم قبل الترقية.
- **تراجع مقصود:** حُذف مبدّل V1/V2/V3 وجمّد الـRouter على Legacy (ملفات Modern/V3 ميتة في الشجرة).

---

## 4. حزمة العمل 1.1.0 (نفّذ بالترتيب)

### المرحلة 1 — قسم التقارير + لوحة البيانات المركزية (4 تابات)
انقل تجربة الداشبورد إلى **قسم خاص** داخل الويب يحتوي قسم التقارير ومن ضمنها اللوحة المركزية.
تصميم اللوحة المركزية الجديد — 4 تابات بنفس الشريط العلوي الموحد (شعار + اتصال + تحديث + لغة + ثيم + ملء شاشة):

| التاب | الجمهور | المحتوى (مصدر البيانات) |
|---|---|---|
| `المدير العام` | الإدارة | ملخص الشركات العام: جدول الشركات (من `snapshot.Companies`) + أعلى/أدنى 5 جودة والتزام + شارت إجمالي شهري — قراءة فقط بلا فلاتر معقدة |
| `مدير البرنامج` | متابعة الوضع العام | مؤشرات البرنامج: معتمدة/بانتظار/معادة + التزام المدة + تأخير (متوسط/أقصى) + تكرار + أخطاء البنود العشرة — بفلاتر الفترة والأساس |
| `الشركات` | تحليل شركة | اختيار شركة + كل الفلاتر (`FilterQuery` كاملاً) → ملخص الشركة + تقريرها المخصص (المرحلة 3) |
| `المراجعة` | طابور التدقيق | مذكرات `NEEDS_REVIEW/UNDER_REVIEW` (نقطة جديدة `GET /api/dashboard/review-queue` تُبنى على `ReportService` في `Data`) + زر انتقال لتقرير الشركة |

### المرحلة 2 — تحويل `CompanyDetailedReport` إلى تقرير ويب (الأولوية القصوى)
التقرير موجود مكتبياً في `src/AuditMemoSystem.Reports/ReportDefinitions/CompanyDetailedReport.cs` (678 سطراً) — **لم يُعثر على تحويل سابق، ابنِه من الصفر هنا.**
نفس الفلاتر والبرامترات: كل حقول `FilterQuery` (§1.5) + حقول `ReportPresentationContext` (يبنيها المستخدم في صفحة التقرير):
`Issue{Title, LetterNumber, IssuerName, IssuedAt, Recipient, City, HijriDate, GregorianDate, FormEfficiencyIndex}` + `FilterSummary` (يُبنى تلقائياً من الفلاتر) + `Identity` (من إعدادات هوية التقرير) + اسم التطبيق.
خارطة المقاييس (من `CompanyMetrics` في الخلفية — نفس أسماء الحقول حرفياً):
`total_notes / clean_reviewed_notes / error_notes / incomplete_review_notes / all_not_applicable_note_count / quality_eligible_note_count / item_quality_percentage / review_coverage_percentage / time_compliance_percentage / affected_notes / affected_note_rate_percentage / duplicate_incidents / duplicate_free_percentage / company_resend_incidents / entry_notes / unlinked_entry_notes / link_efficiency_percentage / on_time_notes / late_notes (+late_6_to_10/11_to_20/over_20) / average_late_days / maximum_late_days` + المؤشرات المحسوبة في الواجهة (`FormEfficiency / MechanismUnlinked / WeightedTimeliness / OverallComposite` — الصيغ في `CompanyDetailedReport.cs:573-606` ومرآتها في `Frontend/src/services/kpi.ts`).
التخطيط (RTL كامل): ترويسة مؤسسية (عربي يمين/إنجليزي يسار/شعار وسط — نفس `ReportFactory`) + شبكة بطاقات + الرسوم الثلاثة (المرحلة 3) + جدولا البنود والمصانع + الملخص التنفيذي + تذييل (رقم كتاب/محرر/تاريخ) + زر طباعة (`@media print landscape`) وتصدير Excel عبر نقطة موجودة.

### المرحلة 3 — تقرير الشركات المخصص (3 شارتات فقط)
داخل تاب الشركات: `التطور الشهري` (خط: صحة البنود + أعمدة الحجم — مصدر `monthlyActivity`) + `توزيع الحالات` (دونات: سليمة/أخطاء/غير مكتملة/غير منطبقة — مصدر `statusDistribution`) + `التأخير والالتزام` (أعمدة: ضمن المدة/6-10/11-20/+20 — مصدر حقول `Late*`).
قواعد Chart.js الإلزامية (دروس مستفادة): `legend.rtl:true + tooltip.rtl:true + scales.x.reverse:true` والحاوية `dir="rtl"` — لا `dir="ltr"` حول الرسوم أبداً.

### المرحلة 4 — نظام التصميم الجديد (روح جديدة + الأفكار الأساسية)
- **الثوابت المحفوظة (لا تغيّرها):** ألوان `index.css` (`--brand-gold:#b9a77a`, `--brand-brown:#5c4927`, خلفية `#f8f5eb`, وضع داكن موجود) + خط `ITF Qomra Arabic` + صفحة الدخول + زر ملء الشاشة + إعدادات الاتصال (`SetupPage` + `sslMode` + الرمز) + زر العودة للواجهة الرئيسية للويب في كل صفحة (`backToCompanyHref` نمطاً).
- **الجديد:** دمج روح القديمة (تقسيمات V1/V2 + البساطة) مع مكونات الحالية؛ تابات بدل مبدّل الإصدار؛ `Header` موحد.
- **قواعد RTL الصارمة (موثقة من إصلاحات سابقة):** محاذاة العربي `right` دائماً (لا `left` داخل `rtl`)؛ ممنوع `letter-spacing` سالب على العربي (`:lang(ar){letter-spacing:0}`)؛ `dir` من اللغة لا `auto` للعناوين الرسمية؛ أرقام لاتينية بـ `tabular-nums`.

### المرحلة 5 — إغلاق الباكج 1.1.0
1. `Directory.Build.props`: `ProductVersion 1.1.0` (+ `InformationalVersion`).
2. قاعدة البيانات: اعتماد مسودة `066` أو تركها (قرار المالك — راجع تحذير §1.4) + تحديث `compatible_application_version` بترحيل وصفي جديد إن لزم + تحديث `doc/` (الأرقام `065→066`، سطر `CHANGELOG`).
3. `Frontend/package.json + version.ts`: `1.5.0` للواجهة (استمرار تسلسل `1.4.0`).
4. الاختبارات الخضراء شرط الإغلاق: `dotnet test` (الخلفي `89/89`، التقارير `13/13`، الأساسي غير التكاملي) + `vitest` للواجهة + بناء `Release` كامل.
5. تحديث `doc/DEPLOYMENT.md` بأي endpoint/إعداد جديد.

### المرحلة 6 — `install.sh` لأوبونتو (مشروطة بانتهاء الاختبار)
توسعة `scripts/Install-MemoDashboard.sh` الموجود (لا سكربت جديد): حزمة `MemoDashboard-1.1.0-linux-x64.zip`، متغيرات `--port --bind-address --allowed-hosts` نفسها، مستخدم خدمة + `systemd` + أسرار خارجية عبر `Provision-MemoDashboardSecurity.sh`، فحص `/health=200`، وتراجع تلقائي عند الفشل. ثم اختبار تثبيت نظيف على `Ubuntu 24.04`.

---

## 5. معايير القبول لكل مرحلة
- المرحلة 1: التابات الأربع تعمل ببيانات حية + تنقل عائد للرئيسية + لا تمرير أفقي على `360px`.
- المرحلة 2: تطابق رقمي سطر بسطر مع `PDF` المكتبي لنفس الفلاتر (7 مذكرات الذهب + عينة إنتاج).
- المرحلة 3: الشارتات الثلاثة تطابق أرقام البطاقات + طباعة نظيفة.
- المرحلة 4: `light/dark` + `ar/en` + `print` بلا انكسار + مراجعة لقطات شاشة.
- المرحلة 5–6: بناء أخضر + تثبيت نظيف ناجح.

## 6. أوامر التحقق المحفوظة
```powershell
dotnet build .\AuditMemoSystem.sln -c Release
dotnet test tests\MemoDashboard.Web\Backend.Tests -c Release
dotnet test tests\AuditMemoSystem.Reports.Tests -c Release
# الواجهة: npm.cmd run build داخل src\MemoDashboard.Web\Frontend
```

---

## 7. الأسئلة المتبقية للمحادثة الجديدة (اسأل المالك قبل التنفيذ)
1. نقطة `review-queue` غير موجودة خلفياً — هل تُبنى على `core.notes` مباشرة أم تُشتق من `ReportService.GetReportTable("PendingReview")`؟
2. هل تقرير الويب يحتاج تصدير `PDF` خادمياً أم تكفي طباعة المتصفح + `Excel`؟
3. أسماء التابات العربية النهائية ونطاق رؤية كل دور (هل تُخفى تابات حسب الصلاحية)؟
4. هل تُحذف ملفات `Modern/V3` الميتة أم تُبقى؟
5. هل تُعتمد `066` ضمن `1.1.0` (مع ترقية الذهب/الاختبار) أم تُؤجل؟

---

## 8. سكيلز التصميم المطلوب تحميلها أولاً (موثقة 2026)
1. **البلجن الرسمي `frontend-design`** — يتفعّل تلقائياً ويمنع «AI slop» (خطوط عامة/تدرجات بنفسجية): `https://claude.com/plugins/frontend-design` + طباخ الجماليات: `https://platform.claude.com/cookbook/coding-prompting-for-frontend-aesthetics`
2. **`ui-ux-pro-max-skill`** (مطابقة نمط الداشبورد تلقائياً): `claude plugin add nextlevelbuilder/ui-ux-pro-max-skill` — المصدر: `https://github.com/nextlevelbuilder/ui-ux-pro-max-skill`
3. **`rtl-skill`** (عربي أولاً، لا مجرد قلب اتجاه): `https://github.com/Maryeus/rtl-skill` والتوثيق `https://samuadda.github.io/rtl-skill/` + سجل السكيلز: `https://github.com/majiayu000/claude-skill-registry/blob/main/skills/design/rtl-ui/SKILL.md`
4. **تعريب RTL آلي:** `Arabic RTL Localization skill` — `https://mcpmarket.com/tools/skills/arabic-rtl-localization`
5. **حلقة التحقق البصري:** `Playwright MCP` (`npx @playwright/mcp@latest`) للقطات + `Chrome DevTools MCP` (`npx @anthropic-ai/chrome-devtools-mcp@latest`) — صمم ← صوّر ← حسّن.
6. مرجع الأدوات الشامل: `https://github.com/wilwaldon/Claude-Code-Frontend-Design-Toolkit`

</div>
