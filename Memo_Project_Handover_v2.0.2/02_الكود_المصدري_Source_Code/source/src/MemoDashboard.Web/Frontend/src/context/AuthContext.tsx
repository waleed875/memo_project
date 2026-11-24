import React, { createContext, useContext, useEffect, useState } from 'react';
import { api, type AdminSession } from '../services/api';
import type { UserRole } from '../types/memo';

interface AuthContextType {
  user: AdminSession | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  role: UserRole | null;
  isAdmin: boolean;
  isReviewer: boolean;
  isDataEntry: boolean;
  isViewer: boolean;
  hasRole: (...roles: UserRole[]) => boolean;
  login: (username: string, password: string) => Promise<AdminSession>;
  logout: () => Promise<void>;
  refreshSession: () => Promise<AdminSession | null>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode; initialSession?: AdminSession | null }> = ({
  children,
  initialSession,
}) => {
  const [user, setUser] = useState<AdminSession | null>(initialSession ?? null);
  const [isLoading, setIsLoading] = useState(!initialSession);

  const refreshSession = async (): Promise<AdminSession | null> => {
    try {
      const session = await api.getAdminSession();
      if (session.authenticated) {
        setUser(session);
        return session;
      }
      setUser(null);
      return null;
    } catch {
      setUser(null);
      return null;
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (!initialSession) {
      refreshSession();
    }
  }, [initialSession]);

  const login = async (username: string, password: string): Promise<AdminSession> => {
    const session = await api.loginAdmin(username, password);
    if (session.authenticated) {
      setUser(session);
    }
    return session;
  };

  const logout = async (): Promise<void> => {
    try {
      await api.logoutAdmin();
    } finally {
      setUser(null);
    }
  };

  const currentRole = (user?.role as UserRole) || null;
  const isAdmin = currentRole === 'ADMIN';
  const isReviewer = currentRole === 'REVIEWER';
  const isDataEntry = currentRole === 'DATA_ENTRY';
  const isViewer = currentRole === 'VIEWER';

  const hasRole = (...roles: UserRole[]): boolean => {
    if (!currentRole) return false;
    return roles.includes(currentRole);
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user?.authenticated,
        isLoading,
        role: currentRole,
        isAdmin,
        isReviewer,
        isDataEntry,
        isViewer,
        hasRole,
        login,
        logout,
        refreshSession,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

const defaultAuthContext: AuthContextType = {
  user: null,
  isAuthenticated: false,
  isLoading: false,
  role: null,
  isAdmin: false,
  isReviewer: false,
  isDataEntry: false,
  isViewer: false,
  hasRole: () => false,
  login: async () => ({ authenticated: false }),
  logout: async () => {},
  refreshSession: async () => null,
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  return context ?? defaultAuthContext;
};