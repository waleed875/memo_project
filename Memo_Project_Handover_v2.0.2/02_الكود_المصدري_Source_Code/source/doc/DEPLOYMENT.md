<div dir="rtl">

# التشغيل والنشر (1.1.0)

## تطبيق سطح المكتب (Windows)

- المتطلب: لا شيء إضافي (النسخة مكتفية ذاتيًا) — فك الضغط وشغّل
  `AuditMemoSystem.App.exe`.
- أول دخول ADMIN → إن لزم ترقية: خذ النسخة المقترحة → طبّق → تحقق.
- الاتصال: مضيف/منفذ/قاعدة/مستخدم/كلمة مرور من شاشة الدخول.

## لوحة الويب (Ubuntu 24.04)

```bash
sudo apt-get install -y unzip
unzip MemoDashboard-1.1.0-linux-x64.zip -d memo-dashboard-1.1.0
cd memo-dashboard-1.1.0
sudo ./install.sh --port 8080 --bind-address 192.168.1.20
```

- `install.sh` يجهز النظام الفارغ بنفسه: `curl/unzip/ca-certificates/openssl`
  ومكتبات `ICU`، ثم **Chromium** (لتصدير PDF الخادمي — `chromium-browser`،
  وبدونه تعمل اللوحة طبيعيًا وتجيب نقطة PDF بـ`503` مع التوجيه)، ثم مستخدم
  خدمة + وحدة systemd + إعدادات.
- متغير البيئة `MEMO_CHROMIUM_PATH` يتجاوز اكتشاف المتصفح (يُسجل تلقائيًا في
  ملف البيئة عند التثبيت، ولا يُستبدل عند الترقية).
- بعده افتح `http://SERVER:8080/setup` **من الخادم نفسه** (`localhost` مفتوح
  بلا رموز) وأدخل اتصال PostgreSQL.
- رقِّ القاعدة من تطبيق المكتبي أولًا (اللوحة لا تنفذ migrations).
  **1.1.0 لا تتطلب ترحيلًا جديدًا**: البقاء على `065` (توافق `1.0.7` صالح)،
  ومسودة `066` مؤجلة بقرار المالك — لا تطبقها.
- التحقق: `/health` يعيد 200. الجدار: افتح المنفذ لأضيق نطاق داخلي فقط.
- التشغيل اليدوي (تطوير): `dotnet run --project src/MemoDashboard.Web/Backend`
  (يحتاج Node 22 على `PATH` لبناء الواجهة، أو `npm ci` داخل `Frontend`،
  ومتصفح Chrome/Edge لتصدير PDF محليًا).

## نقاط النهاية الجديدة (1.1.0)

كلها خلف جلسة المدير + حد `dashboard` (`120/دقيقة`) وتعيد `X-Correlation-ID`:

| النقطة | الغرض |
|---|---|
| `GET /api/dashboard/review-queue` | طابور التدقيق (`NEEDS_REVIEW/UNDER_REVIEW` من `core.notes`) + `limit (1..200)` و`offset` |
| `GET /api/dashboard/company-monthly?companyId&from&to` | سلسلة الأشهر الستة (الحجم + صحة البنود) لشارت التطور |
| `GET /api/dashboard/company-report.html` | التقرير التفصيلي كـHTML مكتفٍ (معاينة/تدقيق) |
| `GET /api/dashboard/company-report.pdf` | التقرير كـPDF أفقي A4 عبر Chromium (يحتاج متصفحًا وإلا `503`) |
| `GET /api/dashboard/company-report.xlsx` | التقرير كمصنف Excel بورقة RTL واحدة |

- نطاق تقرير الشركة (PDF/HTML/Excel): الشركة + الفترة (`from/to` إجبارية)
  + `dateBasis` + `entryCutoffDate` فقط — أي مرشح إضافي (مصنع/خط/نوع/حالة)
  يُرفض `400` ليطابق نطاق `CompanyDetailed` المكتبي (V3) رقميًا.
- قسم التقارير واللوحة المركزية في الواجهة: `?view=central` بأربع تابات
  (`centralTab=general|program|companies|review`) — ظاهرة للجميع بقرار المالك.

## استكشاف سريع

| العرض | السبب والحل |
|---|---|
| `npm.cmd --version` خرج `9009` | Node ليس على `PATH` — ثبّت Node 22 LTS أو أضف مجلده |
| `tsc`/`vite` مفقودة بعد `npm ci` | لا تحذف مجلدات `bin` داخل `node_modules` (كانت سبب حادثة سابقة!) |
| `401` في الإعداد | محفوظ مسبقًا ← سجل دخول مدير؛ أو افتح من `localhost` |
| تقرير الويب لا يظهر | نسخة باك اند قديمة شغالة ← أوقف، أعد البناء، أعد التشغيل |
| `Blocked host` | أضف المضيف لـ`--allowed-hosts` |
| PDF يجيب `503 chromium_unavailable` | ثبّت Chromium (`apt-get install -y chromium-browser`) أو اضبط `MEMO_CHROMIUM_PATH` ثم أعد تشغيل الخدمة |
| `400 company_report_invalid` على PDF | الفترة (`from/to`) إجبارية والمرشحات الإضافية مرفوضة — راجع النطاق أعلاه |

</div>
