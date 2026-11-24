-- Correct the one-time 025 backfill so review_started_at represents only
-- the active review or the review cycle that resulted in final approval.

-- Draft, submitted, and returned notes have no active or approved review cycle.
update core.notes
set review_started_at = null
where status not in ('UNDER_REVIEW', 'APPROVED')
  and review_started_at is not null;

-- A note currently under review keeps the latest actual start event.
update core.notes n
set review_started_at = h.started_at
from (
    select note_id, max(created_at) as started_at
    from core.note_review_history
    where action = 'START_REVIEW'
    group by note_id
) h
where n.id = h.note_id
  and n.status = 'UNDER_REVIEW'
  and n.review_started_at is distinct from h.started_at;

-- If no start event exists, do not preserve or invent a timestamp for old data.
update core.notes n
set review_started_at = null
where n.status = 'UNDER_REVIEW'
  and not exists (
      select 1
      from core.note_review_history history
      where history.note_id = n.id
        and history.action = 'START_REVIEW'
  )
  and n.review_started_at is not null;

-- An approved note keeps the start of the last review cycle that began
-- on or before its final approval. If history cannot prove a start, null
-- is more truthful than an invented timestamp.
update core.notes n
set review_started_at = h.started_at
from (
    select n2.id, max(history.created_at) as started_at
    from core.notes n2
    join core.note_review_history history
      on history.note_id = n2.id
     and history.action = 'START_REVIEW'
     and history.created_at <= n2.approved_at
    where n2.status = 'APPROVED'
      and n2.approved_at is not null
    group by n2.id
) h
where n.id = h.id
  and n.status = 'APPROVED'
  and n.review_started_at is distinct from h.started_at;

update core.notes n
set review_started_at = null
where n.status = 'APPROVED'
  and not exists (
      select 1
      from core.note_review_history history
      where history.note_id = n.id
        and history.action = 'START_REVIEW'
        and history.created_at <= n.approved_at
  )
  and n.review_started_at is not null;
