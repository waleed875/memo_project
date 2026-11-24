import type { ReactNode } from 'react'

export type PreviewView = 'executive' | 'operations' | 'company' | 'directory'
type MetricTone = 'blue' | 'teal' | 'gold' | 'red' | 'violet'
type CompanyPreviewRow = { id: string; name: string; notes: number; tone: MetricTone }

type BarItem = { id: string; label: string; value: number; tone: MetricTone; detail?: string }
type GroupedItem = { id: string; label: string; primary: number; secondary: number; x?: number }

const acceptance = {
  totalNotes: 943,
  activeCompanies: 8,
  companiesWithNotes: 5,
  onTime: 595,
  late: 348,
  pending: 51,
  approved: 846,
  repetition: 52,
  coverage: '100.00%',
  coverageDetail: '13,482 من 13,482 بندًا متوقعًا',
  quality: '93.96%',
  qualityDetail: '12,668 صحيح من 13,482 بندًا منطبقًا',
  cleanNotes: 459,
  errorNotes: 484,
  months: [
    { id: '2026-07', label: 'يوليو 2026', primary: 893, secondary: 33, x: 12 },
    { id: '2026-08', label: 'أغسطس 2026', primary: 50, secondary: 0, x: 176 },
  ] satisfies GroupedItem[],
  companies: [
    { id: 'company-al-shahbaa', name: 'الشهباء', notes: 526, tone: 'blue' as MetricTone },
    { id: 'company-al-raed', name: 'الرائد', notes: 210, tone: 'teal' as MetricTone },
    { id: 'company-al-fayhaa', name: 'الفيحاء', notes: 120, tone: 'blue' as MetricTone },
    { id: 'company-shihab', name: 'شهاب', notes: 58, tone: 'teal' as MetricTone },
    { id: 'company-saqr', name: 'صقر', notes: 29, tone: 'blue' as MetricTone },
    { id: 'company-raad', name: 'رعد', notes: 0, tone: 'gold' as MetricTone },
    { id: 'company-rimah', name: 'رماح', notes: 0, tone: 'gold' as MetricTone },
    { id: 'company-ataa', name: 'عطاء', notes: 0, tone: 'gold' as MetricTone },
  ] satisfies CompanyPreviewRow[],
  factories: [
    { id: 'factory-sensitive', label: 'مصنع المواد الحساسة والبوادئ', total: 269, onTime: 246, late: 23 },
    { id: 'factory-rockets', label: 'الراجمات', total: 108, onTime: 69, late: 39 },
    { id: 'factory-m', label: 'مصنع M', total: 96, onTime: 12, late: 84 },
  ],
  lines: [
    { id: 'line-kapson', label: 'خط الكبسون', total: 97, onTime: 90, late: 7 },
    { id: 'line-sf', label: 'خط الإنتاج SF', total: 92, onTime: 54, late: 38 },
    { id: 'line-lightning', label: 'خط الصواعق', total: 91, onTime: 84, late: 7 },
  ],
  fields: [
    { id: 'field-material-code', label: 'رمز المادة', errors: 273, sample: 937 },
    { id: 'field-warehouse-code', label: 'رمز مستودع', errors: 219, sample: 943 },
    { id: 'field-time', label: 'الوقت', errors: 135, sample: 943 },
  ],
  company: {
    name: 'الشهباء',
    notes: 526,
    correct: 7_137,
    checked: 7_566,
    errors: 429,
    quality: '94.33%',
    timeliness: '85.55%',
  },
}

const viewCopy: Record<PreviewView, { eyebrow: string; title: string; description: string }> = {
  executive: {
    eyebrow: 'معاينة تجريبية · بيانات القبول الموثقة',
    title: 'ملخص المدير العام',
    description: 'قراءة قرارية مختصرة: حجم العمل، الجودة، الالتزام الزمني، واتجاه الأداء.',
  },
  operations: {
    eyebrow: 'معاينة تجريبية · بيانات القبول الموثقة',
    title: 'لوحة النظام والعمليات',
    description: 'التدقيق والإدخال والتأخير والتكرار وجودة البيانات في مساحة عمل واحدة.',
  },
  company: {
    eyebrow: 'معاينة تجريبية · تقرير شركة مستقل',
    title: acceptance.company.name,
    description: 'تفصيل شركة واحدة بنطاق زمني واضح، مع مقارنة الفترة السابقة ومصادر المؤشرات.',
  },
  directory: {
    eyebrow: 'معاينة تجريبية · مسار مستقل',
    title: 'اختيار شركة للتقرير',
    description: 'الشركات الثماني كاملة. الشركة بلا بيانات ضمن النطاق تظهر بحالة صريحة، لا بصفر افتراضي.',
  },
}

function MetricCard({ label, value, detail, tone }: { label: string; value: string; detail: string; tone: MetricTone }) {
  return (
    <article className={'dashboard-preview-metric dashboard-preview-metric-' + tone}>
      <div className="dashboard-preview-metric-label">{label}</div>
      <div className="dashboard-preview-metric-value" dir="ltr">{value}</div>
      <div className="dashboard-preview-metric-detail">{detail}</div>
    </article>
  )
}

function Section({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <section className="dashboard-preview-section">
      <div className="dashboard-preview-section-heading">
        <div><h2>{title}</h2>{description && <p>{description}</p>}</div>
        <span className="dashboard-preview-section-mark" aria-hidden="true">✦</span>
      </div>
      {children}
    </section>
  )
}

function GroupedBars({ title, primaryLabel, secondaryLabel, items }: { title: string; primaryLabel: string; secondaryLabel: string; items: GroupedItem[] }) {
  const max = Math.max(1, ...items.flatMap((item) => [item.primary, item.secondary]))
  return (
    <div className="dashboard-preview-grouped-chart" aria-label={title}>
      <div className="dashboard-preview-legend"><span><i className="dashboard-preview-legend-primary" />{primaryLabel}</span><span><i className="dashboard-preview-legend-secondary" />{secondaryLabel}</span></div>
      <div className="dashboard-preview-grouped-bars">
        {items.map((item) => (
          <div className="dashboard-preview-group" key={item.id}>
            <div className="dashboard-preview-group-columns">
              <div className="dashboard-preview-column-wrap"><strong dir="ltr">{item.primary}</strong><span className="dashboard-preview-column dashboard-preview-column-primary" style={{ height: `${Math.max(2, item.primary / max * 100)}%` }} /></div>
              <div className="dashboard-preview-column-wrap"><strong dir="ltr">{item.secondary}</strong><span className="dashboard-preview-column dashboard-preview-column-secondary" style={{ height: `${Math.max(2, item.secondary / max * 100)}%` }} /></div>
            </div>
            <small>{item.label}</small>
          </div>
        ))}
      </div>
    </div>
  )
}

function BarList({ title, values }: { title: string; values: BarItem[] }) {
  const max = Math.max(1, ...values.map((entry) => entry.value))
  return (
    <div className="dashboard-preview-chart" aria-label={title}>
      <div className="dashboard-preview-chart-bars">
        {values.map((entry) => (
          <div className="dashboard-preview-chart-bar-item" key={entry.id}>
            <strong dir="ltr">{entry.value}</strong>
            <span className={'dashboard-preview-chart-bar dashboard-preview-chart-bar-' + entry.tone} style={{ height: `${Math.max(2, entry.value / max * 100)}%` }} />
            <small>{entry.label}</small>
            {entry.detail && <em>{entry.detail}</em>}
          </div>
        ))}
      </div>
    </div>
  )
}

function TimeTrend() {
  const primary = acceptance.months.map((item, index) => `${12 + index * 176},${136 - item.primary / 893 * 105}`).join(' ')
  const secondary = acceptance.months.map((item, index) => `${12 + index * 176},${136 - item.secondary / 33 * 105}`).join(' ')
  return (
    <div className="dashboard-preview-line-chart" aria-label="اتجاه شركة الشهباء حسب الشهر">
      <div className="dashboard-preview-line-grid" aria-hidden="true"><span /><span /><span /></div>
      <svg viewBox="0 0 188 150" role="img" aria-label="اتجاه المذكرات حسب الأشهر">
        <polyline points={primary} fill="none" stroke="#2b7c86" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
        <polyline points={secondary} fill="none" stroke="#d29b3c" strokeWidth="3" strokeDasharray="7 8" strokeLinecap="round" />
      </svg>
      <div className="dashboard-preview-line-labels"><span>يوليو 2026</span><span>أغسطس 2026</span></div>
      <div className="dashboard-preview-legend"><span><i className="dashboard-preview-legend-primary" />الوارد</span><span><i className="dashboard-preview-legend-secondary" />المعتمد</span></div>
    </div>
  )
}

function ProgressRow({ label, value, total, tone }: { label: string; value: number; total: number; tone: MetricTone }) {
  const width = total > 0 ? Math.min(100, Math.max(0, value / total * 100)) : 0
  return (
    <div className="dashboard-preview-progress-row">
      <div className="dashboard-preview-progress-copy"><span>{label}</span><strong dir="ltr">{value} / {total}</strong></div>
      <div className="dashboard-preview-progress-track"><span className={'dashboard-preview-progress-fill dashboard-preview-progress-' + tone} style={{ width: `${width}%` }} /></div>
    </div>
  )
}

function CompactTable({ headers, rows }: { headers: string[]; rows: Array<Array<string | number>> }) {
  return <div className="dashboard-preview-table-scroll"><table className="dashboard-preview-table"><thead><tr>{headers.map((header) => <th key={header}>{header}</th>)}</tr></thead><tbody>{rows.map((row) => { const rowKey = row.map(String).join('|'); return <tr key={rowKey}>{row.map((cell) => <td key={rowKey + '-' + String(cell)} dir={typeof cell === 'number' ? 'ltr' : undefined}>{cell}</td>)}</tr> })}</tbody></table></div>
}
function PreviewStates() {
  return (
    <Section title="حالات الواجهة" description="الغياب عن النطاق ليس صفرًا، والخطأ له مسار إعادة محاولة واضح.">
      <div className="dashboard-preview-state-grid">
        <div className="dashboard-preview-state dashboard-preview-state-loading"><span className="dashboard-preview-state-icon">…</span><strong>جارٍ تحميل المؤشرات</strong><small>يظهر الهيكل قبل وصول البيانات.</small></div>
        <div className="dashboard-preview-state dashboard-preview-state-empty"><span className="dashboard-preview-state-icon">—</span><strong>لا توجد بيانات ضمن النطاق</strong><small>غيّر الشركة أو التاريخ للمحاولة.</small></div>
        <div className="dashboard-preview-state dashboard-preview-state-error"><span className="dashboard-preview-state-icon">!</span><strong>تعذر تحميل البيانات</strong><small>إعادة المحاولة متاحة مع رسالة مفهومة.</small></div>
      </div>
    </Section>
  )
}

function CompanyDirectory({ compact = false }: { compact?: boolean }) {
  return (
    <div className={'dashboard-preview-directory' + (compact ? ' dashboard-preview-directory-compact' : '')}>
      {acceptance.companies.map((company) => (
        <div className="dashboard-preview-directory-row" key={company.id}>
          <span className={'dashboard-preview-company-dot dashboard-preview-dot-' + company.tone} aria-hidden="true" />
          <span className="dashboard-preview-directory-name">{company.name}</span>
          <strong dir="ltr">{company.notes}</strong>
          <span className="dashboard-preview-directory-unit">{company.notes === 0 ? 'لا توجد بيانات ضمن النطاق' : 'مذكرة'}</span>
          {!compact && <a href="?dashboardResetPreview=company">فتح التقرير</a>}
        </div>
      ))}
    </div>
  )
}

function ExecutivePreview() {
  return (
    <>
      <div className="dashboard-preview-kpis">
        <MetricCard label="إجمالي المذكرات" value="943" detail="ضمن النطاق حتى 03 أغسطس 2026" tone="blue" />
        <MetricCard label="الالتزام الزمني" value="63.10%" detail="595 في الموعد من 943 مؤهلة" tone="teal" />
        <MetricCard label="جودة البنود" value={acceptance.quality} detail={acceptance.qualityDetail} tone="teal" />
        <MetricCard label="المتأخرة" value="348" detail="من 943 مؤهلة لحساب المدة" tone="red" />
        <MetricCard label="قيد المراجعة" value="51" detail="من حالات العمل الحالية" tone="gold" />
        <MetricCard label="حوادث التكرار" value="52" detail="52 إعادة إرسال · 0 تكرار رقم" tone="red" />
      </div>
      <div className="dashboard-preview-columns">
        <Section title="اتجاه الوارد والمعتمد" description="مقارنة شهرية تساعد الإدارة على قراءة حجم العمل والاعتماد.">
          <GroupedBars title="الوارد والمعتمد حسب الشهر" primaryLabel="الوارد" secondaryLabel="المعتمد" items={acceptance.months} />
        </Section>
        <Section title="إشارة الإدارة" description="اللون يرافق شرحًا عدديًا، ولا يستبدل القرار.">
          <div className="dashboard-preview-callout dashboard-preview-callout-warning"><strong>متابعة مطلوبة</strong><span>348 مذكرة متأخرة، و51 مذكرة ما زالت قيد المراجعة.</span></div>
          <div className="dashboard-preview-callout dashboard-preview-callout-neutral"><strong>الانتقال إلى التفاصيل</strong><span>دليل الشركات مستقل عن هذه اللوحة ويعرض 8 من 8 شركات.</span></div>
          <a className="dashboard-preview-action" href="?dashboardResetPreview=directory">اختيار شركة للتقرير ←</a>
        </Section>
      </div>
    </>
  )
}

function OperationsPreview() {
  return (
    <>
      <div className="dashboard-preview-kpis">
        <MetricCard label="الوارد" value="943" detail="893 في يوليو · 50 في أغسطس" tone="blue" />
        <MetricCard label="المعتمد" value="846" detail="حالة اعتماد فعلية" tone="teal" />
        <MetricCard label="قيد المراجعة" value="51" detail="من إجمالي 943 حالة" tone="gold" />
        <MetricCard label="المتأخرة" value="348" detail="من 943 مؤهلة" tone="red" />
        <MetricCard label="متوسط التأخير" value="10.84" detail="يومًا · القيم الصالحة فقط" tone="violet" />
        <MetricCard label="التكرار المثبت" value="52" detail="نفس عداد التصنيف والاتجاه" tone="red" />
      </div>
      <div className="dashboard-preview-columns">
        <Section title="حالات سير العمل" description="الحالات فروع تشغيلية؛ العدد والنسبة من إجمالي المذكرات.">
          <div className="dashboard-preview-workflow">
            {[
              ['معتمدة', 846, '89.71%', 'teal'],
              ['تحتوي أخطاء', 49, '5.20%', 'red'],
              ['معادة للتصحيح', 45, '4.77%', 'red'],
              ['مكتملة', 2, '0.21%', 'teal'],
              ['مسودة', 1, '0.11%', 'gold'],
            ].map(([label, count, ratio, tone]) => <div className="dashboard-preview-workflow-step" key={label}><span className={'dashboard-preview-step-index dashboard-preview-step-' + tone}>حالة</span><div><strong>{label}</strong><small>{ratio} من إجمالي المذكرات</small></div><b dir="ltr">{count}</b></div>)}
          </div>
        </Section>
        <Section title="تصنيف التكرار" description="العدد نفسه في البطاقة والرسم والجدول.">
          <BarList title="تصنيف التكرار" values={[{ id: 'company-resend', label: 'إعادة إرسال من الشركة', value: 52, tone: 'red', detail: '52 من 943 مذكرة' }, { id: 'duplicate-number', label: 'تكرار رقم المذكرة', value: 0, tone: 'gold', detail: '0 من 943 مذكرة' }]} />
          <CompactTable headers={['التصنيف', 'العدد', 'النسبة']} rows={[['إعادة إرسال من الشركة', 52, '5.51%'], ['تكرار رقم المذكرة', 0, '0.00%']]} />
        </Section>
      </div>
      <div className="dashboard-preview-columns">
        <Section title="أعلى الحقول خطأً" description="الأسماء عربية والعدد مرتبط بمقام الفحص.">
          {acceptance.fields.map((field) => <ProgressRow key={field.id} label={field.label} value={field.errors} total={field.sample} tone="red" />)}
        </Section>
        <Section title="اكتمال البيانات" description="النقص يظهر كحالة تشغيلية مستقلة.">
          <ProgressRow label="دون مصنع" value={1} total={acceptance.totalNotes} tone="gold" />
          <ProgressRow label="دون خط إنتاج" value={17} total={acceptance.totalNotes} tone="gold" />
          <div className="dashboard-preview-note">لا توجد بيانات مفقودة لتاريخ الكتابة ضمن هذا النطاق.</div>
        </Section>
      </div>
      <div className="dashboard-preview-columns">
        <Section title="أداء المصانع" description="أعلى الوحدات مع العدد الفعلي لكل جزء.">
          <CompactTable headers={['الوحدة', 'الإجمالي', 'في الموعد', 'متأخر']} rows={acceptance.factories.map((row) => [row.label, row.total, row.onTime, row.late])} />
        </Section>
        <Section title="أداء خطوط الإنتاج" description="الاسم الدلالي يظهر مع أعداد الالتزام والتأخير.">
          <CompactTable headers={['خط الإنتاج', 'الإجمالي', 'في الموعد', 'متأخر']} rows={acceptance.lines.map((row) => [row.label, row.total, row.onTime, row.late])} />
        </Section>
      </div>
    </>
  )
}

function CompanyPreview() {
  return (
    <>
      <div className="dashboard-preview-company-toolbar">
        <label>الشركة<select defaultValue="company-al-shahbaa"><option value="company-al-shahbaa">الشهباء</option></select></label>
        <label>من تاريخ<input type="date" defaultValue="2026-07-01" /></label>
        <label>إلى تاريخ<input type="date" defaultValue="2026-08-03" /></label>
        <button type="button">تطبيق النطاق</button>
      </div>
      <div className="dashboard-preview-company-hero">
        <div><span className="dashboard-preview-company-tag">تقرير شركة · 01 يوليو — 03 أغسطس 2026</span><h2>{acceptance.company.name}</h2><p>الفترة الحالية: 526 مذكرة · المقارنة السابقة غير قابلة للقياس دون عينة سابقة.</p></div>
        <div className="dashboard-preview-company-score"><span>جودة البنود</span><strong dir="ltr">{acceptance.company.quality}</strong><small dir="ltr">{acceptance.company.correct} من {acceptance.company.checked}</small></div>
      </div>
      <div className="dashboard-preview-kpis">
        <MetricCard label="إجمالي المذكرات" value="526" detail="ضمن الشركة والفترة" tone="blue" />
        <MetricCard label="الصحيح" value="7,137" detail="من 7,566 بندًا منطبقًا" tone="teal" />
        <MetricCard label="الخاطئ" value="429" detail="من 7,566 بندًا منطبقًا" tone="red" />
        <MetricCard label="الفترة السابقة" value="غير متاح" detail="لا توجد عينة قابلة للمقارنة" tone="gold" />
      </div>
      <div className="dashboard-preview-columns">
        <Section title="اتجاه المذكرات" description="التغيير الزمني يعرض أشهرًا عربية مسماة."><TimeTrend /></Section>
        <Section title="جودة الشركة" description="مقارنة واضحة بين الصحيح والخاطئ مع المقام."><GroupedBars title="الصحيح والخاطئ" primaryLabel="صحيح" secondaryLabel="خطأ" items={[{ id: 'company-quality', label: 'بنود الشهباء', primary: acceptance.company.correct, secondary: acceptance.company.errors }]} /></Section>
      </div>
      <div className="dashboard-preview-columns">
        <Section title="مصانع الشركة" description="أرقام الالتزام داخل جدول قابل للقراءة."><CompactTable headers={['الوحدة', 'الإجمالي', 'في الموعد', 'متأخر']} rows={acceptance.factories.slice(0, 2).map((row) => [row.label, row.total, row.onTime, row.late])} /></Section>
        <Section title="آخر مذكرات الشركة" description="صفوف مختصرة دون ارتفاع فارغ ثابت."><CompactTable headers={['رقم المذكرة', 'الحالة', 'تاريخ الوصول']} rows={[[37, 'معتمدة', '03 أغسطس 2026'], [196, 'معتمدة', '03 أغسطس 2026'], [502, 'تحتوي أخطاء', '03 أغسطس 2026']]} /></Section>
      </div>
    </>
  )
}

function DirectoryPreview() {
  return (
    <>
      <section className="dashboard-preview-directory-panel">
        <div className="dashboard-preview-directory-toolbar"><label>بحث عن شركة<input type="search" placeholder="اكتب اسم الشركة" /></label><span>8 من 8 شركات</span></div>
        <div className="dashboard-preview-directory-head"><span>الشركة</span><span>الحالة ضمن النطاق</span><span>الإجراء</span></div>
        <CompanyDirectory />
      </section>
      <Section title="مسار الانتقال" description="اختيار الشركة يفتح تقرير الشركة المستقل مع نفس النطاق الزمني.">
        <div className="dashboard-preview-drilldown"><span>اختيار</span><strong>الشهباء</strong><span>←</span><strong>تقرير شركة مستقل</strong><a href="?dashboardResetPreview=company">معاينة الانتقال</a></div>
      </Section>
      <div className="dashboard-preview-note dashboard-preview-note-wide">الشركات الثلاث: رعد، رماح، عطاء؛ موجودة في الدليل وعددها صفر حقيقي ضمن هذا النطاق، مع حالة غياب بيانات منفصلة عن القياسات غير القابلة للمقارنة.</div>
    </>
  )
}

export function DashboardPreview({ view }: { view: PreviewView }) {
  const copy = viewCopy[view]
  return (
    <main className="dashboard-preview-page" dir="rtl" lang="ar" data-testid={'preview-view-' + view}>
      <div className="dashboard-preview-shell">
        <header className="dashboard-preview-header">
          <div className="dashboard-preview-brand"><span className="dashboard-preview-brand-mark">M</span><div><span className="dashboard-preview-brand-name">AuditMemo</span><small>معاينة منفصلة · غير منشورة</small></div></div>
          <div className="dashboard-preview-actions"><span className="dashboard-preview-status"><i /> بيانات قبول موثقة</span><span className="dashboard-preview-version">إصدار 1.1.0 · مرحلة المعاينة</span></div>
        </header>
        <nav className="dashboard-preview-tabs" aria-label="أنواع المعاينة">
          <a className={view === 'executive' ? 'active' : ''} href="?dashboardResetPreview=executive">المدير العام</a>
          <a className={view === 'operations' ? 'active' : ''} href="?dashboardResetPreview=operations">النظام والعمليات</a>
          <a className={view === 'company' ? 'active' : ''} href="?dashboardResetPreview=company">تقرير شركة</a>
          <a className={view === 'directory' ? 'active' : ''} href="?dashboardResetPreview=directory">اختيار الشركات</a>
        </nav>
        <section className="dashboard-preview-intro">
          <div><span className="dashboard-preview-eyebrow">{copy.eyebrow}</span><h1>{copy.title}</h1><p>{copy.description}</p></div>
          <div className="dashboard-preview-period"><span>النطاق</span><strong>حتى 03 أغسطس 2026</strong><small>المصدر: نظام المذكرات · معاينة فقط</small></div>
        </section>
        {view === 'executive' && <ExecutivePreview />}
        {view === 'operations' && <OperationsPreview />}
        {view === 'company' && <CompanyPreview />}
        {view === 'directory' && <DirectoryPreview />}
        <PreviewStates />
        <footer className="dashboard-preview-footer"><span>معاينة تصميم فقط · لا تغيّر المسار الفعلي</span><strong>8 شركات مسجلة · 943 مذكرة · 5 لديها نشاط</strong></footer>
      </div>
    </main>
  )
}