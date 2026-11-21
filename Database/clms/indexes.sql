-- CLMS repository query indexes (MySQL 8.0.21+, matching stored_procedures.sql).
-- Select the intended database, then SOURCE Database/clms/indexes.sql after
-- all table definitions exist. Indexes are on base tables, supporting both
-- direct repository queries and queries through mergeable views.
-- Apply once: CREATE INDEX fails if the name already exists. No existing
-- primary keys, unique constraints, foreign keys or indexes are removed.
-- MySQL DDL commits implicitly; a failure can leave earlier indexes installed.
-- These are workload candidates based on the current repository predicates.
-- Validate EXPLAIN ANALYZE against representative deployed data before rollout;
-- runtime gains depend on data size/selectivity, and indexes add write/storage cost.
-- Some composite indexes extend existing single-column FK indexes. Check actual
-- SHOW INDEX output before deciding whether to consolidate those older indexes.

-- UserRepository.FindByEmailAsync / ExternalIdentityRepository.LinkGoogleAsync:
-- the existing UNIQUE(email) does not index the LOWER(TRIM(email)) expression.
-- Non-unique: preserves existing uniqueness and account-matching semantics.
CREATE INDEX ix_users_email_normalized
    ON users ((LOWER(TRIM(email))));

-- BookingRepository.GetAllAsync: active bookings in date/time order.
CREATE INDEX ix_bookings_active_date_time
    ON bookings (deleted_at, booking_date, start_time);

-- BookingRepository.FindByRequestorNameAndDateAsync: includes deleted rows,
-- matching the current repository; do not put deleted_at before these fields.
CREATE INDEX ix_bookings_requestor_date_time
    ON bookings (requestor_name, booking_date, start_time);

-- ClassScheduleRepository.GetByInstructorAsync: instructor+term, active rows,
-- followed by the same year/section/time ordering as the repository.
CREATE INDEX ix_schedule_instructor_term_active_order
    ON class_schedule (instructor_id, term_id, deleted_at, year_level, section, time_start);

-- ClassScheduleRepository.GetAllForTermAsync: term and active-row filtering.
-- ORDER BY also uses courses.course_code across a join; this does not eliminate
-- that complete sort, nor does it optimize every day/time schedule query.
CREATE INDEX ix_schedule_term_active
    ON class_schedule (term_id, deleted_at);

-- ClassScheduleRepository.GetGroupScheduleIdsAsync / GetGroupSchedulesAsync:
-- the class grouping uses all five fields, including null-safe year/section.
CREATE INDEX ix_schedule_class_group_active
    ON class_schedule (course_id, instructor_id, term_id, year_level, section, deleted_at);

-- EquipmentRepository.GetActiveAssignmentAsync and active assignment joins.
CREATE INDEX ix_assignments_equipment_status
    ON equipment_assignments (equipment_id, status);

-- EquipmentRepository.GetAssignmentsForComputerUnitAsync: active assignments
-- for one PC, ordered by assigned_at (backward scan for DESC).
CREATE INDEX ix_assignments_unit_status_assigned
    ON equipment_assignments (computer_unit_id, status, assigned_at);

-- LabUtilizationRepository.GetActiveSessionForStudentAsync: newest open entry.
CREATE INDEX ix_attendance_student_open_time
    ON attendance_logs (student_id, time_out, time_in);

-- LabUtilizationRepository.GetActiveSessionsForGuestNameAsync: guest join and
-- open-entry lookup. A sort can still be needed across multiple matching guests.
CREATE INDEX ix_guests_name
    ON guests (guest_name);

CREATE INDEX ix_attendance_guest_open_time
    ON attendance_logs (guest_id, time_out, time_in);

-- LogbookRepository day filter: equality on log_date, then newest arrivals.
-- Existing YEARWEEK/MONTH/YEAR predicates do not become date-range seeks merely
-- by adding this index; those queries retain their current SQL and semantics.
CREATE INDEX ix_attendance_date_time
    ON attendance_logs (log_date, time_in);

-- LogbookRepository unfiltered paging/export: newest arrivals first.
CREATE INDEX ix_attendance_time
    ON attendance_logs (time_in);

-- LabUtilizationRepository.GetDashboardStatsAsync: faculty scope + lab grouping.
CREATE INDEX ix_attendance_faculty_laboratory
    ON attendance_logs (faculty_in_charge, laboratory_id);

-- UnitMonitoringRepository.GetAllAsync: find exactly the newest open log per PC,
-- using the same time_in / primary-key tie-break as its correlated subquery.
CREATE INDEX ix_unit_utilization_open_latest
    ON unit_utilization_logs (computer_unit_id, time_out, time_in, unit_utilization_log_id);

-- RefreshTokenRepository.GetActiveByUserIdAsync / TryIssueAsync and revocation:
-- user + unrevoked rows, then oldest-first creation/id order for session eviction.
-- expires_at is a trailing covered filter: moving its range before created_at
-- would prevent this index from providing that ordering.
CREATE INDEX ix_refresh_user_revoked_created
    ON refresh_tokens (user_id, revoked_at, created_at, id, expires_at);

-- UnitRegistryRepository.ExpirePendingAsync: lab + pending state + expiry range.
-- Existing status/expiry and generated pending-slot unique indexes remain.
CREATE INDEX ix_registration_lab_status_expiry
    ON unit_registrations (laboratory_id, status, expires_at);

-- ScreenCastRepository.EndAsync / RecoverInterruptedAsync: active-session lookup
-- as historical ended/failed sessions accumulate. InnoDB also stores the PK in
-- this secondary index, so adding cast_session_id explicitly is unnecessary.
CREATE INDEX ix_cast_sessions_status
    ON screen_cast_sessions (status);

-- Existing indexes already cover ID/token/fingerprint lookups, unit lab/number
-- uniqueness, enrollment student/schedule keys, availability-by-unit, and device
-- challenge expiry/outstanding lookups; no duplicate definitions are added here.
-- No indexes solely for tiny reference-table sorts or low-cardinality role flags.
-- Leading-wildcard LIKE '%text%' searches, CAST(unit_number AS CHAR) searches,
-- and CASE-derived effective registration status need different query strategies;
-- ordinary indexes on their raw columns do not make these predicates seekable.
