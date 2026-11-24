-- Preserve the last/current review-start time when a note is approved.
-- Existing records are only backfilled when history provides a real value.
update core.notes n
set review_started_at = h.started_at
from (
    select note_id, max(created_at) as started_at
    from core.note_review_history
    where action = 'START_REVIEW'
    group by note_id
) h
where n.id = h.note_id and n.review_started_at is null;
