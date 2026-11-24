# أمان provisioning وخدمة التحديث — 1.0.7

## الأمر الرسمي

على Windows شغّل الأمر من حساب إداري على الخادم:

\`powershell -ExecutionPolicy Bypass -File .\scripts\Provision-MemoDashboardSecurity.ps1 -ServiceAccount "NT SERVICE\MemoDashboard" -CopySetupToken\`

وعلى Ubuntu:

\`sudo ./scripts/Provision-MemoDashboardSecurity.sh --service-user memo-dashboard\`

يجب أن يكون \`SecretDirectory\` خارج مجلد الإصدار. ينشئ الأمر القيم الناقصة فقط، ويرفض تدوير زوج RSA الموجود. إعادة إنشاء رمز first-run لا تتم إلا بخيار صريح \`-RegenerateSetupToken\` أو \`--regenerate-setup-token\`. يعرض الأمر رمز first-run عند إنشائه فقط؛ لا يطبع المفتاح الخاص ولا legacy admin key ولا check-in key.

## ملفات الأسرار

يُربط حساب خدمة Backend بملفات خارج release ZIP:

- \`MEMO_DASHBOARD_SETUP_TOKEN_FILE\`
- \`MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE\`
- \`MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE\`
- \`MEMO_UPDATE_ADMIN_KEY_FILE\`
- \`MEMO_UPDATE_CHECKIN_KEY_FILE\` عند تفعيل check-in

يبقى المفتاح الخاص على الخادم، ويُستخدم المفتاح العام فقط للتحقق عند العملاء. أي ملف public key لا يطابق private key يمنع النشر ويظهر كحالة \`mismatch\` آمنة. لا توجد قيم أسرار في \`appsettings.json\` أو الواجهة أو سجل التشغيل أو حزمة الإصدار.

## first-run

الإعداد الأولي من نفس الجهاز (`localhost`) مفتوح دائمًا دون أي رمز: من يصل عبر
loopback يملك الجهاز أصلًا. من خارج الجهاز يبقى الإعداد مغلقًا إلا لعناوين
الشبكة الخاصة عند تفعيل `MEMO_DASHBOARD_INTERNAL_SETUP=1`. بعد نجاح حفظ اتصال
قاعدة البيانات يُستهلك رمز first-run (إن وُجد ملفه) ويُحذف. بعد اكتمال الإعداد
لا تُفتح شاشات التهيئة إلا لجلسة مدير مسجلة.

## تهيئة العملاء

يقدم Backend الموحد:

\`GET /api/updates/public-key\`

هذا المسار يعيد public key فقط إذا كان private key صالحًا ومتطابقًا مع public key المحفوظ، ولا ينشئ أسرارًا ولا يعيد private key. شاشة ADMIN في سطح المكتب تتحقق من رابط HTTPS، تجلب المفتاح العام، ثم تحفظ \`UPDATE_SERVICE_URL\` و\`UPDATE_PUBLIC_KEY\` عبر \`SystemSettingsService\` داخل \`system.app_settings\`. لا حاجة إلى SQL يدوي، ولا تسمح الشاشة بلصق private key.

يبقى مسار MemoUpdateCenter القديم مقيدًا بـ legacy admin key أثناء فترة التوافق، ولا يتغير URL القديم أو يعتمد على redirect. لا يُدار زوج RSA جديد أثناء cutover الأول.

## metadata متعددة artifacts

عند وصول artifact ثانٍ للإصدار نفسه، يقفل Backend سجل الإصدار ويقارن:

- \`notes\`
- \`minimumVersion\`
- \`minimumDatabaseVersion\`
- \`targetDatabaseVersion\`

المطابقة تسمح بإضافة artifact للمنصة الأخرى. الاختلاف يعيد \`409 Conflict\` مع أسماء الحقول المختلفة قبل نقل الملف، ولا يغير metadata لإصدار \`PUBLISHED\` ضمنيًا.

## تحقق 1.0.7

- اختبارات Backend: 88/88 (على قاعدة الذهب `audit_memo_integration_golden_20260816` عند migration `064`).
- اختبارات Core: 197/197 (منها 178 غير تكاملية و19 تكاملية على قواعد معزولة).
- اختبارات Reports: 12/12.
- اختبارات Frontend: 59/59.
- smoke test لأمر PowerShell provisioning: ناجح في مجلد مؤقت مع تنظيفه.
- ترحيل التوافق `065` يرفع `compatible_application_version` إلى `1.0.7` دون تغيير المخطط.
## جلسة Web Admin وكلمة المرور

- جلسة الإدارة تعتمد على `UserId` واسم المستخدم مع ختم أمان مشتق من `auth.users.updated_at` ووقت دخول مستقل.
- كل طلب API يمر عبر تحقق موحد؛ تُراجع حالة الحساب والدور `ADMIN` و`is_active` و`is_deleted` و`must_change_password`، وتُلغى الجلسة عند اختلاف الختم.
- مدة الخمول 30 دقيقة، والمدة المطلقة 8 ساعات. سياسة إعادة التحقق القصيرة 15 ثانية موثقة لتفادي استعلام قاعدة بيانات مع كل polling.
- مستخدم `must_change_password` لا يحصل على جلسة Dashboard أو Setup أو نشر؛ الرسالة توجهه إلى تطبيق Desktop.
- كوكي الجلسة `HttpOnly` و`SameSite=Strict`، و`Secure` في الإنتاج؛ كل طلب POST حساس يحتاج `X-CSRF-TOKEN` صالحًا.
- تُسجل أحداث الدخول والخروج وإبطال الجلسة وتغيير كلمة المرور دون كلمة المرور أو الهاش.

## TASK-06 — اتصال PostgreSQL الداخلي وفصل الحسابات

يُعامل اتصال قاعدة البيانات كسياسة نشر مستقلة عن حزمة التطبيق:

| الوظيفة | الحساب | الصلاحية الأساسية | مكان السر |
|---|---|---|---|
| لوحة التحكم والمصادقة | `audit_memo_dashboard_reader` | قراءة جداول العرض والجداول الأساسية المطلوبة وتنفيذ دوال `reporting` فقط | مدير أسرار الخادم/إعداد لوحة التحكم المحمي |
| خدمة التحديث | `audit_memo_update_writer` | قراءة/إضافة/تعديل جداول `updates` فقط | `UPDATE_CONNECTION_STRING` أو ملف سر خارجي |
| سطح المكتب اليومي | `audit_memo_runtime` | تشغيل العمليات اليومية وإضافة سجل التدقيق فقط؛ بلا تعديل/حذف لسجل التدقيق | ملف تعريف الاتصال المشفر في بيانات التطبيق |
| الترحيل | `audit_memo_migrator` | صيانة مخطط محددة خلال نافذة ترقية فقط | مدير الأسرار/جلسة الصيانة |
| النسخ الاحتياطي | `audit_memo_backup` | قراءة لاستخراج النسخة؛ الاستعادة بحساب الترحيل/DBA منفصل | مدير الأسرار/جلسة النسخ |

لا توجد كلمة مرور أو سلسلة اتصال في `scripts/security_roles.sql` أو `create_db_reader_user.sql`. تُنشأ الأدوار بخصائص `NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION`، ويُدار السر خارج ZIP وخارج مجلد التطبيق.

### سياسة الاتصال

- يتصل Dashboard وDesktop بقاعدة PostgreSQL مباشرة عبر الشبكة الداخلية باستخدام عنوان الخادم والمنفذ وبيانات الحساب.
- لا يتطلب اتصال PostgreSQL شهادات أو إعدادات إضافية أو تحقق اسم المضيف، ويستخدم `SslMode=Disable`.
- يجب حصر منفذ PostgreSQL على شبكة المؤسسة وعدم فتحه على الإنترنت، مع إبقاء كلمات المرور خارج حزمة الإصدار.

### التدوير والاسترجاع المرحلي

1. أنشئ الحساب الجديد وامنحه الصلاحيات المحددة، ثم اختبر القراءة/الكتابة السالبة والموجبة من بيئة قبول.
2. حدّث سر Dashboard وخدمة التحديث وملف Desktop المشفر مع إبقاء حساب 1.0.5 قائمًا خلال نافذة التحقق.
3. نفّذ اختبار دورة تشغيل ونسخة احتياطية قابلة للقراءة، ثم راقب السجلات والصحة.
4. بعد اعتماد خروج آخر عميل 1.0.5 فقط، اسحب كلمة مرور الحساب القديم وسجّل وقت السحب.
5. عند الفشل، أعد تفعيل السر القديم فقط ضمن نافذة طوارئ موثقة، ثم أعد الاختبار والتدوير.

يجب الاحتفاظ بنسخة الترحيل والنسخ الاحتياطي في مسار الصيانة، ولا تستخدم خدمة Desktop اليومية لإجراء DDL أو restore. أي جدول أو دالة جديدة تحتاج grant يضاف في أول migration لاحق وبعد مراجعة الصلاحيات، ولا تُعدّل migrations 001–059 لهذا الغرض.

## فتح منفذ لوحة المعلومات في جدار حماية Windows (وضع InternalLanHttp فقط)

> ينطبق فقط عند تفعيل `DeploymentMode=InternalLanHttp` لعنوان لوحة المعلومات.
> عنوان خدمة التحديث يبقى HTTPS ولا يُفتح له HTTP أبداً.

1. حدد منفذ لوحة المعلومات (مثال `5000`) ونطاق الشبكة الداخلية (مثال `192.168.1.0/24`).
2. من حساب إداري على جهاز الاستضافة نفّذ — بأضيق نطاق ممكن، وملف التعريف الخاص فقط:

```powershell
New-NetFirewallRule -DisplayName "MemoDashboard LAN (TCP 5000)" `
  -Direction Inbound -Protocol TCP -LocalPort 5000 `
  -RemoteAddress 192.168.1.0/24 -Profile Private `
  -Action Allow -Program "C:\Memo\MemoDashboard.Web\Backend\MemoDashboard.Backend.exe"
```

3. تحقق من جهاز ثانٍ داخل الشبكة نفسها: `http://192.168.1.20:5000/health` ثم صفحة اللوحة.
4. ممنوع فتح هذا المنفذ على ملف تعريف Public أو على الإنترنت العام أو توجيهه عبر الراوتر.
5. عند إيقاف `InternalLanHttp` (العودة إلى `InternalLanHttps`) احذف القاعدة أو عطّلها:

```powershell
Disable-NetFirewallRule -DisplayName "MemoDashboard LAN (TCP 5000)"
```
