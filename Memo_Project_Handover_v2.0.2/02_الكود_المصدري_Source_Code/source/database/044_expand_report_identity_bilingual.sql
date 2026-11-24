alter table system.report_identity_settings
    add column if not exists republic_name_ar text not null default 'الجمهورية العربية السورية',
    add column if not exists republic_name_en text not null default 'Syrian Arab Republic',
    add column if not exists ministry_name_en text not null default '',
    add column if not exists company_name_en text not null default '',
    add column if not exists administrative_level_one_en text not null default '',
    add column if not exists administrative_level_two_en text not null default '',
    add column if not exists administrative_level_three text not null default '',
    add column if not exists administrative_level_three_en text not null default '';
