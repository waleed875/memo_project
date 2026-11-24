import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../services/api';
import { Modal } from '../../components/common/Modal';
import type { UserModel, RoleModel, AuditLogModel } from '../../types/memo';

type AdminTab = 'users' | 'audit' | 'system';

export const AdminConsolePage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<AdminTab>('users');

  // Users & Roles state
  const [users, setUsers] = useState<UserModel[]>([]);
  const [roles, setRoles] = useState<RoleModel[]>([]);
  const [loadingUsers, setLoadingUsers] = useState(false);

  // User Add/Edit modal
  const [userModal, setUserModal] = useState<{
    isOpen: boolean;
    id: string | null;
    username: string;
    fullName: string;
    password?: string;
    roleId: string;
    isActive: boolean;
    mustChangePassword: boolean;
  }>({
    isOpen: false,
    id: null,
    username: '',
    fullName: '',
    password: '',
    roleId: '',
    isActive: true,
    mustChangePassword: false,
  });

  // Reset Password modal
  const [resetModal, setResetModal] = useState<{
    isOpen: boolean;
    userId: string | null;
    userName: string;
    newPassword: string;
  }>({
    isOpen: false,
    userId: null,
    userName: '',
    newPassword: '',
  });

  // Audit Logs state
  const [logs, setLogs] = useState<AuditLogModel[]>([]);
  const [totalLogs, setTotalLogs] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [logPage, setLogPage] = useState(1);
  const [actionTypes, setActionTypes] = useState<string[]>([]);
  const [filterAction, setFilterAction] = useState('');
  const [filterTable, setFilterTable] = useState('');
  const [filterUser, setFilterUser] = useState('');
  const [loadingLogs, setLoadingLogs] = useState(false);

  // System Mode state
  const [systemMode, setSystemMode] = useState<string>('SERVER');
  const [loadingMode, setLoadingMode] = useState(false);
  const [changingMode, setChangingMode] = useState(false);

  const [submitting, setSubmitting] = useState(false);

  const loadUsersAndRoles = useCallback(async () => {
    setLoadingUsers(true);
    try {
      const [u, r] = await Promise.all([api.getUsers(), api.getRoles()]);
      setUsers(u);
      setRoles(r);
    } catch (err) {
      console.error('Failed to load users/roles', err);
    } finally {
      setLoadingUsers(false);
    }
  }, []);

  const loadAuditLogs = useCallback(async () => {
    setLoadingLogs(true);
    try {
      const res = await api.getAuditLogs({
        pageNumber: logPage,
        pageSize: 25,
        actionType: filterAction || undefined,
        tableName: filterTable || undefined,
        userId: filterUser || undefined,
      });
      setLogs(res.items);
      setTotalLogs(res.totalCount);
      setTotalPages(res.totalPages || Math.ceil(res.totalCount / 25) || 1);
    } catch (err) {
      console.error('Failed to load audit logs', err);
    } finally {
      setLoadingLogs(false);
    }
  }, [logPage, filterAction, filterTable, filterUser]);

  const loadSystemMode = useCallback(async () => {
    setLoadingMode(true);
    try {
      const res = await api.getSystemMode();
      setSystemMode(res.mode);
    } catch (err) {
      console.error('Failed to load system mode', err);
    } finally {
      setLoadingMode(false);
    }
  }, []);

  useEffect(() => {
    loadUsersAndRoles();
    api.getAuditActions().then(setActionTypes).catch(console.error);
    loadSystemMode();
  }, [loadUsersAndRoles, loadSystemMode]);

  useEffect(() => {
    if (activeTab === 'audit') {
      loadAuditLogs();
    }
  }, [activeTab, loadAuditLogs]);

  // Save User
  const handleSaveUser = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      if (userModal.id) {
        await api.updateUser(
          userModal.id,
          {
            fullName: userModal.fullName,
            roleId: userModal.roleId,
            isActive: userModal.isActive,
            mustChangePassword: userModal.mustChangePassword,
          },
          userModal.password ? userModal.password : undefined
        );
      } else {
        await api.saveUser(
          {
            username: userModal.username,
            fullName: userModal.fullName,
            roleId: userModal.roleId,
            isActive: userModal.isActive,
            mustChangePassword: userModal.mustChangePassword,
          },
          userModal.password
        );
      }
      setUserModal({
        isOpen: false,
        id: null,
        username: '',
        fullName: '',
        password: '',
        roleId: '',
        isActive: true,
        mustChangePassword: false,
      });
      loadUsersAndRoles();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل حفظ بيانات المستخدم.');
    } finally {
      setSubmitting(false);
    }
  };

  // Reset Password
  const handleResetPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!resetModal.userId || !resetModal.newPassword) return;
    setSubmitting(true);
    try {
      await api.resetPassword(resetModal.userId, resetModal.newPassword);
      alert('تم إعادة تعيين كلمة المرور بنجاح.');
      setResetModal({ isOpen: false, userId: null, userName: '', newPassword: '' });
      loadUsersAndRoles();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل إعادة تعيين كلمة المرور.');
    } finally {
      setSubmitting(false);
    }
  };

  // Change System Mode
  const handleChangeSystemMode = async (targetMode: 'SERVER' | 'LOCAL_READONLY') => {
    if (!window.confirm(`هل أنت متأكد من تحويل وضع تشغيل النظام إلى: ${targetMode === 'SERVER' ? 'الوضع الكامل (خادم)' : 'وضع القراءة فقط'}؟`)) return;
    setChangingMode(true);
    try {
      await api.setSystemMode(targetMode);
      setSystemMode(targetMode);
      alert('تم تغيير وضع تشغيل النظام بنجاح.');
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : 'فشل تغيير وضع التشغيل.');
    } finally {
      setChangingMode(false);
    }
  };

  return (
    <div className="flex flex-col gap-6 p-6 min-h-screen app-bg" dir="rtl">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-4">
        <div>
          <h1 className="text-2xl font-bold app-text flex items-center gap-3">
            <span>⚙️</span>
            <span>لوحة الإدارة والتحكم</span>
          </h1>
          <p className="text-sm app-text-muted mt-1">
            إدارة حسابات المستخدمين وصلاحياتهم، مراقبة سجل التدقيق والعمليات، والتحكم بوضع تشغيل النظام.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-2 border-b app-border pb-1">
        <button
          type="button"
          onClick={() => setActiveTab('users')}
          className={`px-4 py-2.5 text-sm font-semibold rounded-t-lg transition-colors border-b-2 ${
            activeTab === 'users'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          المستخدمون والصلاحيات ({users.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('audit')}
          className={`px-4 py-2.5 text-sm font-semibold rounded-t-lg transition-colors border-b-2 ${
            activeTab === 'audit'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          سجل التدقيق والرقابة ({totalLogs})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab('system')}
          className={`px-4 py-2.5 text-sm font-semibold rounded-t-lg transition-colors border-b-2 ${
            activeTab === 'system'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          وضع التشغيل وإعدادات الأمان
        </button>
      </div>

      {/* Tab 1: Users & Roles */}
      {activeTab === 'users' && (
        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <span className="text-sm font-semibold app-text-muted">قائمة مستخدمي النظام ومستويات وصولهم</span>
            <button
              type="button"
              onClick={() =>
                setUserModal({
                  isOpen: true,
                  id: null,
                  username: '',
                  fullName: '',
                  password: '',
                  roleId: roles[0]?.id || '',
                  isActive: true,
                  mustChangePassword: false,
                })
              }
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700 transition-colors"
            >
              + إضافة مستخدم جديد
            </button>
          </div>

          <div className="app-surface rounded-xl border app-border overflow-hidden shadow-sm">
            <table className="w-full text-right text-sm border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 text-xs font-semibold app-text-muted">
                  <th className="p-3">اسم المستخدم</th>
                  <th className="p-3">الاسم الكامل</th>
                  <th className="p-3">الدور الوظيفي</th>
                  <th className="p-3">الحالة</th>
                  <th className="p-3">كلمة المرور</th>
                  <th className="p-3 text-center">الإجراءات</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {loadingUsers ? (
                  <tr>
                    <td colSpan={6} className="p-8 text-center app-text-muted">جارٍ تحميل المستخدمين...</td>
                  </tr>
                ) : (
                  users.map((u) => (
                    <tr key={u.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/40">
                      <td className="p-3 font-mono font-bold text-blue-600 dark:text-blue-400">{u.username}</td>
                      <td className="p-3 font-semibold app-text">{u.fullName}</td>
                      <td className="p-3">
                        <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-indigo-50 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 border border-indigo-200 dark:border-indigo-800">
                          {u.roleNameAr || u.roleCode}
                        </span>
                      </td>
                      <td className="p-3">
                        <span className={`px-2 py-0.5 text-xs rounded-full font-semibold ${u.isActive ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300' : 'bg-slate-100 text-slate-500'}`}>
                          {u.isActive ? 'نشط' : 'معطل'}
                        </span>
                      </td>
                      <td className="p-3 text-xs text-slate-400">
                        {u.mustChangePassword ? (
                          <span className="text-amber-600 dark:text-amber-400 font-semibold">مطلوب التغيير عند الدخول</span>
                        ) : (
                          'مُعيّنة'
                        )}
                      </td>
                      <td className="p-3 text-center">
                        <div className="flex items-center justify-center gap-2">
                          <button
                            type="button"
                            onClick={() =>
                              setUserModal({
                                isOpen: true,
                                id: u.id,
                                username: u.username,
                                fullName: u.fullName,
                                password: '',
                                roleId: u.roleId,
                                isActive: u.isActive,
                                mustChangePassword: u.mustChangePassword,
                              })
                            }
                            className="px-2.5 py-1 text-xs rounded border app-border app-hover app-text"
                          >
                            تعديل
                          </button>
                          <button
                            type="button"
                            onClick={() =>
                              setResetModal({
                                isOpen: true,
                                userId: u.id,
                                userName: u.fullName || u.username,
                                newPassword: '',
                              })
                            }
                            className="px-2.5 py-1 text-xs rounded border border-amber-300 dark:border-amber-800 text-amber-700 dark:text-amber-300 app-hover"
                          >
                            إعادة تعيين كلمة المرور
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Tab 2: Audit Logs */}
      {activeTab === 'audit' && (
        <div className="space-y-4">
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 app-surface p-4 rounded-xl border app-border shadow-sm text-xs">
            <div>
              <label className="block font-semibold app-text-muted mb-1">نوع العملية</label>
              <select
                value={filterAction}
                onChange={(e) => setFilterAction(e.target.value)}
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              >
                <option value="">جميع أنواع العمليات</option>
                {actionTypes.map((a) => (
                  <option key={a} value={a}>{a}</option>
                ))}
              </select>
            </div>
            <div>
              <label className="block font-semibold app-text-muted mb-1">اسم الجدول أو السجل</label>
              <input
                type="text"
                value={filterTable}
                onChange={(e) => setFilterTable(e.target.value)}
                placeholder="core.notes, system.users..."
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              />
            </div>
            <div>
              <label className="block font-semibold app-text-muted mb-1">المستخدم</label>
              <input
                type="text"
                value={filterUser}
                onChange={(e) => setFilterUser(e.target.value)}
                placeholder="اسم المستخدم أو المعرف..."
                className="w-full px-2.5 py-1.5 rounded-lg border app-border app-surface app-text focus:ring-1 focus:ring-blue-500"
              />
            </div>
            <div className="flex items-end">
              <button
                type="button"
                onClick={() => {
                  setLogPage(1);
                  loadAuditLogs();
                }}
                disabled={loadingLogs}
                className="w-full px-4 py-2 text-xs font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700"
              >
                {loadingLogs ? 'جارٍ البحث...' : 'تحديث سجل التدقيق'}
              </button>
            </div>
          </div>

          <div className="app-surface rounded-xl border app-border overflow-hidden shadow-sm">
            <table className="w-full text-right text-xs border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 font-semibold app-text-muted">
                  <th className="p-3">التاريخ والوقت</th>
                  <th className="p-3">المستخدم</th>
                  <th className="p-3">نوع الإجراء</th>
                  <th className="p-3">الجدول</th>
                  <th className="p-3">الوصف والتفاصيل</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {logs.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="p-8 text-center app-text-muted">
                      {loadingLogs ? 'جارٍ تحميل سجل التدقيق...' : 'لا توجد سجلات تدقيق مسجلة.'}
                    </td>
                  </tr>
                ) : (
                  logs.map((log) => (
                    <tr key={log.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30">
                      <td className="p-3 font-mono text-slate-500 whitespace-nowrap">
                        {log.createdAt ? log.createdAt.replace('T', ' ').slice(0, 19) : '—'}
                      </td>
                      <td className="p-3 font-medium app-text whitespace-nowrap">{log.userName || 'النظام'}</td>
                      <td className="p-3 whitespace-nowrap">
                        <span className="px-2 py-0.5 rounded text-[11px] font-mono font-semibold bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300">
                          {log.actionType}
                        </span>
                      </td>
                      <td className="p-3 font-mono text-slate-400 whitespace-nowrap">
                        {log.tableName || '—'}
                      </td>
                      <td className="p-3 app-text max-w-md break-words">{log.description || '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>

            {/* Pagination for Audit Logs */}
            {totalPages > 1 && (
              <div className="flex items-center justify-between p-3 border-t app-border text-xs">
                <span className="app-text-muted">الصفحة {logPage} من {totalPages}</span>
                <div className="flex gap-1">
                  <button
                    type="button"
                    disabled={logPage <= 1}
                    onClick={() => setLogPage((p) => Math.max(1, p - 1))}
                    className="px-2.5 py-1 rounded border app-border disabled:opacity-40"
                  >
                    السابق
                  </button>
                  <button
                    type="button"
                    disabled={logPage >= totalPages}
                    onClick={() => setLogPage((p) => Math.min(totalPages, p + 1))}
                    className="px-2.5 py-1 rounded border app-border disabled:opacity-40"
                  >
                    التالي
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Tab 3: System Mode */}
      {activeTab === 'system' && (
        <div className="space-y-6 max-w-3xl">
          <div className="app-surface rounded-xl border app-border p-6 shadow-sm space-y-4">
            <h2 className="text-lg font-bold app-text flex items-center gap-2">
              <span>🔒</span>
              <span>وضع تشغيل النظام (System Operational Mode)</span>
            </h2>
            <p className="text-sm app-text-muted">
              يتحكم وضع التشغيل في قدرة المستخدمين على إجراء تعديلات، إنشاء مذكرات، أو قفل النظام في وضع القراءة فقط لأغراض التدقيق أو الصيانة.
            </p>

            <div className="p-4 rounded-xl border app-border bg-slate-50 dark:bg-slate-900/40 flex items-center justify-between">
              <div>
                <span className="text-xs font-semibold app-text-muted block">الوضع الحالي:</span>
                <span className="text-lg font-bold text-blue-600 dark:text-blue-400">
                  {loadingMode
                    ? 'جارٍ التحقق...'
                    : systemMode === 'SERVER'
                    ? 'خادم رئيسي (كامل الصلاحيات)'
                    : 'قراءة فقط (Read-Only)'}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {systemMode === 'SERVER' ? (
                  <button
                    type="button"
                    onClick={() => handleChangeSystemMode('LOCAL_READONLY')}
                    disabled={changingMode}
                    className="px-4 py-2 text-xs font-semibold rounded-lg bg-amber-600 text-white hover:bg-amber-700 disabled:opacity-50"
                  >
                    {changingMode ? 'جارٍ التغيير...' : 'تحويل إلى وضع القراءة فقط'}
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={() => handleChangeSystemMode('SERVER')}
                    disabled={changingMode}
                    className="px-4 py-2 text-xs font-semibold rounded-lg bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50"
                  >
                    {changingMode ? 'جارٍ التغيير...' : 'تحويل إلى الوضع الكامل (خادم)'}
                  </button>
                )}
              </div>
            </div>

            <div className="text-xs space-y-2 text-slate-500 dark:text-slate-400 pt-2">
              <p>• <strong>الوضع الكامل (SERVER):</strong> يسمح لكافة المستخدمين بإدخال المذكرات، رفع المرفقات، المراجعة والاعتماد وفق صلاحياتهم.</p>
              <p>• <strong>وضع القراءة فقط (LOCAL_READONLY):</strong> يمنع أي عمليات كتابة أو تعديل ويحمي البيانات من أي تغيير، ومناسب لفترات الجرد أو الترحيل.</p>
            </div>
          </div>
        </div>
      )}

      {/* User Add/Edit Modal */}
      <Modal
        isOpen={userModal.isOpen}
        onClose={() => setUserModal({ ...userModal, isOpen: false })}
        title={userModal.id ? 'تعديل بيانات المستخدم' : 'إضافة مستخدم جديد'}
      >
        <form onSubmit={handleSaveUser} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">اسم المستخدم (لتسجيل الدخول) *</label>
            <input
              type="text"
              required
              disabled={Boolean(userModal.id)}
              value={userModal.username}
              onChange={(e) => setUserModal({ ...userModal, username: e.target.value })}
              className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:ring-2 focus:ring-blue-500 disabled:opacity-60"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">الاسم الكامل *</label>
            <input
              type="text"
              required
              value={userModal.fullName}
              onChange={(e) => setUserModal({ ...userModal, fullName: e.target.value })}
              className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">الدور الوظيفي والصلاحيات *</label>
            <select
              required
              value={userModal.roleId}
              onChange={(e) => setUserModal({ ...userModal, roleId: e.target.value })}
              className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            >
              <option value="">-- اختر الدور --</option>
              {roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.nameAr} ({r.code})
                </option>
              ))}
            </select>
          </div>

          {!userModal.id && (
            <div>
              <label className="block text-xs font-semibold app-text-muted mb-1">كلمة المرور الأولية *</label>
              <input
                type="password"
                required={!userModal.id}
                value={userModal.password}
                onChange={(e) => setUserModal({ ...userModal, password: e.target.value })}
                placeholder="8 محارف على الأقل"
                className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
              />
            </div>
          )}

          <div className="space-y-2 pt-2 border-t app-border">
            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="userActiveCheck"
                checked={userModal.isActive}
                onChange={(e) => setUserModal({ ...userModal, isActive: e.target.checked })}
                className="rounded border-slate-300"
              />
              <label htmlFor="userActiveCheck" className="text-sm app-text">حساب نشط ومفعّل</label>
            </div>
            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="userMustChangeCheck"
                checked={userModal.mustChangePassword}
                onChange={(e) => setUserModal({ ...userModal, mustChangePassword: e.target.checked })}
                className="rounded border-slate-300"
              />
              <label htmlFor="userMustChangeCheck" className="text-sm app-text">إلزام المستخدم بتغيير كلمة المرور فور الدخول</label>
            </div>
          </div>

          <div className="flex justify-end gap-3 pt-3 border-t app-border">
            <button
              type="button"
              onClick={() => setUserModal({ ...userModal, isOpen: false })}
              className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="px-5 py-2 text-sm font-semibold rounded-lg bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-60"
            >
              {submitting ? 'جارٍ الحفظ...' : 'حفظ بيانات المستخدم'}
            </button>
          </div>
        </form>
      </Modal>

      {/* Reset Password Modal */}
      <Modal
        isOpen={resetModal.isOpen}
        onClose={() => setResetModal({ ...resetModal, isOpen: false })}
        title={`إعادة تعيين كلمة مرور: ${resetModal.userName}`}
      >
        <form onSubmit={handleResetPassword} className="space-y-4">
          <p className="text-xs app-text-muted">
            سيتم تعيين كلمة مرور جديدة للمستخدم فوراً وإلزامه بتغييرها عند أول تسجيل دخول.
          </p>
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">كلمة المرور الجديدة *</label>
            <input
              type="password"
              required
              value={resetModal.newPassword}
              onChange={(e) => setResetModal({ ...resetModal, newPassword: e.target.value })}
              placeholder="8 محارف على الأقل"
              className="w-full px-3 py-2 text-sm rounded-lg border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div className="flex justify-end gap-3 pt-3 border-t app-border">
            <button
              type="button"
              onClick={() => setResetModal({ ...resetModal, isOpen: false })}
              className="px-4 py-2 text-sm font-semibold rounded-lg border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting || resetModal.newPassword.length < 8}
              className="px-5 py-2 text-sm font-semibold rounded-lg bg-amber-600 text-white hover:bg-amber-700 disabled:opacity-60"
            >
              {submitting ? 'جارٍ التعيين...' : 'تأكيد التعيين'}
            </button>
          </div>
        </form>
      </Modal>
    </div>
  );
};