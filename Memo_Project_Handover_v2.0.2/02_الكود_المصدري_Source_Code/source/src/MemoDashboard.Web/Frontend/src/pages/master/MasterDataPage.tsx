import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { Modal } from '../../components/common/Modal';
import type {
  CompanyModel,
  FactoryModel,
  ProductionLineModel,
  NoteTypeModel,
  NoteTypeFieldModel,
  NoteTypeFieldDefinitionModel,
} from '../../types/memo';

type MasterTab = 'companies' | 'factories' | 'lines' | 'types';

interface ImpactPreviewState {
  isOpen: boolean;
  definition: NoteTypeFieldDefinitionModel | null;
  affectedNoteCount: number;
  existingValueCount: number;
  missingOrUnreviewedCount: number;
  isNewField: boolean;
}

export const MasterDataPage: React.FC = () => {
  const { hasRole } = useAuth();
  const canEdit = hasRole('ADMIN');

  const [activeTab, setActiveTab] = useState<MasterTab>('companies');
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [actionMessage, setActionMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);

  // Entities
  const [companies, setCompanies] = useState<CompanyModel[]>([]);
  const [factories, setFactories] = useState<FactoryModel[]>([]);
  const [lines, setLines] = useState<ProductionLineModel[]>([]);
  const [types, setTypes] = useState<NoteTypeModel[]>([]);

  // Search & Filters
  const [searchCompany, setSearchCompany] = useState('');
  const [searchFactory, setSearchFactory] = useState('');
  const [factoryCompanyFilter, setFactoryCompanyFilter] = useState('');

  const [searchLine, setSearchLine] = useState('');
  const [lineCompanyFilter, setLineCompanyFilter] = useState('');
  const [lineFactoryFilter, setLineFactoryFilter] = useState('');

  // Note Types & Dynamic Fields Master-Detail
  const [selectedNoteType, setSelectedNoteType] = useState<NoteTypeModel | null>(null);
  const [selectedFields, setSelectedFields] = useState<NoteTypeFieldModel[]>([]);
  const [loadingFields, setLoadingFields] = useState(false);

  // Modals state
  const [companyModal, setCompanyModal] = useState<{ isOpen: boolean; id: string | null; name: string; isActive: boolean }>({
    isOpen: false,
    id: null,
    name: '',
    isActive: true,
  });

  const [factoryModal, setFactoryModal] = useState<{ isOpen: boolean; id: string | null; name: string; companyId: string; isActive: boolean }>({
    isOpen: false,
    id: null,
    name: '',
    companyId: '',
    isActive: true,
  });

  const [lineModal, setLineModal] = useState<{
    isOpen: boolean;
    id: string | null;
    companyId: string;
    factoryId: string;
    name: string;
    isActive: boolean;
  }>({
    isOpen: false,
    id: null,
    companyId: '',
    factoryId: '',
    name: '',
    isActive: true,
  });

  // Dynamic Field Definition Modal
  const [fieldModal, setFieldModal] = useState<{
    isOpen: boolean;
    id: string | null;
    noteTypeId: string;
    fieldId?: string | null;
    fieldCode: string;
    fieldNameAr: string;
    sortOrder: number;
    isRequired: boolean;
    isActive: boolean;
    isNew: boolean;
  }>({
    isOpen: false,
    id: null,
    noteTypeId: '',
    fieldId: null,
    fieldCode: '',
    fieldNameAr: '',
    sortOrder: 1,
    isRequired: true,
    isActive: true,
    isNew: true,
  });

  // Impact Preview State
  const [impactPreview, setImpactPreview] = useState<ImpactPreviewState>({
    isOpen: false,
    definition: null,
    affectedNoteCount: 0,
    existingValueCount: 0,
    missingOrUnreviewedCount: 0,
    isNewField: false,
  });

  const showNotification = (text: string, type: 'success' | 'error' = 'success') => {
    setActionMessage({ text, type });
    setTimeout(() => setActionMessage(null), 4000);
  };

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [compRes, factRes, lineRes, typeRes] = await Promise.all([
        api.getCompanies(),
        api.getFactories(),
        api.getProductionLines(),
        api.getNoteTypes(),
      ]);
      setCompanies(compRes);
      setFactories(factRes);
      setLines(lineRes);
      setTypes(typeRes);

      if (typeRes.length > 0 && !selectedNoteType) {
        setSelectedNoteType(typeRes[0]);
      }
    } catch (err) {
      console.error('Failed to load master data', err);
      showNotification('فشل تحميل البيانات الأساسية من الخادم.', 'error');
    } finally {
      setLoading(false);
    }
  }, [selectedNoteType]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Load indicator fields when selected note type changes
  const loadFieldsForNoteType = useCallback(async (noteTypeId: string) => {
    setLoadingFields(true);
    try {
      const defs = await api.getFieldDefinitions(noteTypeId);
      setSelectedFields(defs || []);
    } catch (err) {
      console.error('Failed to load fields', err);
      setSelectedFields([]);
    } finally {
      setLoadingFields(false);
    }
  }, []);

  useEffect(() => {
    if (selectedNoteType) {
      loadFieldsForNoteType(selectedNoteType.id);
    }
  }, [selectedNoteType, loadFieldsForNoteType]);

  // ==================== Companies Handlers ====================
  const handleSaveCompany = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyModal.name.trim()) return;
    setSubmitting(true);
    try {
      if (companyModal.id) {
        await api.updateCompany(companyModal.id, {
          name: companyModal.name.trim(),
          isActive: companyModal.isActive,
        });
        showNotification('تم تحديث بيانات الشركة بنجاح.');
      } else {
        await api.createCompany({
          name: companyModal.name.trim(),
          isActive: companyModal.isActive,
        });
        showNotification('تمت إضافة الشركة الجديدة بنجاح.');
      }
      setCompanyModal({ isOpen: false, id: null, name: '', isActive: true });
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حفظ بيانات الشركة.', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleCompany = async (company: CompanyModel) => {
    if (!canEdit) return;
    try {
      await api.updateCompany(company.id, {
        ...company,
        isActive: !company.isActive,
      });
      showNotification(company.isActive ? 'تم تعطيل الشركة.' : 'تم تفعيل الشركة.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل تعديل حالة الشركة.', 'error');
    }
  };

  const handleDeleteCompany = async (company: CompanyModel) => {
    if (!canEdit) return;
    const confirm = window.confirm(
      `هل أنت متأكد من حذف الشركة «${company.name}»؟\nسيتم التحقق من عدم وجود مذكرات أو سجلات مرتبطة بها.`
    );
    if (!confirm) return;

    try {
      await api.deleteCompany(company.id);
      showNotification('تم حذف الشركة بنجاح.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حذف الشركة. تأكد من عدم وجود مذكرات مرتبطة بها.', 'error');
    }
  };

  // ==================== Factories Handlers ====================
  const handleSaveFactory = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!factoryModal.name.trim() || !factoryModal.companyId) return;
    setSubmitting(true);
    try {
      if (factoryModal.id) {
        await api.updateFactory(factoryModal.id, {
          name: factoryModal.name.trim(),
          companyId: factoryModal.companyId,
          isActive: factoryModal.isActive,
        });
        showNotification('تم تحديث بيانات المعمل بنجاح.');
      } else {
        await api.createFactory({
          name: factoryModal.name.trim(),
          companyId: factoryModal.companyId,
          isActive: factoryModal.isActive,
        });
        showNotification('تمت إضافة المعمل الجديد بنجاح.');
      }
      setFactoryModal({ isOpen: false, id: null, name: '', companyId: '', isActive: true });
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حفظ بيانات المعمل.', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleFactory = async (factory: FactoryModel) => {
    if (!canEdit) return;
    try {
      await api.updateFactory(factory.id, {
        ...factory,
        isActive: !factory.isActive,
      });
      showNotification(factory.isActive ? 'تم تعطيل المعمل.' : 'تم تفعيل المعمل.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل تعديل حالة المعمل.', 'error');
    }
  };

  const handleDeleteFactory = async (factory: FactoryModel) => {
    if (!canEdit) return;
    const confirm = window.confirm(`هل أنت متأكد من حذف المعمل «${factory.name}»؟`);
    if (!confirm) return;

    try {
      await api.deleteFactory(factory.id);
      showNotification('تم حذف المعمل بنجاح.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حذف المعمل. قد يكون مرتبطاً بخطوط إنتاج أو مذكرات.', 'error');
    }
  };

  // ==================== Production Lines Handlers ====================
  const handleOpenAddLine = () => {
    const firstCompany = lineCompanyFilter || companies[0]?.id || '';
    const compFactories = firstCompany
      ? factories.filter((f) => f.companyId === firstCompany && f.isActive)
      : factories.filter((f) => f.isActive);
    const firstFact = lineFactoryFilter || compFactories[0]?.id || (factories.find((f) => f.isActive)?.id || '');
    const resolvedComp = firstCompany || (factories.find((f) => f.id === firstFact)?.companyId || '');

    setLineModal({
      isOpen: true,
      id: null,
      companyId: resolvedComp,
      factoryId: firstFact,
      name: '',
      isActive: true,
    });
  };

  const handleOpenEditLine = (line: ProductionLineModel) => {
    setLineModal({
      isOpen: true,
      id: line.id,
      companyId: line.companyId || '',
      factoryId: line.factoryId,
      name: line.name,
      isActive: line.isActive,
    });
  };

  const handleSaveLine = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!lineModal.name.trim() || !lineModal.factoryId) return;
    setSubmitting(true);
    try {
      if (lineModal.id) {
        await api.updateProductionLine(lineModal.id, {
          name: lineModal.name.trim(),
          factoryId: lineModal.factoryId,
          isActive: lineModal.isActive,
        });
        showNotification('تم تحديث خط الإنتاج بنجاح.');
      } else {
        await api.createProductionLine({
          name: lineModal.name.trim(),
          factoryId: lineModal.factoryId,
          isActive: lineModal.isActive,
        });
        showNotification('تمت إضافة خط الإنتاج بنجاح.');
      }
      setLineModal({ isOpen: false, id: null, companyId: '', factoryId: '', name: '', isActive: true });
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حفظ بيانات خط الإنتاج.', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleLine = async (line: ProductionLineModel) => {
    if (!canEdit) return;
    try {
      await api.toggleProductionLine(line.id, !line.isActive);
      showNotification(line.isActive ? 'تم تعطيل خط الإنتاج.' : 'تم تفعيل خط الإنتاج.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل تعديل حالة خط الإنتاج.', 'error');
    }
  };

  const handleDeleteLine = async (line: ProductionLineModel) => {
    if (!canEdit) return;
    const confirm = window.confirm(`هل أنت متأكد من حذف خط الإنتاج «${line.name}»؟`);
    if (!confirm) return;

    try {
      await api.deleteProductionLine(line.id);
      showNotification('تم حذف خط الإنتاج بنجاح.');
      await loadData();
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حذف خط الإنتاج. قد يكون مرتبطاً بمذكرات مسجلة.', 'error');
    }
  };

  // ==================== Dynamic Indicators Handlers ====================
  const handleOpenAddField = () => {
    if (!selectedNoteType) return;
    const maxSort = selectedFields.length > 0 ? Math.max(...selectedFields.map((f) => f.sortOrder)) : 0;
    setFieldModal({
      isOpen: true,
      id: null,
      noteTypeId: selectedNoteType.id,
      fieldId: null,
      fieldCode: '',
      fieldNameAr: '',
      sortOrder: maxSort + 1,
      isRequired: true,
      isActive: true,
      isNew: true,
    });
  };

  const handleOpenEditField = (field: NoteTypeFieldModel) => {
    if (!selectedNoteType) return;
    setFieldModal({
      isOpen: true,
      id: field.id,
      noteTypeId: selectedNoteType.id,
      fieldId: field.fieldId,
      fieldCode: field.fieldCode,
      fieldNameAr: field.fieldNameAr,
      sortOrder: field.sortOrder,
      isRequired: field.isRequired,
      isActive: field.isActive,
      isNew: false,
    });
  };

  // Step 1 of saving a field: Request Impact Preview matching FrmNoteTypes.cs
  const handleRequestFieldPreview = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!fieldModal.fieldCode.trim() || !fieldModal.fieldNameAr.trim()) return;

    const defPayload: NoteTypeFieldDefinitionModel = {
      id: fieldModal.id || '00000000-0000-0000-0000-000000000000',
      noteTypeId: fieldModal.noteTypeId,
      fieldId: fieldModal.fieldId,
      fieldCode: fieldModal.fieldCode.trim().toUpperCase(),
      fieldNameAr: fieldModal.fieldNameAr.trim(),
      sortOrder: Number(fieldModal.sortOrder) || 1,
      isRequired: fieldModal.isRequired,
      isActive: fieldModal.isActive,
    };

    setSubmitting(true);
    try {
      const previewRes = await api.previewFieldDefinition(defPayload);
      setImpactPreview({
        isOpen: true,
        definition: defPayload,
        affectedNoteCount: Number(previewRes.affectedNoteCount) || 0,
        existingValueCount: Number(previewRes.existingValueCount) || 0,
        missingOrUnreviewedCount: Number(previewRes.missingOrUnreviewedCount) || 0,
        isNewField: fieldModal.isNew,
      });
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل استخراج معاينة أثر تعديل البند.', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  // Step 2: Confirm impact and commit save
  const handleConfirmSaveField = async () => {
    if (!impactPreview.definition) return;
    setSubmitting(true);
    try {
      await api.saveFieldDefinition(impactPreview.definition);
      showNotification(impactPreview.isNewField ? 'تمت إضافة بند الفحص بنجاح.' : 'تم تحديث بند الفحص بنجاح.');
      setImpactPreview({ isOpen: false, definition: null, affectedNoteCount: 0, existingValueCount: 0, missingOrUnreviewedCount: 0, isNewField: false });
      setFieldModal({ ...fieldModal, isOpen: false });
      if (selectedNoteType) {
        await loadFieldsForNoteType(selectedNoteType.id);
      }
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل حفظ تعريف بند الفحص.', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDeactivateField = async (field: NoteTypeFieldModel) => {
    if (!canEdit) return;
    const confirm = window.confirm(
      `سيتم تعطيل بند الفحص «${field.fieldNameAr}» دون حذف القيم التاريخية المسجلة مسبقاً بالمذكرات.\nهل تريد المتابعة؟`
    );
    if (!confirm) return;

    try {
      await api.deleteFieldDefinition(field.id);
      showNotification('تم تعطيل بند الفحص بنجاح.');
      if (selectedNoteType) {
        await loadFieldsForNoteType(selectedNoteType.id);
      }
    } catch (err: unknown) {
      showNotification(err instanceof Error ? err.message : 'فشل تعطيل بند الفحص.', 'error');
    }
  };

  // ==================== Filtered Views ====================
  const filteredCompanies = useMemo(() => {
    return companies.filter((c) => c.name.toLowerCase().includes(searchCompany.trim().toLowerCase()));
  }, [companies, searchCompany]);

  const filteredFactories = useMemo(() => {
    return factories.filter((f) => {
      const matchComp = !factoryCompanyFilter || f.companyId === factoryCompanyFilter;
      const matchSearch = f.name.toLowerCase().includes(searchFactory.trim().toLowerCase());
      return matchComp && matchSearch;
    });
  }, [factories, factoryCompanyFilter, searchFactory]);

  const filteredLines = useMemo(() => {
    return lines.filter((l) => {
      const matchComp = !lineCompanyFilter || l.companyId === lineCompanyFilter;
      const matchFact = !lineFactoryFilter || l.factoryId === lineFactoryFilter;
      const matchSearch = l.name.toLowerCase().includes(searchLine.trim().toLowerCase());
      return matchComp && matchFact && matchSearch;
    });
  }, [lines, lineCompanyFilter, lineFactoryFilter, searchLine]);

  // Modal factories for cascading dropdown
  const lineModalFactories = useMemo(() => {
    if (!lineModal.companyId) return factories;
    return factories.filter((f) => f.companyId === lineModal.companyId && f.isActive);
  }, [factories, lineModal.companyId]);

  return (
    <div className="flex flex-col gap-6 p-6 min-h-screen app-bg max-w-[1700px] mx-auto" dir="rtl">
      {/* Toast / Action Notification */}
      {actionMessage && (
        <div
          className={`fixed bottom-6 left-6 z-50 px-5 py-3 rounded-xl shadow-lg font-semibold text-sm flex items-center gap-3 transition-all ${
            actionMessage.type === 'success'
              ? 'bg-emerald-600 text-white'
              : 'bg-rose-600 text-white'
          }`}
        >
          <span>{actionMessage.type === 'success' ? '✓' : '⚠️'}</span>
          <span>{actionMessage.text}</span>
        </div>
      )}

      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4 border-b app-border pb-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="text-xl">🏢</span>
            <h1 className="text-2xl font-bold app-text">إدارة البيانات الأساسية</h1>
          </div>
          <p className="text-xs app-text-muted mt-1">
            هيكلة وتصنيف المنشآت الصناعية (الشركات، المعامل، الخطوط) وبنود الفحص والمؤشرات التفتيشية المعتمدة.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={loadData}
            disabled={loading}
            className="px-4 py-2 text-xs font-semibold rounded-lg border app-border app-surface-muted app-hover app-text transition-colors flex items-center gap-2"
          >
            <span>🔄</span>
            <span>{loading ? 'جارٍ التحديث...' : 'تحديث البيانات'}</span>
          </button>
        </div>
      </div>

      {/* Navigation Tabs */}
      <div className="flex items-center gap-2 border-b app-border pb-1 overflow-x-auto">
        <button
          type="button"
          onClick={() => setActiveTab('companies')}
          className={`px-4 py-2.5 text-xs font-bold rounded-t-xl transition-all border-b-2 flex items-center gap-2 ${
            activeTab === 'companies'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          <span>🏢</span>
          <span>الشركات</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-slate-200 dark:bg-slate-700 app-text">
            {companies.length}
          </span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('factories')}
          className={`px-4 py-2.5 text-xs font-bold rounded-t-xl transition-all border-b-2 flex items-center gap-2 ${
            activeTab === 'factories'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          <span>🏭</span>
          <span>المعامل والمصانع</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-slate-200 dark:bg-slate-700 app-text">
            {factories.length}
          </span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('lines')}
          className={`px-4 py-2.5 text-xs font-bold rounded-t-xl transition-all border-b-2 flex items-center gap-2 ${
            activeTab === 'lines'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          <span>⚙️</span>
          <span>خطوط الإنتاج</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-slate-200 dark:bg-slate-700 app-text">
            {lines.length}
          </span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('types')}
          className={`px-4 py-2.5 text-xs font-bold rounded-t-xl transition-all border-b-2 flex items-center gap-2 ${
            activeTab === 'types'
              ? 'border-blue-600 text-blue-600 dark:text-blue-400 bg-blue-50/50 dark:bg-blue-950/20'
              : 'border-transparent app-text-muted hover:text-slate-900 dark:hover:text-slate-200'
          }`}
        >
          <span>📋</span>
          <span>أنواع المذكرات وبنود الفحص</span>
          <span className="px-1.5 py-0.5 rounded-full text-[10px] bg-slate-200 dark:bg-slate-700 app-text">
            {types.length}
          </span>
        </button>
      </div>

      {/* ========================================================= */}
      {/* Tab 1: Companies (الشركات) */}
      {/* ========================================================= */}
      {activeTab === 'companies' && (
        <div className="space-y-4">
          <div className="flex flex-wrap justify-between items-center gap-3">
            <div className="flex items-center gap-3 flex-1 max-w-md">
              <input
                type="text"
                placeholder="بحث فوري باسم الشركة..."
                value={searchCompany}
                onChange={(e) => setSearchCompany(e.target.value)}
                className="w-full px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
              />
              {searchCompany && (
                <button
                  type="button"
                  onClick={() => setSearchCompany('')}
                  className="text-xs text-slate-400 hover:text-slate-600"
                >
                  مسح
                </button>
              )}
            </div>
            {canEdit && (
              <button
                type="button"
                onClick={() => setCompanyModal({ isOpen: true, id: null, name: '', isActive: true })}
                className="btn-primary px-4 py-2 text-xs font-bold rounded-xl flex items-center gap-1.5 shadow-sm"
              >
                <span>+</span>
                <span>إضافة شركة جديدة</span>
              </button>
            )}
          </div>

          <div className="app-surface rounded-2xl border app-border overflow-hidden shadow-sm">
            <table className="w-full text-right text-xs border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 font-semibold app-text-muted">
                  <th className="p-3.5">اسم الشركة</th>
                  <th className="p-3.5 text-center">المعامل التابعة</th>
                  <th className="p-3.5 text-center">الحالة</th>
                  <th className="p-3.5 text-center">الإجراءات والتحكم</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {filteredCompanies.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="p-8 text-center text-xs app-text-muted">
                      لا توجد شركات مطابقة لمعايير البحث.
                    </td>
                  </tr>
                ) : (
                  filteredCompanies.map((c) => {
                    const compFactoriesCount = factories.filter((f) => f.companyId === c.id).length;
                    return (
                      <tr key={c.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                        <td className="p-3.5 font-bold app-text text-sm">{c.name}</td>
                        <td className="p-3.5 text-center">
                          <span className="px-2 py-0.5 rounded-full text-xs font-mono bg-blue-50 dark:bg-blue-950 text-blue-700 dark:text-blue-300 font-semibold">
                            {compFactoriesCount} معمل
                          </span>
                        </td>
                        <td className="p-3.5 text-center">
                          <button
                            type="button"
                            onClick={() => handleToggleCompany(c)}
                            title={canEdit ? 'انقر لتبديل الحالة' : undefined}
                            disabled={!canEdit}
                            className={`px-2.5 py-1 text-xs rounded-full font-semibold transition-all ${
                              c.isActive
                                ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300 hover:opacity-80'
                                : 'bg-slate-100 text-slate-500 hover:opacity-80'
                            }`}
                          >
                            {c.isActive ? '● نشط' : '○ معطل'}
                          </button>
                        </td>
                        <td className="p-3.5 text-center">
                          <div className="flex items-center justify-center gap-2">
                            {canEdit && (
                              <>
                                <button
                                  type="button"
                                  onClick={() => setCompanyModal({ isOpen: true, id: c.id, name: c.name, isActive: c.isActive })}
                                  className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text font-semibold"
                                >
                                  تعديل
                                </button>
                                <button
                                  type="button"
                                  onClick={() => handleDeleteCompany(c)}
                                  className="px-2.5 py-1 text-xs rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 text-rose-700 dark:text-rose-300 hover:bg-rose-100 font-semibold"
                                >
                                  حذف
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ========================================================= */}
      {/* Tab 2: Factories (المعامل والمصانع) */}
      {/* ========================================================= */}
      {activeTab === 'factories' && (
        <div className="space-y-4">
          <div className="flex flex-wrap justify-between items-center gap-3">
            <div className="flex flex-wrap items-center gap-3 flex-1">
              <select
                value={factoryCompanyFilter}
                onChange={(e) => setFactoryCompanyFilter(e.target.value)}
                className="px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
              >
                <option value="">-- جميع الشركات ({companies.length}) --</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>

              <input
                type="text"
                placeholder="بحث فوري باسم المعمل..."
                value={searchFactory}
                onChange={(e) => setSearchFactory(e.target.value)}
                className="px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500 min-w-[220px]"
              />
            </div>

            {canEdit && (
              <button
                type="button"
                onClick={() =>
                  setFactoryModal({
                    isOpen: true,
                    id: null,
                    name: '',
                    companyId: factoryCompanyFilter || companies[0]?.id || '',
                    isActive: true,
                  })
                }
                className="btn-primary px-4 py-2 text-xs font-bold rounded-xl flex items-center gap-1.5 shadow-sm"
              >
                <span>+</span>
                <span>إضافة معمل جديد</span>
              </button>
            )}
          </div>

          <div className="app-surface rounded-2xl border app-border overflow-hidden shadow-sm">
            <table className="w-full text-right text-xs border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 font-semibold app-text-muted">
                  <th className="p-3.5">اسم المعمل</th>
                  <th className="p-3.5">الشركة التابع لها</th>
                  <th className="p-3.5 text-center">خطوط الإنتاج</th>
                  <th className="p-3.5 text-center">الحالة</th>
                  <th className="p-3.5 text-center">الإجراءات والتحكم</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {filteredFactories.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="p-8 text-center text-xs app-text-muted">
                      لا توجد معامل مطابقة لمعايير البحث.
                    </td>
                  </tr>
                ) : (
                  filteredFactories.map((f) => {
                    const factLinesCount = lines.filter((l) => l.factoryId === f.id).length;
                    return (
                      <tr key={f.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                        <td className="p-3.5 font-bold app-text text-sm">{f.name}</td>
                        <td className="p-3.5 text-xs text-blue-600 dark:text-blue-400 font-semibold">
                          {f.companyName || '—'}
                        </td>
                        <td className="p-3.5 text-center">
                          <span className="px-2 py-0.5 rounded-full text-xs font-mono bg-purple-50 dark:bg-purple-950 text-purple-700 dark:text-purple-300 font-semibold">
                            {factLinesCount} خط
                          </span>
                        </td>
                        <td className="p-3.5 text-center">
                          <button
                            type="button"
                            onClick={() => handleToggleFactory(f)}
                            title={canEdit ? 'انقر لتبديل الحالة' : undefined}
                            disabled={!canEdit}
                            className={`px-2.5 py-1 text-xs rounded-full font-semibold transition-all ${
                              f.isActive
                                ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300 hover:opacity-80'
                                : 'bg-slate-100 text-slate-500 hover:opacity-80'
                            }`}
                          >
                            {f.isActive ? '● نشط' : '○ معطل'}
                          </button>
                        </td>
                        <td className="p-3.5 text-center">
                          <div className="flex items-center justify-center gap-2">
                            {canEdit && (
                              <>
                                <button
                                  type="button"
                                  onClick={() =>
                                    setFactoryModal({
                                      isOpen: true,
                                      id: f.id,
                                      name: f.name,
                                      companyId: f.companyId || '',
                                      isActive: f.isActive,
                                    })
                                  }
                                  className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text font-semibold"
                                >
                                  تعديل
                                </button>
                                <button
                                  type="button"
                                  onClick={() => handleDeleteFactory(f)}
                                  className="px-2.5 py-1 text-xs rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 text-rose-700 dark:text-rose-300 hover:bg-rose-100 font-semibold"
                                >
                                  حذف
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ========================================================= */}
      {/* Tab 3: Production Lines (خطوط الإنتاج) */}
      {/* ========================================================= */}
      {activeTab === 'lines' && (
        <div className="space-y-4">
          <div className="flex flex-wrap justify-between items-center gap-3">
            <div className="flex flex-wrap items-center gap-3 flex-1">
              <select
                value={lineCompanyFilter}
                onChange={(e) => {
                  setLineCompanyFilter(e.target.value);
                  setLineFactoryFilter('');
                }}
                className="px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
              >
                <option value="">-- جميع الشركات ({companies.length}) --</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>

              <select
                value={lineFactoryFilter}
                onChange={(e) => setLineFactoryFilter(e.target.value)}
                className="px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
              >
                <option value="">-- جميع المعامل --</option>
                {factories
                  .filter((f) => !lineCompanyFilter || f.companyId === lineCompanyFilter)
                  .map((f) => (
                    <option key={f.id} value={f.id}>{f.name} ({f.companyName})</option>
                  ))}
              </select>

              <input
                type="text"
                placeholder="بحث فوري باسم خط الإنتاج..."
                value={searchLine}
                onChange={(e) => setSearchLine(e.target.value)}
                className="px-3 py-2 text-xs rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500 min-w-[200px]"
              />
            </div>

            {canEdit && (
              <button
                type="button"
                onClick={handleOpenAddLine}
                className="btn-primary px-4 py-2 text-xs font-bold rounded-xl flex items-center gap-1.5 shadow-sm"
              >
                <span>+</span>
                <span>إضافة خط إنتاج</span>
              </button>
            )}
          </div>

          <div className="app-surface rounded-2xl border app-border overflow-hidden shadow-sm">
            <table className="w-full text-right text-xs border-collapse">
              <thead>
                <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 font-semibold app-text-muted">
                  <th className="p-3.5">اسم خط الإنتاج</th>
                  <th className="p-3.5">المعمل</th>
                  <th className="p-3.5">الشركة التابع لها</th>
                  <th className="p-3.5 text-center">الحالة</th>
                  <th className="p-3.5 text-center">الإجراءات والتحكم</th>
                </tr>
              </thead>
              <tbody className="divide-y app-border">
                {filteredLines.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="p-8 text-center text-xs app-text-muted">
                      لا توجد خطوط إنتاج مطابقة لمعايير البحث.
                    </td>
                  </tr>
                ) : (
                  filteredLines.map((l) => (
                    <tr key={l.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                      <td className="p-3.5 font-bold app-text text-sm">{l.name}</td>
                      <td className="p-3.5 text-xs text-purple-700 dark:text-purple-300 font-semibold">
                        {l.factoryName || '—'}
                      </td>
                      <td className="p-3.5 text-xs text-blue-600 dark:text-blue-400 font-semibold">
                        {l.companyName || '—'}
                      </td>
                      <td className="p-3.5 text-center">
                        <button
                          type="button"
                          onClick={() => handleToggleLine(l)}
                          title={canEdit ? 'انقر لتبديل الحالة' : undefined}
                          disabled={!canEdit}
                          className={`px-2.5 py-1 text-xs rounded-full font-semibold transition-all ${
                            l.isActive
                              ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300 hover:opacity-80'
                              : 'bg-slate-100 text-slate-500 hover:opacity-80'
                          }`}
                        >
                          {l.isActive ? '● نشط' : '○ معطل'}
                        </button>
                      </td>
                      <td className="p-3.5 text-center">
                        <div className="flex items-center justify-center gap-2">
                          {canEdit && (
                            <>
                              <button
                                type="button"
                                onClick={() => handleOpenEditLine(l)}
                                className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text font-semibold"
                              >
                                تعديل
                              </button>
                              <button
                                type="button"
                                onClick={() => handleDeleteLine(l)}
                                className="px-2.5 py-1 text-xs rounded-lg border border-rose-200 dark:border-rose-900 bg-rose-50 dark:bg-rose-950/40 text-rose-700 dark:text-rose-300 hover:bg-rose-100 font-semibold"
                              >
                                حذف
                              </button>
                            </>
                          )}
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

      {/* ========================================================= */}
      {/* Tab 4: Note Types & Dynamic Fields (أنواع المذكرات وبنود الفحص) */}
      {/* Master-Detail layout directly matching FrmNoteTypes.cs */}
      {/* ========================================================= */}
      {activeTab === 'types' && (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
          {/* Right Column: Note Types List */}
          <div className="lg:col-span-4 app-surface rounded-2xl border app-border p-4 shadow-sm space-y-3">
            <div className="flex items-center justify-between border-b app-border pb-2.5">
              <h2 className="text-xs font-bold app-text flex items-center gap-2">
                <span>📑</span>
                <span>أنواع مذكرات التفتيش ({types.length})</span>
              </h2>
            </div>
            <p className="text-[11px] app-text-muted">
              اختر نوع المذكرة من القائمة أدناه لإدارة وضبط قائمة بنود ومؤشرات الفحص المعتمدة له:
            </p>

            <div className="divide-y app-border max-h-[600px] overflow-y-auto">
              {types.map((nt) => {
                const isSelected = selectedNoteType?.id === nt.id;
                return (
                  <div
                    key={nt.id}
                    onClick={() => setSelectedNoteType(nt)}
                    className={`p-3 rounded-xl cursor-pointer transition-all flex items-center justify-between ${
                      isSelected
                        ? 'bg-blue-50 dark:bg-blue-950/50 border border-blue-300 dark:border-blue-800 text-blue-900 dark:text-blue-200 shadow-sm'
                        : 'hover:bg-slate-50 dark:hover:bg-slate-800/40 app-text'
                    }`}
                  >
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-[10px] font-bold px-1.5 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300">
                          {nt.code}
                        </span>
                        <span className="font-bold text-xs">{nt.nameAr}</span>
                      </div>
                      {nt.description && <p className="text-[11px] text-slate-400 mt-1 line-clamp-1">{nt.description}</p>}
                    </div>
                    <span className="text-sm">{isSelected ? '👈' : '←'}</span>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Left Column: Dynamic Fields Grid for Selected Note Type */}
          <div className="lg:col-span-8 app-surface rounded-2xl border app-border p-5 shadow-sm space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b app-border pb-3">
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-bold app-text">بنود ومؤشرات:</span>
                  <span className="text-sm font-bold text-blue-600 dark:text-blue-400">{selectedNoteType?.nameAr || '—'}</span>
                  <span className="font-mono text-xs px-2 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-300 font-semibold">
                    {selectedNoteType?.code}
                  </span>
                </div>
                <p className="text-xs app-text-muted mt-1">
                  البنود المسجلة تظهر ديناميكياً أثناء إنشاء وتدقيق مذكرات هذا النوع (عدد البنود: {selectedFields.length}).
                </p>
              </div>

              {canEdit && (
                <button
                  type="button"
                  onClick={handleOpenAddField}
                  disabled={!selectedNoteType}
                  className="btn-primary px-4 py-2 text-xs font-bold rounded-xl flex items-center gap-1.5 shadow-sm disabled:opacity-50"
                >
                  <span>+</span>
                  <span>إضافة بند فحص جديد</span>
                </button>
              )}
            </div>

            {loadingFields ? (
              <div className="p-12 text-center app-text-muted text-xs">جارٍ جلب بنود الفحص...</div>
            ) : selectedFields.length === 0 ? (
              <div className="p-12 text-center app-text-muted text-xs">
                لا توجد بنود فحص مخصصة لهذا النوع حتى الآن. يمكنك إضافة بنود جديدة بالضغط على «إضافة بند فحص جديد».
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-right text-xs border-collapse">
                  <thead>
                    <tr className="border-b app-border bg-slate-50/60 dark:bg-slate-900/60 font-semibold app-text-muted">
                      <th className="p-3 w-12 text-center">الترتيب</th>
                      <th className="p-3">رمز الحقل</th>
                      <th className="p-3">اسم بند الفحص بالعربية</th>
                      <th className="p-3 text-center">إلزامي</th>
                      <th className="p-3 text-center">الحالة</th>
                      <th className="p-3 text-center">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y app-border">
                    {selectedFields.map((fld) => (
                      <tr key={fld.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/40 transition-colors">
                        <td className="p-3 text-center font-mono font-bold">{fld.sortOrder}</td>
                        <td className="p-3 font-mono font-bold text-blue-600 dark:text-blue-400">{fld.fieldCode}</td>
                        <td className="p-3 font-semibold app-text">{fld.fieldNameAr}</td>
                        <td className="p-3 text-center">
                          <span
                            className={`px-2 py-0.5 rounded-full text-[11px] font-semibold ${
                              fld.isRequired
                                ? 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300'
                                : 'bg-slate-100 text-slate-500'
                            }`}
                          >
                            {fld.isRequired ? 'إلزامي' : 'اختياري'}
                          </span>
                        </td>
                        <td className="p-3 text-center">
                          <span
                            className={`px-2 py-0.5 rounded-full text-[11px] font-semibold ${
                              fld.isActive
                                ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300'
                                : 'bg-slate-100 text-slate-500'
                            }`}
                          >
                            {fld.isActive ? 'نشط' : 'معطل'}
                          </span>
                        </td>
                        <td className="p-3 text-center">
                          <div className="flex items-center justify-center gap-2">
                            {canEdit && (
                              <>
                                <button
                                  type="button"
                                  onClick={() => handleOpenEditField(fld)}
                                  className="px-2.5 py-1 text-xs rounded-lg border app-border app-surface-muted app-hover app-text font-semibold"
                                >
                                  تعديل
                                </button>
                                {fld.isActive && (
                                  <button
                                    type="button"
                                    onClick={() => handleDeactivateField(fld)}
                                    title="تعطيل البند بأمان دون حذف القيم التاريخية"
                                    className="px-2.5 py-1 text-xs rounded-lg border border-amber-200 dark:border-amber-900 bg-amber-50 dark:bg-amber-950/40 text-amber-700 dark:text-amber-300 hover:bg-amber-100 font-semibold"
                                  >
                                    تعطيل
                                  </button>
                                )}
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ========================================================= */}
      {/* Modal: Company Add/Edit */}
      {/* ========================================================= */}
      <Modal
        isOpen={companyModal.isOpen}
        onClose={() => setCompanyModal({ ...companyModal, isOpen: false })}
        title={companyModal.id ? 'تعديل بيانات الشركة' : 'إضافة شركة جديدة'}
      >
        <form onSubmit={handleSaveCompany} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">اسم الشركة *</label>
            <input
              type="text"
              required
              maxLength={200}
              value={companyModal.name}
              onChange={(e) => setCompanyModal({ ...companyModal, name: e.target.value })}
              placeholder="مثال: الشركة العامة للزيوت"
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div className="flex items-center gap-2 pt-2">
            <input
              type="checkbox"
              id="compActive"
              checked={companyModal.isActive}
              onChange={(e) => setCompanyModal({ ...companyModal, isActive: e.target.checked })}
              className="rounded border-slate-300 h-4 w-4 text-blue-600"
            />
            <label htmlFor="compActive" className="text-xs font-semibold app-text cursor-pointer">
              حالة الشركة نشطة
            </label>
          </div>
          <div className="flex justify-end gap-3 pt-4 border-t app-border">
            <button
              type="button"
              onClick={() => setCompanyModal({ ...companyModal, isOpen: false })}
              className="px-4 py-2 text-xs font-semibold rounded-xl border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="btn-primary px-5 py-2 text-xs font-bold rounded-xl disabled:opacity-60"
            >
              {submitting ? 'جارٍ الحفظ...' : 'حفظ البيانات'}
            </button>
          </div>
        </form>
      </Modal>

      {/* ========================================================= */}
      {/* Modal: Factory Add/Edit */}
      {/* ========================================================= */}
      <Modal
        isOpen={factoryModal.isOpen}
        onClose={() => setFactoryModal({ ...factoryModal, isOpen: false })}
        title={factoryModal.id ? 'تعديل بيانات المعمل' : 'إضافة معمل جديد'}
      >
        <form onSubmit={handleSaveFactory} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">الشركة التابع لها المعمل *</label>
            <select
              required
              value={factoryModal.companyId}
              onChange={(e) => setFactoryModal({ ...factoryModal, companyId: e.target.value })}
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            >
              <option value="">-- اختر الشركة --</option>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">اسم المعمل / المنشأة *</label>
            <input
              type="text"
              required
              maxLength={200}
              value={factoryModal.name}
              onChange={(e) => setFactoryModal({ ...factoryModal, name: e.target.value })}
              placeholder="مثال: معمل سكر دمشق"
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div className="flex items-center gap-2 pt-2">
            <input
              type="checkbox"
              id="factActive"
              checked={factoryModal.isActive}
              onChange={(e) => setFactoryModal({ ...factoryModal, isActive: e.target.checked })}
              className="rounded border-slate-300 h-4 w-4 text-blue-600"
            />
            <label htmlFor="factActive" className="text-xs font-semibold app-text cursor-pointer">
              حالة المعمل نشط
            </label>
          </div>
          <div className="flex justify-end gap-3 pt-4 border-t app-border">
            <button
              type="button"
              onClick={() => setFactoryModal({ ...factoryModal, isOpen: false })}
              className="px-4 py-2 text-xs font-semibold rounded-xl border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="btn-primary px-5 py-2 text-xs font-bold rounded-xl disabled:opacity-60"
            >
              {submitting ? 'جارٍ الحفظ...' : 'حفظ البيانات'}
            </button>
          </div>
        </form>
      </Modal>

      {/* ========================================================= */}
      {/* Modal: Production Line Add/Edit (with cascading company->factory) */}
      {/* ========================================================= */}
      <Modal
        isOpen={lineModal.isOpen}
        onClose={() => setLineModal({ ...lineModal, isOpen: false })}
        title={lineModal.id ? 'تعديل خط الإنتاج' : 'إضافة خط إنتاج جديد'}
      >
        <form onSubmit={handleSaveLine} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">الشركة *</label>
            <select
              value={lineModal.companyId}
              onChange={(e) => {
                const compId = e.target.value;
                const availableFact = factories.filter((f) => f.companyId === compId && f.isActive);
                setLineModal({
                  ...lineModal,
                  companyId: compId,
                  factoryId: availableFact[0]?.id || '',
                });
              }}
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            >
              <option value="">-- اختر الشركة لتصفية المعامل --</option>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">المعمل التابع له خط الإنتاج *</label>
            <select
              required
              value={lineModal.factoryId}
              onChange={(e) => {
                const factId = e.target.value;
                const fact = factories.find((x) => x.id === factId);
                setLineModal({
                  ...lineModal,
                  factoryId: factId,
                  companyId: fact?.companyId || lineModal.companyId,
                });
              }}
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            >
              <option value="">-- اختر المعمل --</option>
              {lineModalFactories.map((f) => (
                <option key={f.id} value={f.id}>{f.name} ({f.companyName})</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">اسم خط الإنتاج *</label>
            <input
              type="text"
              required
              maxLength={200}
              value={lineModal.name}
              onChange={(e) => setLineModal({ ...lineModal, name: e.target.value })}
              placeholder="مثال: خط تعبئة العبوات 1 لتر"
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>

          <div className="flex items-center gap-2 pt-2">
            <input
              type="checkbox"
              id="lineActive"
              checked={lineModal.isActive}
              onChange={(e) => setLineModal({ ...lineModal, isActive: e.target.checked })}
              className="rounded border-slate-300 h-4 w-4 text-blue-600"
            />
            <label htmlFor="lineActive" className="text-xs font-semibold app-text cursor-pointer">
              حالة خط الإنتاج نشط
            </label>
          </div>

          <div className="flex justify-end gap-3 pt-4 border-t app-border">
            <button
              type="button"
              onClick={() => setLineModal({ ...lineModal, isOpen: false })}
              className="px-4 py-2 text-xs font-semibold rounded-xl border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="btn-primary px-5 py-2 text-xs font-bold rounded-xl disabled:opacity-60"
            >
              {submitting ? 'جارٍ الحفظ...' : 'حفظ البيانات'}
            </button>
          </div>
        </form>
      </Modal>

      {/* ========================================================= */}
      {/* Modal: Add/Edit Dynamic Field Definition (مطابق لـ FrmNoteTypes.cs) */}
      {/* ========================================================= */}
      <Modal
        isOpen={fieldModal.isOpen}
        onClose={() => setFieldModal({ ...fieldModal, isOpen: false })}
        title={fieldModal.isNew ? 'إضافة بند فحص جديد' : 'تعديل بند الفحص'}
      >
        <form onSubmit={handleRequestFieldPreview} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">
              رمز البند (Field Code) *
              {!fieldModal.isNew && <span className="text-[10px] text-amber-600 mr-2">(لا يمكن تعديل الرمز لبند سابق)</span>}
            </label>
            <input
              type="text"
              required
              disabled={!fieldModal.isNew}
              maxLength={50}
              value={fieldModal.fieldCode}
              onChange={(e) => setFieldModal({ ...fieldModal, fieldCode: e.target.value.toUpperCase() })}
              placeholder="مثال: VISUAL_DEFECTS"
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text font-mono uppercase focus:ring-2 focus:ring-blue-500 disabled:opacity-60"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">اسم المؤشر أو البند بالعربية *</label>
            <input
              type="text"
              required
              maxLength={200}
              value={fieldModal.fieldNameAr}
              onChange={(e) => setFieldModal({ ...fieldModal, fieldNameAr: e.target.value })}
              placeholder="مثال: سلامة التغليف الخارجي واللصاقة"
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text focus:ring-2 focus:ring-blue-500"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold app-text-muted mb-1">ترتيب الظهور (Sort Order)</label>
            <input
              type="number"
              min={1}
              max={9999}
              required
              value={fieldModal.sortOrder}
              onChange={(e) => setFieldModal({ ...fieldModal, sortOrder: parseInt(e.target.value, 10) || 1 })}
              className="w-full px-3 py-2 text-sm rounded-xl border app-border app-surface app-text font-mono focus:ring-2 focus:ring-blue-500"
            />
          </div>

          <div className="flex items-center gap-6 pt-2">
            <label className="flex items-center gap-2 text-xs font-semibold app-text cursor-pointer">
              <input
                type="checkbox"
                checked={fieldModal.isRequired}
                onChange={(e) => setFieldModal({ ...fieldModal, isRequired: e.target.checked })}
                className="rounded border-slate-300 h-4 w-4 text-blue-600"
              />
              <span>البند إلزامي عند إدخال المذكرة</span>
            </label>

            <label className="flex items-center gap-2 text-xs font-semibold app-text cursor-pointer">
              <input
                type="checkbox"
                checked={fieldModal.isActive}
                onChange={(e) => setFieldModal({ ...fieldModal, isActive: e.target.checked })}
                className="rounded border-slate-300 h-4 w-4 text-blue-600"
              />
              <span>البند فعال</span>
            </label>
          </div>

          <div className="flex justify-end gap-3 pt-4 border-t app-border">
            <button
              type="button"
              onClick={() => setFieldModal({ ...fieldModal, isOpen: false })}
              className="px-4 py-2 text-xs font-semibold rounded-xl border app-border app-surface-muted app-hover app-text"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="btn-primary px-5 py-2 text-xs font-bold rounded-xl disabled:opacity-60"
            >
              {submitting ? 'جارٍ فحص الأثر...' : 'متابعة ومعاينة الأثر ←'}
            </button>
          </div>
        </form>
      </Modal>

      {/* ========================================================= */}
      {/* Modal: Change Impact Preview (معاينة أثر التغيير التاريخي) */}
      {/* ========================================================= */}
      <Modal
        isOpen={impactPreview.isOpen}
        onClose={() => setImpactPreview({ ...impactPreview, isOpen: false })}
        title="معاينة أثر التغيير على المذكرات التاريخية"
      >
        <div className="space-y-4">
          <div className="p-4 rounded-xl bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900 text-xs space-y-2">
            <p className="font-bold text-amber-900 dark:text-amber-300">
              تنبيه تدقيق: تغيير هيكلية بنود الفحص ينعكس على سجلات المنظومة كاملة.
            </p>
            <p className="text-amber-800 dark:text-amber-200 leading-relaxed">
              سيتم تطبيق هذا البند على جميع المذكرات الحالية والسابقة التابعة لنوع «{selectedNoteType?.nameAr}».
            </p>
          </div>

          <div className="grid grid-cols-2 gap-3 text-xs">
            <div className="p-3 rounded-xl border app-border app-surface-muted">
              <span className="text-slate-500 block mb-1">المذكرات القديمة المتأثرة:</span>
              <span className="text-lg font-bold font-mono text-blue-600 dark:text-blue-400">
                {impactPreview.affectedNoteCount.toLocaleString('ar-SY')} مذكرة
              </span>
            </div>
            <div className="p-3 rounded-xl border app-border app-surface-muted">
              <span className="text-slate-500 block mb-1">القيم المسجلة مسبقاً:</span>
              <span className="text-lg font-bold font-mono text-emerald-600 dark:text-emerald-400">
                {impactPreview.existingValueCount.toLocaleString('ar-SY')} قيمة
              </span>
            </div>
          </div>

          <p className="text-xs app-text-muted leading-relaxed">
            💡 ستظهر أي قيم مفقودة في المذكرات القديمة غير المدققة تلقائياً بحالة «لم تتم المراجعة» لحين استكمالها من قبل المدقق المعتمد.
          </p>

          <div className="flex justify-end gap-3 pt-4 border-t app-border">
            <button
              type="button"
              onClick={() => setImpactPreview({ ...impactPreview, isOpen: false })}
              className="px-4 py-2 text-xs font-semibold rounded-xl border app-border app-surface-muted app-hover app-text"
            >
              تراجع
            </button>
            <button
              type="button"
              onClick={handleConfirmSaveField}
              disabled={submitting}
              className="btn-primary px-5 py-2 text-xs font-bold rounded-xl disabled:opacity-60"
            >
              {submitting ? 'جارٍ الحفظ...' : 'تأكيد الحفظ والاعتماد'}
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
};