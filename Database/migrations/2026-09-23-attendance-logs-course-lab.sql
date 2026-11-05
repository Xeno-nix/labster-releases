-- Adds the two missing columns to attendance_logs so each logged session records
-- the subject taken and the laboratory it happened in (previously both were only
-- resolved transiently from the class schedule / booking and then discarded).
--
-- course        : subject of the session, e.g. "CS101 - Intro to Programming"
--                 (students: from the class schedule; guests: from the booking purpose —
--                 NULL when neither is known). Matches the paper logbook's Subject column.
-- laboratory_id : FK to laboratories.lab_id; NULL for rows created before this change
--                 (no backfill is possible — the lab was never stored).
--
-- faculty_in_charge already exists and is an FK to users.user_id, so the Faculty
-- column needs no schema change — it is resolved by joining users at read time.

ALTER TABLE attendance_logs
    ADD COLUMN course VARCHAR(255) NULL AFTER purpose_subject,
    ADD COLUMN laboratory_id INT NULL AFTER course;

ALTER TABLE attendance_logs
    ADD CONSTRAINT fk_attendance_logs_laboratory
    FOREIGN KEY (laboratory_id) REFERENCES laboratories (lab_id);
