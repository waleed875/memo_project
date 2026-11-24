import React, { useState, useEffect } from 'react';
import { Modal } from '../common/Modal';
import { api } from '../../services/api';
import type { ReportIdentitySettingsModel } from '../../types/memo';

interface ReportIdentitySettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSaved?: () => void;
}

export const ReportIdentitySettingsModal: React.FC<ReportIdentitySettingsModalProps> = ({
  isOpen,
  onClose,
  onSaved,
}) => {
  const [settings, setSettings] = useState<ReportIdentitySettingsModel>({
    republicNameArabic: 'الجمهورية العربية السورية',
    republicNameEnglish: 'Syrian Arab Republic',
    ministryName: '',
    ministryNameEnglish: '',
    companyName: '',
    companyNameEnglish: '',
    administrativeLevelOne: '',
    administrativeLevelOneEnglish: '',
    administrativeLevelTwo: '',
    administrativeLevelTwoEnglish: '',
    administrativeLevelThree: '',
    administrativeLevelThreeEnglish: '',
    issuingEntityName: '',
    defaultCity: '',
  });

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      setLoading(true);
      setError(null);
      api.getReportIdentitySettings()
        .then((res) => {
          setSettings(res);
        })
        .catch((err) => {
          console.error('Failed to load identity settings', err);
          setError('تعذر تحميل إعدادات الهوية.');
        })
        .finally(() => setLoading(false));
    }
  }, [isOpen]);

  const handleChange = (field: keyof ReportIdentitySettingsModel, value: string) => {
    setSettings((prev) => ({ ...prev, [field]: value }));
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      await api.saveReportIdentitySettings(settings);
      onSaved?.();
      onClose();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'فشل حفظ الإعدادات.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="إعدادات قالب التقرير والهوية الرسمية" maxWidth="4xl">
      <form onSubmit={handleSave} className="space-y-4" dir="rtl">
        {error && (
          <div className="p-3 text-xs rounded-lg bg-rose-50 dark:bg-rose-950/40 text-rose-700 dark:text-rose-300 border border-rose-200 dark:border-rose-900">
            {error}
          </div>
        )}

        {loading ? (
          <div className="p-8 text-center app-text-muted">جارٍ تحميل الإعدادات...</div>
        ) : (
          <>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              تحدد هذه الحقول النصوص الإدارية والهيكل المؤسسي الذي يظهر في أعلى كافة التقارير الرسمية المصدرة.
            </p>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {/* Arabic Republic */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">اسم الدولة بالعربية</label>
                <input
                  type="text"
                  value={settings.republicNameArabic}
                  onChange={(e) => handleChange('republicNameArabic', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>

              {/* English Republic */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1 text-left" dir="ltr">Republic Name (English)</label>
                <input
                  type="text"
                  dir="ltr"
                  value={settings.republicNameEnglish}
                  onChange={(e) => handleChange('republicNameEnglish', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500 font-sans"
                />
              </div>

              {/* Arabic Ministry */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">اسم الوزارة بالعربية</label>
                <input
                  type="text"
                  placeholder="مثال: وزارة الصناعة"
                  value={settings.ministryName}
                  onChange={(e) => handleChange('ministryName', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>

              {/* English Ministry */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1 text-left" dir="ltr">Ministry Name (English)</label>
                <input
                  type="text"
                  dir="ltr"
                  placeholder="e.g. Ministry of Industry"
                  value={settings.ministryNameEnglish}
                  onChange={(e) => handleChange('ministryNameEnglish', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500 font-sans"
                />
              </div>

              {/* Arabic Company/Institution */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">اسم المؤسسة / الهيئة بالعربية</label>
                <input
                  type="text"
                  placeholder="مثال: المؤسسة العامة للصناعات الكيميائية"
                  value={settings.companyName}
                  onChange={(e) => handleChange('companyName', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>

              {/* English Company/Institution */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1 text-left" dir="ltr">Organization Name (English)</label>
                <input
                  type="text"
                  dir="ltr"
                  placeholder="e.g. General Organization for Chemical Industries"
                  value={settings.companyNameEnglish}
                  onChange={(e) => handleChange('companyNameEnglish', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500 font-sans"
                />
              </div>

              {/* Administrative Level 1 */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">المستوى الإداري الأول</label>
                <input
                  type="text"
                  placeholder="مثال: مديرية الرقابة الصناعية"
                  value={settings.administrativeLevelOne}
                  onChange={(e) => handleChange('administrativeLevelOne', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>

              {/* Administrative Level 1 English */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1 text-left" dir="ltr">Admin Level 1 (English)</label>
                <input
                  type="text"
                  dir="ltr"
                  placeholder="e.g. Directorate of Industrial Control"
                  value={settings.administrativeLevelOneEnglish}
                  onChange={(e) => handleChange('administrativeLevelOneEnglish', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500 font-sans"
                />
              </div>

              {/* Administrative Level 2 */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">المستوى الإداري الثاني</label>
                <input
                  type="text"
                  placeholder="مثال: دائرة التدقيق ومتابعة المذكرات"
                  value={settings.administrativeLevelTwo}
                  onChange={(e) => handleChange('administrativeLevelTwo', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>

              {/* Administrative Level 2 English */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1 text-left" dir="ltr">Admin Level 2 (English)</label>
                <input
                  type="text"
                  dir="ltr"
                  placeholder="e.g. Audit & Memo Follow-up Dept."
                  value={settings.administrativeLevelTwoEnglish}
                  onChange={(e) => handleChange('administrativeLevelTwoEnglish', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500 font-sans"
                />
              </div>

              {/* City */}
              <div>
                <label className="block text-xs font-semibold app-text-muted mb-1">المدينة الافتراضية</label>
                <input
                  type="text"
                  placeholder="مثال: دمشق"
                  value={settings.defaultCity}
                  onChange={(e) => handleChange('defaultCity', e.target.value)}
                  className="w-full px-3 py-1.5 text-xs rounded-lg border app-border app-surface app-text focus:outline-none focus:border-amber-500"
                />
              </div>
            </div>

            <div className="flex justify-end gap-3 pt-4 border-t app-border">
              <button
                type="button"
                onClick={onClose}
                className="px-4 py-2 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
              >
                إلغاء
              </button>
              <button
                type="submit"
                disabled={saving}
                className="px-5 py-2 text-xs font-bold rounded-lg bg-amber-600 hover:bg-amber-700 text-white shadow-sm disabled:opacity-60 cursor-pointer"
              >
                {saving ? 'جارٍ الحفظ...' : 'حفظ إعدادات الهوية'}
              </button>
            </div>
          </>
        )}
      </form>
    </Modal>
  );
};
