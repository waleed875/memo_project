import React, { useState } from 'react';
import { api } from '../services/api';
import { t } from '../i18n';
import { useDirection } from '../i18n/LanguageProvider';
import SyrianLogo from '../assets/Syrian_logo_icon_gold.svg';

interface AdminLoginPageProps {
  onAuthenticated: () => void;
  onOpenSetup?: () => void;
}

export function AdminLoginPage({ onAuthenticated, onOpenSetup }: AdminLoginPageProps) {
  const dir = useDirection();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [mustChangePassword, setMustChangePassword] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const handleLogin = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      const session = await api.loginAdmin(username.trim(), password);
      if (session.mustChangePassword) {
        setMustChangePassword(true);
        setMessage('حسابك يتطلب تعيين كلمة مرور جديدة للمتابعة.');
        return;
      }
      if (!session.authenticated) {
        setMessage(session.message ?? t('updates.loginFailed'));
        return;
      }
      setPassword('');
      onAuthenticated();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : t('updates.loginFailed'));
    } finally {
      setBusy(false);
    }
  };

  const handleChangePasswordAndLogin = async (event: React.FormEvent) => {
    event.preventDefault();
    if (newPassword.length < 8) {
      setMessage('كلمة المرور الجديدة يجب أن تكون 8 محارف على الأقل.');
      return;
    }
    if (newPassword !== confirmPassword) {
      setMessage('كلمتا المرور غير متطابقتين.');
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      await api.changePassword(password, newPassword, username.trim());
      // Re-login with new password
      const session = await api.loginAdmin(username.trim(), newPassword);
      if (session.authenticated) {
        setPassword('');
        setNewPassword('');
        setConfirmPassword('');
        onAuthenticated();
      } else {
        setMessage('تم تغيير كلمة المرور، يرجى تسجيل الدخول بها الآن.');
        setMustChangePassword(false);
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'فشل تغيير كلمة المرور.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="app-bg flex min-h-screen items-center justify-center px-4 py-8" dir={dir}>
      <div className="app-surface w-full max-w-md rounded-2xl border app-border p-7 shadow-lg">
        {/* Header */}
        <div className="mb-6 text-center">
          <div className="mx-auto mb-4 h-20 w-20 flex items-center justify-center p-2 rounded-2xl border border-amber-400/50 bg-gradient-to-br from-amber-500/15 via-amber-500/5 to-transparent shadow-md">
            <img
              src={SyrianLogo}
              alt="الشعار الرسمي للجمهورية العربية السورية"
              className="h-full w-full object-contain"
              onError={(e) => { e.currentTarget.src = '/brand/Syrian_logo_icon_gold.svg'; }}
            />
          </div>
          <p className="text-xs font-semibold tracking-wide text-amber-700 dark:text-amber-400 mb-1">الجمهورية العربية السورية</p>
          <h1 className="text-xl font-bold app-text">نظام تدقيق المذكرات والرقابة الصناعية</h1>
          <p className="mt-1 text-xs app-text-muted">بوابة الإدارة والتفتيش المركزي — الإصدار 2.0.2</p>
        </div>

        {!mustChangePassword ? (
          <form onSubmit={handleLogin} className="space-y-4">
            <label className="block text-xs font-semibold app-text">
              <span className="mb-1 block">اسم المستخدم</span>
              <input
                required
                maxLength={128}
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
                autoComplete="username"
                dir="auto"
              />
            </label>

            <label className="block text-xs font-semibold app-text">
              <span className="mb-1 block">كلمة المرور</span>
              <input
                required
                maxLength={512}
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
                autoComplete="current-password"
                dir="ltr"
              />
            </label>

            {message && (
              <div className="rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 px-3 py-2 text-xs text-rose-700 dark:text-rose-300">
                {message}
              </div>
            )}

            <button
              type="submit"
              disabled={busy}
              className="btn-primary w-full rounded-lg px-4 py-2.5 text-sm font-semibold disabled:opacity-60 transition-colors"
            >
              {busy ? 'جارٍ التحقق...' : 'تسجيل الدخول'}
            </button>
          </form>
        ) : (
          <form onSubmit={handleChangePasswordAndLogin} className="space-y-4">
            <div className="rounded-lg bg-blue-50 dark:bg-blue-950/40 border border-blue-200 dark:border-blue-900 p-3 text-xs text-blue-800 dark:text-blue-300">
              يجب عليك تعيين كلمة مرور جديدة لحسابك للمتابعة لأسباب أمنية.
            </div>

            <label className="block text-xs font-semibold app-text">
              <span className="mb-1 block">كلمة المرور الجديدة *</span>
              <input
                required
                maxLength={512}
                type="password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                placeholder="8 محارف على الأقل"
                className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
                dir="ltr"
              />
            </label>

            <label className="block text-xs font-semibold app-text">
              <span className="mb-1 block">تأكيد كلمة المرور الجديدة *</span>
              <input
                required
                maxLength={512}
                type="password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                placeholder="أعد إدخال كلمة المرور"
                className="w-full rounded-lg border app-border app-surface px-3 py-2 text-sm app-text focus:outline-none focus:ring-2 focus:ring-blue-500"
                dir="ltr"
              />
            </label>

            {message && (
              <div className="rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900 px-3 py-2 text-xs text-rose-700 dark:text-rose-300">
                {message}
              </div>
            )}

            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => setMustChangePassword(false)}
                className="px-4 py-2 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
              >
                رجوع
              </button>
              <button
                type="submit"
                disabled={busy}
                className="btn-primary flex-1 rounded-lg px-4 py-2.5 text-sm font-semibold disabled:opacity-60 transition-colors"
              >
                {busy ? 'جارٍ الحفظ...' : 'حفظ والدخول'}
              </button>
            </div>
          </form>
        )}

        {onOpenSetup && (
          <div className="mt-5 pt-4 border-t app-border text-center">
            <button
              type="button"
              onClick={onOpenSetup}
              className="inline-flex items-center justify-center gap-2 text-xs font-semibold text-slate-600 dark:text-slate-400 hover:text-amber-700 dark:hover:text-amber-400 transition-colors"
            >
              <span>⚙️</span>
              <span>ضبط إعدادات الاتصال بقاعدة البيانات</span>
            </button>
          </div>
        )}
      </div>
    </div>
  );
}