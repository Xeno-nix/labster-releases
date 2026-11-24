-- Run after all table definitions in Database/clms against the selected database.
-- Re-runnable reporting views; compatible repository read procedures use these.
-- Preserve historical/soft-deleted records. Consumers choose their own filters.
-- Uses MySQL default SQL SECURITY DEFINER and the creating account as definer.

CREATE OR REPLACE VIEW vw_academic_terms AS
SELECT academic_term_id, academic_year, semester, start_date, end_date
FROM academic_terms;

CREATE OR REPLACE VIEW vw_agent_refresh_credentials AS
SELECT r.credential_id, r.computer_unit_id, u.unit_name, u.laboratory_id,
       l.lab_name, r.credential_generation, r.family_id, r.created_at,
       r.expires_at, r.revoked_at, r.replaced_by_id
FROM agent_refresh_credentials r
LEFT JOIN computer_units u ON u.computer_unit_id = r.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id;

CREATE OR REPLACE VIEW vw_attendance_logs AS
SELECT a.utilization_log_id, a.laboratory_id, l.lab_name,
       a.student_id,
       NULLIF(TRIM(CONCAT_WS(' ', s.first_name, s.middle_name, s.last_name, s.suffix)), '') AS student_name,
       a.guest_id, g.guest_name, a.purpose_subject, a.faculty_in_charge,
       NULLIF(TRIM(CONCAT_WS(' ', f.first_name, f.middle_name, f.last_name, f.suffix)), '') AS faculty_name,
       a.log_date, a.time_in, a.time_out
FROM attendance_logs a
LEFT JOIN laboratories l ON l.lab_id = a.laboratory_id
LEFT JOIN students s ON s.student_id = a.student_id
LEFT JOIN guests g ON g.guest_id = a.guest_id
LEFT JOIN users f ON f.user_id = a.faculty_in_charge;

CREATE OR REPLACE VIEW vw_bookings AS
SELECT b.booking_id, b.lab_id, l.lab_name, b.requestor_name, b.email,
       b.purpose, b.booking_date, b.start_time, b.end_time, b.deleted_at
FROM bookings b
LEFT JOIN laboratories l ON l.lab_id = b.lab_id;

CREATE OR REPLACE VIEW vw_class_schedule AS
SELECT cs.class_schedule_id, cs.course_id, c.course_code, c.course_name,
       cs.instructor_id,
       NULLIF(TRIM(CONCAT_WS(' ', u.first_name, u.middle_name, u.last_name, u.suffix)), '') AS instructor_name,
       cs.lab_id, l.lab_name, cs.day_of_week, cs.time_start, cs.time_end,
       cs.term_id, t.academic_year, t.semester, cs.year_level, cs.section, cs.deleted_at
FROM class_schedule cs
LEFT JOIN courses c ON c.course_id = cs.course_id
LEFT JOIN users u ON u.user_id = cs.instructor_id
LEFT JOIN laboratories l ON l.lab_id = cs.lab_id
LEFT JOIN academic_terms t ON t.academic_term_id = cs.term_id;

CREATE OR REPLACE VIEW vw_computer_units AS
SELECT u.computer_unit_id, u.unit_name, u.unit_number, u.laboratory_id,
       l.lab_name, u.deleted_at, u.created_at, u.last_modified_at, u.hostname,
       u.operating_system, u.agent_version, u.is_enabled, u.credential_generation,
       u.registered_at, u.last_seen_at, u.serial_number,
       a.is_online, a.last_heartbeat, a.ip_address
FROM computer_units u
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id
LEFT JOIN unit_availability a ON a.computer_unit_id = u.computer_unit_id;

CREATE OR REPLACE VIEW vw_courses AS
SELECT course_id, course_code, course_name
FROM courses;

CREATE OR REPLACE VIEW vw_device_auth_challenges AS
SELECT c.challenge_id, c.computer_unit_id, u.unit_name, c.credential_id,
       c.context_id, c.replacement_credential_id, c.purpose, c.audience,
       c.created_at, c.expires_at, c.consumed_at, c.verified_at
FROM device_auth_challenges c
LEFT JOIN computer_units u ON u.computer_unit_id = c.computer_unit_id;

CREATE OR REPLACE VIEW vw_device_credentials AS
SELECT c.credential_id, c.computer_unit_id, u.unit_name,
       c.credential_generation, c.algorithm, c.created_at, c.activated_at,
       c.revoked_at, c.replaces_id, c.replaced_by_id, c.active_unit_id
FROM device_credentials c
LEFT JOIN computer_units u ON u.computer_unit_id = c.computer_unit_id;

CREATE OR REPLACE VIEW vw_equipment AS
SELECT equipment_id, equipment_no, serial_number, equipment_name, brand,
       model_number, equipment_type, status, needs_pm, needs_calibration,
       remarks, deleted_at
FROM equipment;

CREATE OR REPLACE VIEW vw_equipment_assignments AS
SELECT a.assignment_id, a.equipment_id, e.equipment_no, e.equipment_name,
       a.computer_unit_id, u.unit_name, a.laboratory_id, l.lab_name,
       u.laboratory_id AS unit_laboratory_id, ul.lab_name AS unit_lab_name,
       a.status, a.end_reason, a.remarks, a.assigned_at, a.ended_at
FROM equipment_assignments a
LEFT JOIN equipment e ON e.equipment_id = a.equipment_id
LEFT JOIN computer_units u ON u.computer_unit_id = a.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = a.laboratory_id
LEFT JOIN laboratories ul ON ul.lab_id = u.laboratory_id;

CREATE OR REPLACE VIEW vw_external_identities AS
SELECT i.id, i.user_id,
       NULLIF(TRIM(CONCAT_WS(' ', u.first_name, u.middle_name, u.last_name, u.suffix)), '') AS user_name,
       i.provider
FROM external_identities i
LEFT JOIN users u ON u.user_id = i.user_id;

CREATE OR REPLACE VIEW vw_guests AS
SELECT guest_id, guest_name, organization, contact_number, purpose
FROM guests;

CREATE OR REPLACE VIEW vw_laboratories AS
SELECT lab_id, lab_name
FROM laboratories;

CREATE OR REPLACE VIEW vw_refresh_tokens AS
SELECT r.id, r.user_id,
       NULLIF(TRIM(CONCAT_WS(' ', u.first_name, u.middle_name, u.last_name, u.suffix)), '') AS user_name,
       r.created_at, r.expires_at, r.revoked_at
FROM refresh_tokens r
LEFT JOIN users u ON u.user_id = r.user_id;

CREATE OR REPLACE VIEW vw_screen_cast_connections AS
SELECT c.screen_cast_connection_id, c.cast_session_id, c.computer_unit_id,
       u.unit_name, u.laboratory_id, l.lab_name, c.status, c.started_at, c.ended_at,
       s.initiated_by, s.status AS session_status
FROM screen_cast_connections c
LEFT JOIN computer_units u ON u.computer_unit_id = c.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id
LEFT JOIN screen_cast_sessions s ON s.cast_session_id = c.cast_session_id;

CREATE OR REPLACE VIEW vw_screen_cast_sessions AS
SELECT s.cast_session_id, s.initiated_by,
       NULLIF(TRIM(CONCAT_WS(' ', u.first_name, u.middle_name, u.last_name, u.suffix)), '') AS initiator_name,
       s.status, s.started_at, s.ended_at
FROM screen_cast_sessions s
LEFT JOIN users u ON u.user_id = s.initiated_by;

CREATE OR REPLACE VIEW vw_student_enrollment AS
SELECT e.enrollment_id, e.student_id,
       NULLIF(TRIM(CONCAT_WS(' ', s.first_name, s.middle_name, s.last_name, s.suffix)), '') AS student_name,
       e.class_schedule_id, cs.course_id, c.course_code, c.course_name,
       cs.instructor_id,
       NULLIF(TRIM(CONCAT_WS(' ', u.first_name, u.middle_name, u.last_name, u.suffix)), '') AS instructor_name,
       cs.lab_id, l.lab_name, cs.day_of_week, cs.time_start, cs.time_end,
       cs.term_id, t.academic_year, t.semester, cs.year_level, cs.section,
       cs.deleted_at AS schedule_deleted_at
FROM student_enrollment e
LEFT JOIN students s ON s.student_id = e.student_id
LEFT JOIN class_schedule cs ON cs.class_schedule_id = e.class_schedule_id
LEFT JOIN courses c ON c.course_id = cs.course_id
LEFT JOIN users u ON u.user_id = cs.instructor_id
LEFT JOIN laboratories l ON l.lab_id = cs.lab_id
LEFT JOIN academic_terms t ON t.academic_term_id = cs.term_id;

CREATE OR REPLACE VIEW vw_students AS
SELECT student_id, first_name, last_name, middle_name, suffix,
       NULLIF(TRIM(CONCAT_WS(' ', first_name, middle_name, last_name, suffix)), '') AS full_name
FROM students;

CREATE OR REPLACE VIEW vw_supplies AS
SELECT supply_id, name, category, unit, status, remarks
FROM supplies;

CREATE OR REPLACE VIEW vw_unit_activity AS
SELECT a.activity_id, a.computer_unit_id, u.unit_name, u.laboratory_id, l.lab_name,
       a.student_id,
       NULLIF(TRIM(CONCAT_WS(' ', s.first_name, s.middle_name, s.last_name, s.suffix)), '') AS student_name,
       a.guest_id, g.guest_name, a.activity_type, a.description, a.is_resolved,
       a.detected_at, a.resolved_by,
       NULLIF(TRIM(CONCAT_WS(' ', r.first_name, r.middle_name, r.last_name, r.suffix)), '') AS resolved_by_name,
       a.resolved_at
FROM unit_activity a
LEFT JOIN computer_units u ON u.computer_unit_id = a.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id
LEFT JOIN students s ON s.student_id = a.student_id
LEFT JOIN guests g ON g.guest_id = a.guest_id
LEFT JOIN users r ON r.user_id = a.resolved_by;

CREATE OR REPLACE VIEW vw_unit_availability AS
SELECT a.unit_availability_id, a.computer_unit_id, u.unit_name,
       u.laboratory_id, l.lab_name, a.is_online, a.last_heartbeat, a.ip_address
FROM unit_availability a
LEFT JOIN computer_units u ON u.computer_unit_id = a.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id;

CREATE OR REPLACE VIEW vw_unit_registrations AS
SELECT r.registration_id, r.laboratory_id, l.lab_name, r.unit_number,
       r.device_credential_id, r.hostname, r.operating_system, r.agent_version,
       r.status, r.created_at, r.last_modified_at, r.expires_at, r.decided_at,
       r.decided_by,
       NULLIF(TRIM(CONCAT_WS(' ', a.first_name, a.middle_name, a.last_name, a.suffix)), '') AS decided_by_name,
       r.computer_unit_id, u.unit_name, r.credential_generation, r.claimed_at
FROM unit_registrations r
LEFT JOIN laboratories l ON l.lab_id = r.laboratory_id
LEFT JOIN users a ON a.user_id = r.decided_by
LEFT JOIN computer_units u ON u.computer_unit_id = r.computer_unit_id;

CREATE OR REPLACE VIEW vw_unit_utilization_logs AS
SELECT ul.unit_utilization_log_id, ul.computer_unit_id, u.unit_name,
       u.laboratory_id, l.lab_name, ul.student_id,
       NULLIF(TRIM(CONCAT_WS(' ', s.first_name, s.middle_name, s.last_name, s.suffix)), '') AS student_name,
       ul.guest_id, g.guest_name, ul.log_date, ul.time_in, ul.time_out
FROM unit_utilization_logs ul
LEFT JOIN computer_units u ON u.computer_unit_id = ul.computer_unit_id
LEFT JOIN laboratories l ON l.lab_id = u.laboratory_id
LEFT JOIN students s ON s.student_id = ul.student_id
LEFT JOIN guests g ON g.guest_id = ul.guest_id;

CREATE OR REPLACE VIEW vw_users AS
SELECT user_id, first_name, last_name, middle_name, suffix,
       NULLIF(TRIM(CONCAT_WS(' ', first_name, middle_name, last_name, suffix)), '') AS full_name,
       email, role_type, deleted_at, created_at, last_modified_at,
       password_changed, failed_login_attempts, lockout_end, two_factor_enabled
FROM users;

-- Shared projection for logbook paging and exports. Keep nullable historical rows.
CREATE OR REPLACE VIEW vw_logbook_entries AS
SELECT a.utilization_log_id, a.laboratory_id, l.lab_name, a.student_id,
       s.first_name AS student_first_name, s.last_name AS student_last_name,
       g.guest_name, a.purpose_subject, a.log_date, a.time_in, a.time_out,
       NULLIF(TRIM(CONCAT_WS(' ', faculty.first_name, faculty.last_name)), '') AS faculty_name,
       a.course
FROM attendance_logs a
LEFT JOIN students s ON s.student_id = a.student_id
LEFT JOIN guests g ON g.guest_id = a.guest_id
LEFT JOIN laboratories l ON l.lab_id = a.laboratory_id
LEFT JOIN users faculty ON faculty.user_id = a.faculty_in_charge;
