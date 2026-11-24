alter table core.factories add column if not exists company_id uuid references core.companies(id);

update core.factories f
set company_id = inferred.company_id
from (
    select factory_id, (array_agg(company_id))[1] as company_id
    from core.notes
    where factory_id is not null and not is_deleted
    group by factory_id
    having count(distinct company_id) = 1
) inferred
where f.id = inferred.factory_id and f.company_id is null;

drop index if exists core.ux_factories_name_active;
create unique index if not exists ux_factories_company_name_active
    on core.factories(company_id, lower(name))
    where is_deleted = false;
create index if not exists ix_factories_company_id
    on core.factories(company_id) where is_deleted = false;
