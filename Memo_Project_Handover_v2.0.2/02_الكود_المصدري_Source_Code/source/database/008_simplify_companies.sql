drop index if exists core.ux_companies_code_active;

alter table core.companies
    drop column if exists code,
    drop column if exists phone,
    drop column if exists email,
    drop column if exists address;
