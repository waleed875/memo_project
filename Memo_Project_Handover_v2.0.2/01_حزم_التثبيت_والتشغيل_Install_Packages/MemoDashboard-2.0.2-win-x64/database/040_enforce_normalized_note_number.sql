alter table core.notes
    add constraint ck_notes_normalized_number_not_empty
    check (length(trim(note_number_normalized)) > 0) not valid;

alter table core.notes
    validate constraint ck_notes_normalized_number_not_empty;
