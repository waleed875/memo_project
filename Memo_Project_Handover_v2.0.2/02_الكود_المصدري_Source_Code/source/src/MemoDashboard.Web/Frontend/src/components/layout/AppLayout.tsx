import React, { useState, useEffect } from 'react';
import { Header } from '../Header';
import { api } from '../../services/api';

export type ActiveNavView =
  | 'dashboard'
  | 'memos'
  | 'review'
  | 'incomplete'
  | 'reports'
  | 'master'
  | 'analytics'
  | 'admin';

interface AppLayoutProps {
  currentView: ActiveNavView;
  onNavigate: (view: ActiveNavView) => void;
  children: React.ReactNode;
}

export const AppLayout: React.FC<AppLayoutProps> = ({ currentView, onNavigate, children }) => {
  const [systemMode, setSystemMode] = useState<string>('SERVER');

  useEffect(() => {
    api.getSystemMode().then((r) => setSystemMode(r.mode)).catch(() => {});
  }, []);

  return (
    <div className="min-h-screen flex flex-col app-bg font-sans" dir="rtl">
      {/* Read-Only Banner if applicable */}
      {systemMode === 'LOCAL_READONLY' && (
        <div className="bg-amber-600 text-white text-xs font-semibold py-1.5 px-4 text-center">
          ⚠️ النظام يعمل حالياً في وضع القراءة فقط (Read-Only Mode) — التعديلات وإدخال المذكرات موقوف مؤقتاً.
        </div>
      )}

      {/* Unified Header */}
      <Header
        activeView={currentView}
        onNavigate={(v) => onNavigate(v as ActiveNavView)}
        isConnected={true}
        onRefresh={() => window.location.reload()}
      />

      {/* Main Content Area */}
      <main className="flex-1 w-full max-w-[1900px] mx-auto print:max-w-none print:w-full print:m-0 print:p-0">
        {children}
      </main>

      {/* Footer */}
      <footer className="border-t app-border py-3 px-4 text-center text-[11px] app-text-muted app-surface print:hidden">
        نظام المذكرات والتدقيق — الإصدار 2.0.2 (ASP.NET Core 8 & React Modernized)
      </footer>
    </div>
  );
};