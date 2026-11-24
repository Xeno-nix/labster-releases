-- CLMS repository stored procedures (MySQL 8.0.21+).
-- Select the intended database; install tables, then stored_functions.sql, then this file.
-- Application repositories call these routines; install before deploying the application.
-- SQL SECURITY and DEFINER are omitted, using the MySQL defaults requested.
-- Naming: sp_<repository without Repository>_<method without Async>, snake_case.
-- Inputs use p_ prefixes. Nullable C# inputs are passed as SQL NULL.
-- Result columns/filters mirror repository SQL; authentication routines include secrets.
-- Read methods return rowsets/scalars; DML returns affected_rows unless noted.
-- Identity inserts return id. Conditional security changes return succeeded (0/1).
-- Affected rows depend on connection CLIENT_FOUND_ROWS just as raw repository DML does.
-- Bulk inputs are JSON arrays; pass [] for empty collections, never comma-separated SQL.
-- JSON object keys for student imports: student_id, first_name, last_name, middle_name, suffix.
-- Enum values use existing database strings (e.g. fully_stocked, active, First, Monday).
-- Caller remains responsible for domain validation, DTO mapping, authorization,
-- cryptographic token generation and retries for deadlock/lock-timeout/duplicate-key errors.
-- Routines marked Owns its transaction must NOT run inside a caller transaction.
-- Registry primitives and bulk methods participate in the caller transaction and do not commit.
-- Registry usage: START TRANSACTION; CALL sp_unit_registry_transaction('Unit', UUID);
-- use the returned now_utc for primitive Now parameters; CALL primitives; COMMIT (or ROLLBACK).
-- The generic C# TransactionAsync callback/retry cannot be represented as one SQL routine.
-- Its corresponding routine only resolves the target, locks the lab and returns now_utc.
-- Multi-result methods: Logbook.GetPaged, UnitRegistry.Units and Registrations return
-- total_count first, then rows. LabUtilization.GetDashboardStats returns per-lab rows.
-- Equipment.GetNextEquipmentNo returns the formatted suggestion; it is not reserved.
-- ScreenCast.Create requires the same cryptographically random token generated in C#.
-- ResetToken.Create callers must validate SHA-256 hex hashes as the repository does.
-- Compatible reference/roster reads use reporting views; security and locking reads use base tables.
-- Installation uses DROP/CREATE (MySQL has no CREATE OR REPLACE PROCEDURE).
-- DDL implicitly commits; install while callers are quiescent. Reapply EXECUTE grants if needed.
-- This script installs definitions only; it neither calls routines nor changes table data.

-- AcademicTermRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_academic_term_create;
DELIMITER $$
CREATE PROCEDURE sp_academic_term_create(
    IN p_year VARCHAR(20),
    IN p_semester VARCHAR(10),
    IN p_start DATE,
    IN p_end DATE
)
proc: BEGIN
    INSERT INTO academic_terms (academic_year, semester, start_date, end_date)
    VALUES (p_year, p_semester, p_start, p_end);
    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- AcademicTermRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_academic_term_update;
DELIMITER $$
CREATE PROCEDURE sp_academic_term_update(
    IN p_year VARCHAR(20),
    IN p_semester VARCHAR(10),
    IN p_start DATE,
    IN p_end DATE,
    IN p_id INT
)
proc: BEGIN
    UPDATE academic_terms SET academic_year = p_year, semester = p_semester,
        start_date = p_start, end_date = p_end WHERE academic_term_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- AcademicTermRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_academic_term_get_all;
DELIMITER $$
CREATE PROCEDURE sp_academic_term_get_all()
proc: BEGIN
    SELECT
        academic_term_id,
        academic_year,
        semester,
        start_date,
        end_date
    FROM vw_academic_terms
    ORDER BY academic_year, semester;
END$$
DELIMITER ;

-- AcademicTermRepository.GetCurrentAsync
DROP PROCEDURE IF EXISTS sp_academic_term_get_current;
DELIMITER $$
CREATE PROCEDURE sp_academic_term_get_current(
    IN p_today DATE
)
proc: BEGIN
    SELECT
        academic_term_id,
        academic_year,
        semester,
        start_date,
        end_date
    FROM vw_academic_terms
    WHERE (start_date IS NULL OR start_date <= p_today)
      AND (end_date IS NULL OR end_date >= p_today)
    ORDER BY start_date DESC
    LIMIT 1;
END$$
DELIMITER ;

-- CourseRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_course_create;
DELIMITER $$
CREATE PROCEDURE sp_course_create(
    IN p_code VARCHAR(20),
    IN p_name VARCHAR(100)
)
proc: BEGIN
    INSERT INTO courses (course_code, course_name) VALUES (p_code, p_name);
    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- CourseRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_course_update;
DELIMITER $$
CREATE PROCEDURE sp_course_update(
    IN p_code VARCHAR(20),
    IN p_name VARCHAR(100),
    IN p_id INT
)
proc: BEGIN
    UPDATE courses SET course_code = p_code, course_name = p_name WHERE course_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- CourseRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_course_get_all;
DELIMITER $$
CREATE PROCEDURE sp_course_get_all()
proc: BEGIN
    SELECT
        course_id,
        course_code,
        course_name
    FROM vw_courses
    ORDER BY course_name;
END$$
DELIMITER ;

-- CourseRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_course_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_course_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        course_id,
        course_code,
        course_name
    FROM vw_courses
    WHERE course_id = p_id;
END$$
DELIMITER ;

-- AcademicTermRepository.DeleteAsync
-- Owns its transaction; call outside an existing transaction.
DROP PROCEDURE IF EXISTS sp_academic_term_delete;
DELIMITER $$
CREATE PROCEDURE sp_academic_term_delete(
    IN p_id INT
)
proc: BEGIN
    DECLARE v_id INT DEFAULT NULL;
    DECLARE v_rows INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    SELECT academic_term_id INTO v_id FROM academic_terms WHERE academic_term_id=p_id FOR UPDATE;
    IF v_id IS NULL THEN
        ROLLBACK;
        SELECT 0 AS affected_rows;
        LEAVE proc;
    END IF;
    IF EXISTS(SELECT 1 FROM class_schedule WHERE term_id=p_id) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Record is used by class schedules; remove or reassign them first.';
    END IF;
    DELETE FROM academic_terms WHERE academic_term_id=p_id;
    SET v_rows=ROW_COUNT();
    COMMIT;
    SELECT v_rows AS affected_rows;
END$$
DELIMITER ;

-- CourseRepository.DeleteAsync
-- Owns its transaction; call outside an existing transaction.
DROP PROCEDURE IF EXISTS sp_course_delete;
DELIMITER $$
CREATE PROCEDURE sp_course_delete(
    IN p_id INT
)
proc: BEGIN
    DECLARE v_id INT DEFAULT NULL;
    DECLARE v_rows INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    SELECT course_id INTO v_id FROM courses WHERE course_id=p_id FOR UPDATE;
    IF v_id IS NULL THEN
        ROLLBACK;
        SELECT 0 AS affected_rows;
        LEAVE proc;
    END IF;
    IF EXISTS(SELECT 1 FROM class_schedule WHERE course_id=p_id) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Record is used by class schedules; remove or reassign them first.';
    END IF;
    DELETE FROM courses WHERE course_id=p_id;
    SET v_rows=ROW_COUNT();
    COMMIT;
    SELECT v_rows AS affected_rows;
END$$
DELIMITER ;

-- BookingRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_booking_get_all;
DELIMITER $$
CREATE PROCEDURE sp_booking_get_all()
proc: BEGIN
    SELECT
        b.booking_id,
        b.lab_id,
        l.lab_name,
        b.requestor_name,
        b.email,
        b.purpose,
        b.booking_date,
        b.start_time,
        b.end_time
    FROM bookings b
    INNER JOIN laboratories l
        ON b.lab_id = l.lab_id
    WHERE b.deleted_at IS NULL
    ORDER BY b.booking_date, b.start_time;
END$$
DELIMITER ;

-- BookingRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_booking_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_booking_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        b.booking_id,
        b.lab_id,
        l.lab_name,
        b.requestor_name,
        b.email,
        b.purpose,
        b.booking_date,
        b.start_time,
        b.end_time
    FROM bookings b
    INNER JOIN laboratories l
        ON b.lab_id = l.lab_id
    WHERE b.booking_id = p_id
      AND b.deleted_at IS NULL;
END$$
DELIMITER ;

-- BookingRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_booking_create;
DELIMITER $$
CREATE PROCEDURE sp_booking_create(
    IN p_laboratory_id INT,
    IN p_requestor_name VARCHAR(150),
    IN p_email VARCHAR(100),
    IN p_purpose VARCHAR(255),
    IN p_date DATE,
    IN p_start_time TIME,
    IN p_end_time TIME
)
proc: BEGIN
    INSERT INTO bookings
    (
        lab_id,
        requestor_name,
        email,
        purpose,
        booking_date,
        start_time,
        end_time
    )
    VALUES
    (
        p_laboratory_id,
        p_requestor_name,
        p_email,
        p_purpose,
        p_date,
        p_start_time,
        p_end_time
    );

    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- BookingRepository.FindByRequestorNameAndDateAsync
DROP PROCEDURE IF EXISTS sp_booking_find_by_requestor_name_and_date;
DELIMITER $$
CREATE PROCEDURE sp_booking_find_by_requestor_name_and_date(
    IN p_requestor_name VARCHAR(150),
    IN p_date DATE
)
proc: BEGIN
    SELECT
        booking_id,
        lab_id,
        requestor_name,
        email,
        purpose,
        booking_date,
        start_time,
        end_time
    FROM bookings
    WHERE requestor_name = p_requestor_name
    AND booking_date = p_date
    ORDER BY start_time;
END$$
DELIMITER ;

-- BookingRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_booking_update;
DELIMITER $$
CREATE PROCEDURE sp_booking_update(
    IN p_laboratory_id INT,
    IN p_requestor_name VARCHAR(150),
    IN p_email VARCHAR(100),
    IN p_purpose VARCHAR(255),
    IN p_date DATE,
    IN p_start_time TIME,
    IN p_end_time TIME,
    IN p_id INT
)
proc: BEGIN
    UPDATE bookings
    SET
        lab_id = p_laboratory_id,
        requestor_name = p_requestor_name,
        email = p_email,
        purpose = p_purpose,
        booking_date = p_date,
        start_time = p_start_time,
        end_time = p_end_time
    WHERE booking_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- BookingRepository.DeleteAsync
DROP PROCEDURE IF EXISTS sp_booking_delete;
DELIMITER $$
CREATE PROCEDURE sp_booking_delete(
    IN p_id INT
)
proc: BEGIN
    UPDATE bookings
    SET
        deleted_at = NOW()
    WHERE booking_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_all;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_all()
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id
    WHERE u.role_type = 'Faculty'
      AND cs.deleted_at IS NULL
    ORDER BY
        cs.day_of_week,
        cs.time_start;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetByInstructorAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_by_instructor;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_by_instructor(
    IN p_instructor_id CHAR(36),
    IN p_term_id INT
)
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id
    WHERE cs.instructor_id = p_instructor_id
      AND cs.term_id = p_term_id
      AND cs.deleted_at IS NULL
    ORDER BY
        cs.year_level,
        cs.section,
        cs.time_start;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetAllForTermAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_all_for_term;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_all_for_term(
    IN p_term_id INT
)
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id
    WHERE cs.term_id = p_term_id
      AND cs.deleted_at IS NULL
    ORDER BY
        cs.year_level,
        cs.section,
        c.course_code,
        cs.time_start;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetGroupScheduleIdsAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_group_schedule_ids;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_group_schedule_ids(
    IN p_schedule_id INT
)
proc: BEGIN
    SELECT cs2.class_schedule_id
    FROM class_schedule cs
    INNER JOIN class_schedule cs2
        ON  cs2.course_id     =  cs.course_id
        AND cs2.instructor_id =  cs.instructor_id
        AND cs2.term_id       =  cs.term_id
        AND cs2.year_level   <=> cs.year_level
        AND cs2.section      <=> cs.section
    WHERE cs.class_schedule_id = p_schedule_id
      AND cs.deleted_at IS NULL
      AND cs2.deleted_at IS NULL;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetGroupSchedulesAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_group_schedules;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_group_schedules(
    IN p_schedule_id INT
)
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id
    INNER JOIN class_schedule cs2
        ON  cs.course_id     =  cs2.course_id
        AND cs.instructor_id =  cs2.instructor_id
        AND cs.term_id       =  cs2.term_id
        AND cs.year_level   <=> cs2.year_level
        AND cs.section      <=> cs2.section
    WHERE cs2.class_schedule_id = p_schedule_id
      AND cs.deleted_at IS NULL
      AND cs2.deleted_at IS NULL
    ORDER BY
        cs.day_of_week,
        cs.time_start;
END$$
DELIMITER ;

-- ClassScheduleRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id
    WHERE cs.class_schedule_id = p_id
      AND u.role_type = 'Faculty'
      AND cs.deleted_at IS NULL;
END$$
DELIMITER ;

-- ClassScheduleRepository.FindCurrentForStudentAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_find_current_for_student;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_find_current_for_student(
    IN p_student_id VARCHAR(12),
    IN p_day_of_week VARCHAR(10),
    IN p_current_time TIME
)
proc: BEGIN
    SELECT
        cs.class_schedule_id,
    cs.course_id,
    c.course_code,
    c.course_name,
    cs.instructor_id,
    CONCAT(u.first_name, ' ', u.last_name) AS instructor_name,
    cs.lab_id,
    l.lab_name,
    cs.term_id,
    at.academic_year,
    at.semester,
    cs.day_of_week,
    cs.time_start,
    cs.time_end,
    cs.year_level,
    cs.section,
    (
        SELECT COUNT(DISTINCT se.student_id)
        FROM student_enrollment se
        WHERE se.class_schedule_id = cs.class_schedule_id
    ) AS student_count
    FROM class_schedule cs
    INNER JOIN courses c
        ON cs.course_id = c.course_id
    INNER JOIN users u
        ON cs.instructor_id = u.user_id
    INNER JOIN laboratories l
        ON cs.lab_id = l.lab_id
    INNER JOIN academic_terms at
        ON cs.term_id = at.academic_term_id

    INNER JOIN student_enrollment se
        ON se.class_schedule_id = cs.class_schedule_id

    WHERE se.student_id = p_student_id
    AND cs.day_of_week = p_day_of_week
    AND cs.time_start <= p_current_time
    AND cs.time_end >= p_current_time
    AND cs.deleted_at IS NULL
    AND (at.start_date IS NULL OR at.start_date <= CURDATE())
    AND (at.end_date IS NULL OR at.end_date >= CURDATE())
    AND u.role_type = 'Faculty'

    LIMIT 1;
END$$
DELIMITER ;

-- ClassScheduleRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_create;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_create(
    IN p_course_id INT,
    IN p_instructor_id CHAR(36),
    IN p_laboratory_id INT,
    IN p_day_of_week VARCHAR(10),
    IN p_time_start TIME,
    IN p_time_end TIME,
    IN p_academic_term_id INT,
    IN p_year_level TINYINT UNSIGNED,
    IN p_section VARCHAR(10)
)
proc: BEGIN
    INSERT INTO class_schedule
    (
        course_id,
        instructor_id,
        lab_id,
        day_of_week,
        time_start,
        time_end,
        term_id,
        year_level,
        section
    )
    VALUES
    (
        p_course_id,
        p_instructor_id,
        p_laboratory_id,
        p_day_of_week,
        p_time_start,
        p_time_end,
        p_academic_term_id,
        p_year_level,
        p_section
    );

    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- ClassScheduleRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_update;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_update(
    IN p_course_id INT,
    IN p_instructor_id CHAR(36),
    IN p_laboratory_id INT,
    IN p_day_of_week VARCHAR(10),
    IN p_time_start TIME,
    IN p_time_end TIME,
    IN p_academic_term_id INT,
    IN p_year_level TINYINT UNSIGNED,
    IN p_section VARCHAR(10),
    IN p_id INT
)
proc: BEGIN
    UPDATE class_schedule
    SET
        course_id = p_course_id,
        instructor_id = p_instructor_id,
        lab_id = p_laboratory_id,
        day_of_week = p_day_of_week,
        time_start = p_time_start,
        time_end = p_time_end,
        term_id = p_academic_term_id,
        year_level = p_year_level,
        section = p_section
    WHERE class_schedule_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ClassScheduleRepository.DeleteAsync
DROP PROCEDURE IF EXISTS sp_class_schedule_delete;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_delete(
    IN p_id INT
)
proc: BEGIN
    UPDATE class_schedule
    SET
        deleted_at = NOW()
    WHERE class_schedule_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ClassScheduleRepository.DeleteManyAsync
-- Participates in the caller transaction; JSON array of schedule IDs.
DROP PROCEDURE IF EXISTS sp_class_schedule_delete_many;
DELIMITER $$
CREATE PROCEDURE sp_class_schedule_delete_many(
    IN p_schedule_ids JSON
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    UPDATE class_schedule SET deleted_at=NOW() WHERE deleted_at IS NULL AND class_schedule_id IN (SELECT id FROM JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) ids);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ComputerUnitRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_create;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_create(
    IN p_id CHAR(36),
    IN p_unit_name VARCHAR(100),
    IN p_unit_number TINYINT UNSIGNED,
    IN p_laboratory_id INT
)
proc: BEGIN
    INSERT INTO computer_units
    (
        computer_unit_id,
        unit_name,
        unit_number,
        laboratory_id,
        created_at,
        last_modified_at
    )
    VALUES
    (
        p_id,
        p_unit_name,
        p_unit_number,
        p_laboratory_id,
        UTC_TIMESTAMP(6),
        UTC_TIMESTAMP(6)
    );
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ComputerUnitRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_get_all;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_get_all()
proc: BEGIN
    SELECT
        cu.computer_unit_id,
        cu.unit_name,
        cu.unit_number,
        cu.laboratory_id,
        l.lab_name
    FROM computer_units cu
    INNER JOIN laboratories l
        ON cu.laboratory_id = l.lab_id
    WHERE cu.deleted_at IS NULL
    ORDER BY l.lab_name, cu.unit_number;
END$$
DELIMITER ;

-- ComputerUnitRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_get_by_id(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT
        cu.computer_unit_id,
        cu.unit_name,
        cu.unit_number,
        cu.laboratory_id,
        l.lab_name
    FROM computer_units cu
    INNER JOIN laboratories l
        ON cu.laboratory_id = l.lab_id
    WHERE cu.computer_unit_id = p_id
      AND cu.deleted_at IS NULL;
END$$
DELIMITER ;

-- ComputerUnitRepository.ExistsActiveAtLabAndNumberAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_exists_active_at_lab_and_number;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_exists_active_at_lab_and_number(
    IN p_laboratory_id INT,
    IN p_unit_number TINYINT UNSIGNED,
    IN p_excluding_id CHAR(36)
)
proc: BEGIN
    SELECT COUNT(*)
    FROM computer_units
    WHERE laboratory_id = p_laboratory_id
      AND unit_number = p_unit_number
      AND deleted_at IS NULL
      AND (p_excluding_id IS NULL OR computer_unit_id <> p_excluding_id);
END$$
DELIMITER ;

-- ComputerUnitRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_update;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_update(
    IN p_unit_name VARCHAR(100),
    IN p_unit_number TINYINT UNSIGNED,
    IN p_laboratory_id INT,
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE computer_units
    SET
        unit_name = p_unit_name,
        unit_number = p_unit_number,
        laboratory_id = p_laboratory_id,
        last_modified_at = UTC_TIMESTAMP(6)
    WHERE computer_unit_id = p_id
      AND deleted_at IS NULL
      AND (registered_at IS NULL OR (laboratory_id=p_laboratory_id AND unit_number=p_unit_number));
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ComputerUnitRepository.ExistsAtLabAndNumberAsync
DROP PROCEDURE IF EXISTS sp_computer_unit_exists_at_lab_and_number;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_exists_at_lab_and_number(
    IN p_laboratory_id INT,
    IN p_unit_number TINYINT UNSIGNED,
    IN p_excluding_id CHAR(36)
)
proc: BEGIN
    SELECT EXISTS(SELECT 1 FROM computer_units WHERE laboratory_id=p_laboratory_id AND unit_number=p_unit_number AND (p_excluding_id IS NULL OR computer_unit_id<>p_excluding_id)) AS exists_assignment;
END$$
DELIMITER ;

-- ComputerUnitRepository.DeleteAsync
-- Owns its transaction; locks laboratory then unit, revokes credentials and marks offline.
DROP PROCEDURE IF EXISTS sp_computer_unit_delete;
DELIMITER $$
CREATE PROCEDURE sp_computer_unit_delete(
    IN p_id CHAR(36)
)
proc: BEGIN
    DECLARE v_lab INT DEFAULT NULL;
    DECLARE v_locked_lab INT DEFAULT NULL;
    DECLARE v_now DATETIME(6);
    DECLARE v_locked_unit CHAR(36);
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    SELECT laboratory_id INTO v_lab FROM computer_units WHERE computer_unit_id=p_id;
    IF v_lab IS NULL THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Invalid device credential.'; END IF;
    START TRANSACTION;
    SELECT lab_id INTO v_locked_lab FROM laboratories WHERE lab_id=v_lab FOR UPDATE;
    IF v_locked_lab IS NULL THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Laboratory is unavailable.'; END IF;
    SET v_now=UTC_TIMESTAMP(6);
    SELECT computer_unit_id INTO v_locked_unit FROM computer_units WHERE computer_unit_id=p_id FOR UPDATE;
    UPDATE computer_units SET deleted_at=v_now,last_modified_at=v_now,is_enabled=0,credential_generation=credential_generation+1
    WHERE computer_unit_id=p_id AND deleted_at IS NULL;
    UPDATE device_credentials SET revoked_at=COALESCE(revoked_at,v_now) WHERE computer_unit_id=p_id;
    UPDATE computer_units SET last_seen_at=NULL WHERE computer_unit_id=p_id;
    UPDATE unit_availability SET is_online=0 WHERE computer_unit_id=p_id;
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- SupplyRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_supply_create;
DELIMITER $$
CREATE PROCEDURE sp_supply_create(
    IN p_name VARCHAR(100),
    IN p_category VARCHAR(50),
    IN p_unit VARCHAR(30),
    IN p_status VARCHAR(20),
    IN p_remarks VARCHAR(255)
)
proc: BEGIN
    INSERT INTO supplies
    (
        name,
        category,
        unit,
        status,
        remarks
    )
    VALUES
    (
        p_name,
        p_category,
        p_unit,
        p_status,
        p_remarks
    );

    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- SupplyRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_supply_get_all;
DELIMITER $$
CREATE PROCEDURE sp_supply_get_all()
proc: BEGIN
    SELECT
        supply_id,
        name,
        category,
        unit,
        status,
        remarks
    FROM vw_supplies
    ORDER BY name;
END$$
DELIMITER ;

-- SupplyRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_supply_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_supply_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        supply_id,
        name,
        category,
        unit,
        status,
        remarks
    FROM vw_supplies
    WHERE supply_id = p_id;
END$$
DELIMITER ;

-- SupplyRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_supply_update;
DELIMITER $$
CREATE PROCEDURE sp_supply_update(
    IN p_name VARCHAR(100),
    IN p_category VARCHAR(50),
    IN p_unit VARCHAR(30),
    IN p_status VARCHAR(20),
    IN p_remarks VARCHAR(255),
    IN p_id INT
)
proc: BEGIN
    UPDATE supplies
    SET
        name = p_name,
        category = p_category,
        unit = p_unit,
        status = p_status,
        remarks = p_remarks
    WHERE supply_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- SupplyRepository.UpdateStatusAsync
DROP PROCEDURE IF EXISTS sp_supply_update_status;
DELIMITER $$
CREATE PROCEDURE sp_supply_update_status(
    IN p_status VARCHAR(20),
    IN p_id INT
)
proc: BEGIN
    UPDATE supplies
    SET
        status = p_status
    WHERE supply_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- SupplyRepository.DeleteAsync
DROP PROCEDURE IF EXISTS sp_supply_delete;
DELIMITER $$
CREATE PROCEDURE sp_supply_delete(
    IN p_id INT
)
proc: BEGIN
    DELETE FROM supplies
    WHERE supply_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- GuestRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_guest_create;
DELIMITER $$
CREATE PROCEDURE sp_guest_create(
    IN p_guest_name VARCHAR(150),
    IN p_organization VARCHAR(150),
    IN p_contact_number VARCHAR(30),
    IN p_purpose VARCHAR(255)
)
proc: BEGIN
    INSERT INTO guests
    (
        guest_name,
        organization,
        contact_number,
        purpose
    )
    VALUES
    (
        p_guest_name,
        p_organization,
        p_contact_number,
        p_purpose
    );

    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- GuestRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_guest_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_guest_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        guest_id,
        guest_name,
        organization,
        contact_number,
        purpose
    FROM guests
    WHERE guest_id = p_id;
END$$
DELIMITER ;

-- LaboratoryRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_laboratory_get_all;
DELIMITER $$
CREATE PROCEDURE sp_laboratory_get_all()
proc: BEGIN
    SELECT
        lab_id,
        lab_name
    FROM vw_laboratories
    ORDER BY lab_name;
END$$
DELIMITER ;

-- LaboratoryRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_laboratory_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_laboratory_get_by_id(
    IN p_id INT
)
proc: BEGIN
    SELECT
        lab_id,
        lab_name
    FROM vw_laboratories
    WHERE lab_id = p_id;
END$$
DELIMITER ;

-- LabUtilizationRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_lab_utilization_create;
DELIMITER $$
CREATE PROCEDURE sp_lab_utilization_create(
    IN p_student_id VARCHAR(12),
    IN p_guest_id INT,
    IN p_purpose VARCHAR(255),
    IN p_instructor_id VARCHAR(50),
    IN p_laboratory_id INT,
    IN p_log_date DATE,
    IN p_time_in DATETIME,
    IN p_time_out DATETIME
)
proc: BEGIN
    INSERT INTO attendance_logs
    (
        student_id,
        guest_id,
        purpose_subject,
        faculty_in_charge,
        laboratory_id,
        log_date,
        time_in,
        time_out
    )
    VALUES
    (
        p_student_id,
        p_guest_id,
        p_purpose,
        p_instructor_id,
        p_laboratory_id,
        p_log_date,
        p_time_in,
        p_time_out
    );

    SELECT LAST_INSERT_ID() AS id;
END$$
DELIMITER ;

-- LabUtilizationRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_lab_utilization_update;
DELIMITER $$
CREATE PROCEDURE sp_lab_utilization_update(
    IN p_student_id VARCHAR(12),
    IN p_guest_id INT,
    IN p_purpose VARCHAR(255),
    IN p_instructor_id VARCHAR(50),
    IN p_log_date DATE,
    IN p_time_in DATETIME,
    IN p_time_out DATETIME,
    IN p_id INT
)
proc: BEGIN
    UPDATE attendance_logs
    SET
        student_id = p_student_id,
        guest_id = p_guest_id,
        purpose_subject = p_purpose,
        faculty_in_charge = p_instructor_id,
        log_date = p_log_date,
        time_in = p_time_in,
        time_out = p_time_out
    WHERE utilization_log_id = p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- LabUtilizationRepository.GetActiveSessionForStudentAsync
DROP PROCEDURE IF EXISTS sp_lab_utilization_get_active_session_for_student;
DELIMITER $$
CREATE PROCEDURE sp_lab_utilization_get_active_session_for_student(
    IN p_student_id VARCHAR(12)
)
proc: BEGIN
    SELECT
        utilization_log_id,
        student_id,
        guest_id,
        purpose_subject,
        faculty_in_charge,
        log_date,
        time_in,
        time_out
    FROM attendance_logs
    WHERE student_id = p_student_id
    AND time_out IS NULL
    ORDER BY time_in DESC
    LIMIT 1;
END$$
DELIMITER ;

-- LabUtilizationRepository.GetActiveSessionsForGuestNameAsync
DROP PROCEDURE IF EXISTS sp_lab_utilization_get_active_sessions_for_guest_name;
DELIMITER $$
CREATE PROCEDURE sp_lab_utilization_get_active_sessions_for_guest_name(
    IN p_guest_name VARCHAR(150)
)
proc: BEGIN
    SELECT
        al.utilization_log_id,
        al.student_id,
        al.guest_id,
        al.purpose_subject,
        al.faculty_in_charge,
        al.log_date,
        al.time_in,
        al.time_out
    FROM attendance_logs al
    INNER JOIN guests g
        ON al.guest_id = g.guest_id
    WHERE g.guest_name = p_guest_name
    AND al.time_out IS NULL
    ORDER BY al.time_in DESC;
END$$
DELIMITER ;

-- LabUtilizationRepository.GetDashboardStatsAsync
-- Returns per-lab rows, including a null-lab group; caller sums totals as in the repository.
DROP PROCEDURE IF EXISTS sp_lab_utilization_get_dashboard_stats;
DELIMITER $$
CREATE PROCEDURE sp_lab_utilization_get_dashboard_stats(
    IN p_faculty_id CHAR(36)
)
proc: BEGIN
    SELECT laboratory_id,COALESCE(SUM(time_out IS NULL),0) AS active_now,
    COALESCE(SUM(log_date=CURDATE()),0) AS sessions_today FROM attendance_logs
    WHERE p_faculty_id IS NULL OR faculty_in_charge=p_faculty_id GROUP BY laboratory_id;
END$$
DELIMITER ;

-- StudentRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_student_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_student_get_by_id(
    IN p_student_id VARCHAR(12)
)
proc: BEGIN
    SELECT
        student_id,
        first_name,
        last_name,
        middle_name,
        suffix
    FROM vw_students
    WHERE student_id = p_student_id;
END$$
DELIMITER ;

-- StudentRepository.FindByIdAsync
DROP PROCEDURE IF EXISTS sp_student_find_by_id;
DELIMITER $$
CREATE PROCEDURE sp_student_find_by_id(
    IN p_student_id VARCHAR(12)
)
proc: BEGIN
    SELECT
        student_id,
        first_name,
        last_name,
        middle_name,
        suffix
    FROM vw_students
    WHERE student_id = p_student_id;
END$$
DELIMITER ;

-- StudentRepository.GetExistingIdsAsync
DROP PROCEDURE IF EXISTS sp_student_get_existing_ids;
DELIMITER $$
CREATE PROCEDURE sp_student_get_existing_ids(
    IN p_student_ids JSON
)
proc: BEGIN
    IF p_student_ids IS NULL OR JSON_TYPE(p_student_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='StudentIds must be a JSON array.';
    END IF;
    SELECT student_id FROM vw_students WHERE student_id IN (SELECT id FROM JSON_TABLE(p_student_ids, '$[*]' COLUMNS(id VARCHAR(12) PATH '$' ERROR ON ERROR)) ids);
END$$
DELIMITER ;

-- StudentRepository.FindByIdsAsync
DROP PROCEDURE IF EXISTS sp_student_find_by_ids;
DELIMITER $$
CREATE PROCEDURE sp_student_find_by_ids(
    IN p_student_ids JSON
)
proc: BEGIN
    IF p_student_ids IS NULL OR JSON_TYPE(p_student_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='StudentIds must be a JSON array.';
    END IF;
    SELECT student_id,first_name,last_name,middle_name,suffix FROM vw_students WHERE student_id IN (SELECT id FROM JSON_TABLE(p_student_ids, '$[*]' COLUMNS(id VARCHAR(12) PATH '$' ERROR ON ERROR)) ids);
END$$
DELIMITER ;

-- StudentRepository.CreateManyAsync
-- JSON objects use snake_case keys. Existing student names remain unchanged.
DROP PROCEDURE IF EXISTS sp_student_create_many;
DELIMITER $$
CREATE PROCEDURE sp_student_create_many(
    IN p_students JSON
)
proc: BEGIN
    IF p_students IS NULL OR JSON_TYPE(p_students) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Students must be a JSON array.';
    END IF;
    INSERT INTO students(student_id,first_name,last_name,middle_name,suffix)
    SELECT r.student_id,r.first_name,r.last_name,r.middle_name,r.suffix
    FROM JSON_TABLE(p_students, '$[*]' COLUMNS(
        student_id VARCHAR(12) PATH '$.student_id' ERROR ON EMPTY ERROR ON ERROR,
        first_name VARCHAR(50) PATH '$.first_name' ERROR ON EMPTY ERROR ON ERROR,
        last_name VARCHAR(50) PATH '$.last_name' ERROR ON EMPTY ERROR ON ERROR,
        middle_name VARCHAR(50) PATH '$.middle_name' NULL ON EMPTY ERROR ON ERROR,
        suffix VARCHAR(5) PATH '$.suffix' NULL ON EMPTY ERROR ON ERROR
    )) r
    ON DUPLICATE KEY UPDATE student_id=students.student_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EnrollmentRepository.GetRosterAsync
DROP PROCEDURE IF EXISTS sp_enrollment_get_roster;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_get_roster(
    IN p_schedule_id INT
)
proc: BEGIN
    SELECT
        s.student_id,
        s.first_name,
        s.last_name,
        s.middle_name,
        s.suffix
    FROM student_enrollment se
    INNER JOIN students s
        ON s.student_id = se.student_id
    INNER JOIN class_schedule cs
        ON cs.class_schedule_id = se.class_schedule_id
    WHERE se.class_schedule_id = p_schedule_id
      AND cs.deleted_at IS NULL
    ORDER BY s.last_name, s.first_name;
END$$
DELIMITER ;

-- EnrollmentRepository.GetEnrolledStudentIdsAsync
DROP PROCEDURE IF EXISTS sp_enrollment_get_enrolled_student_ids;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_get_enrolled_student_ids(
    IN p_schedule_ids JSON
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    SELECT DISTINCT se.student_id FROM student_enrollment se INNER JOIN class_schedule cs ON cs.class_schedule_id=se.class_schedule_id
    WHERE cs.deleted_at IS NULL AND se.class_schedule_id IN (SELECT id FROM JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) ids);
END$$
DELIMITER ;

-- EnrollmentRepository.EnrollAsync
DROP PROCEDURE IF EXISTS sp_enrollment_enroll;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_enroll(
    IN p_schedule_ids JSON,
    IN p_student_ids JSON
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    IF p_student_ids IS NULL OR JSON_TYPE(p_student_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='StudentIds must be a JSON array.';
    END IF;
    INSERT INTO student_enrollment(student_id,class_schedule_id)
    SELECT students.id,slots.id FROM (SELECT DISTINCT id FROM JSON_TABLE(p_student_ids, '$[*]' COLUMNS(id VARCHAR(12) PATH '$' ERROR ON ERROR)) student_ids) students
    CROSS JOIN JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) slots
    WHERE TRUE
    ON DUPLICATE KEY UPDATE enrollment_id=student_enrollment.enrollment_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EnrollmentRepository.RemoveAsync
DROP PROCEDURE IF EXISTS sp_enrollment_remove;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_remove(
    IN p_schedule_ids JSON,
    IN p_student_id VARCHAR(12)
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    DELETE FROM student_enrollment WHERE student_id=p_student_id AND class_schedule_id IN (SELECT id FROM JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) ids);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EnrollmentRepository.DeleteAllAsync
DROP PROCEDURE IF EXISTS sp_enrollment_delete_all;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_delete_all(
    IN p_schedule_ids JSON
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    DELETE FROM student_enrollment WHERE class_schedule_id IN (SELECT id FROM JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) ids);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EnrollmentRepository.GetCountsAsync
DROP PROCEDURE IF EXISTS sp_enrollment_get_counts;
DELIMITER $$
CREATE PROCEDURE sp_enrollment_get_counts(
    IN p_schedule_ids JSON
)
proc: BEGIN
    IF p_schedule_ids IS NULL OR JSON_TYPE(p_schedule_ids) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='ScheduleIds must be a JSON array.';
    END IF;
    SELECT se.class_schedule_id,COUNT(DISTINCT se.student_id) AS cnt FROM student_enrollment se
    INNER JOIN class_schedule cs ON cs.class_schedule_id=se.class_schedule_id
    WHERE cs.deleted_at IS NULL AND se.class_schedule_id IN (SELECT id FROM JSON_TABLE(p_schedule_ids, '$[*]' COLUMNS(id INT PATH '$' ERROR ON ERROR)) ids)
    GROUP BY se.class_schedule_id;
END$$
DELIMITER ;

-- EquipmentRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_equipment_create;
DELIMITER $$
CREATE PROCEDURE sp_equipment_create(
    IN p_id CHAR(36),
    IN p_equipment_no VARCHAR(50),
    IN p_serial_number VARCHAR(100),
    IN p_equipment_name VARCHAR(150),
    IN p_brand VARCHAR(100),
    IN p_model_number VARCHAR(100),
    IN p_equipment_type VARCHAR(50),
    IN p_status VARCHAR(20),
    IN p_needs_pm BOOLEAN,
    IN p_needs_calibration BOOLEAN,
    IN p_remarks VARCHAR(255)
)
proc: BEGIN
    INSERT INTO equipment
    (
        equipment_id,
        equipment_no,
        serial_number,
        equipment_name,
        brand,
        model_number,
        equipment_type,
        status,
        needs_pm,
        needs_calibration,
        remarks
    )
    VALUES
    (
        p_id,
        p_equipment_no,
        p_serial_number,
        p_equipment_name,
        p_brand,
        p_model_number,
        p_equipment_type,
        p_status,
        p_needs_pm,
        p_needs_calibration,
        p_remarks
    );
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EquipmentRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_equipment_update;
DELIMITER $$
CREATE PROCEDURE sp_equipment_update(
    IN p_serial_number VARCHAR(100),
    IN p_equipment_name VARCHAR(150),
    IN p_brand VARCHAR(100),
    IN p_model_number VARCHAR(100),
    IN p_equipment_type VARCHAR(50),
    IN p_status VARCHAR(20),
    IN p_needs_pm BOOLEAN,
    IN p_needs_calibration BOOLEAN,
    IN p_remarks VARCHAR(255),
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE equipment
    SET
        serial_number = p_serial_number,
        equipment_name = p_equipment_name,
        brand = p_brand,
        model_number = p_model_number,
        equipment_type = p_equipment_type,
        status = p_status,
        needs_pm = p_needs_pm,
        needs_calibration = p_needs_calibration,
        remarks = p_remarks
    WHERE equipment_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EquipmentRepository.DeleteAsync
DROP PROCEDURE IF EXISTS sp_equipment_delete;
DELIMITER $$
CREATE PROCEDURE sp_equipment_delete(
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE equipment
    SET
        deleted_at = NOW()
    WHERE equipment_id = p_id
      AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EquipmentRepository.CreateAssignmentAsync
DROP PROCEDURE IF EXISTS sp_equipment_create_assignment;
DELIMITER $$
CREATE PROCEDURE sp_equipment_create_assignment(
    IN p_id CHAR(36),
    IN p_equipment_id CHAR(36),
    IN p_computer_unit_id CHAR(36),
    IN p_laboratory_id INT,
    IN p_status VARCHAR(20),
    IN p_assigned_at DATETIME
)
proc: BEGIN
    INSERT INTO equipment_assignments
    (
        assignment_id,
        equipment_id,
        computer_unit_id,
        laboratory_id,
        status,
        assigned_at
    )
    VALUES
    (
        p_id,
        p_equipment_id,
        p_computer_unit_id,
        p_laboratory_id,
        p_status,
        p_assigned_at
    );
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EquipmentRepository.GetActiveAssignmentAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_active_assignment;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_active_assignment(
    IN p_equipment_id CHAR(36)
)
proc: BEGIN
    SELECT
        assignment_id,
        equipment_id,
        computer_unit_id,
        laboratory_id,
        assigned_at
    FROM equipment_assignments
    WHERE equipment_id = p_equipment_id
      AND status = 'active'
    LIMIT 1;
END$$
DELIMITER ;

-- EquipmentRepository.GetAssignmentByIdAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_assignment_by_id;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_assignment_by_id(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT
        assignment_id,
        equipment_id,
        computer_unit_id,
        laboratory_id,
        status,
        end_reason,
        remarks,
        assigned_at,
        ended_at
    FROM equipment_assignments
    WHERE assignment_id = p_id;
END$$
DELIMITER ;

-- EquipmentRepository.EndAssignmentAsync
DROP PROCEDURE IF EXISTS sp_equipment_end_assignment;
DELIMITER $$
CREATE PROCEDURE sp_equipment_end_assignment(
    IN p_status VARCHAR(20),
    IN p_end_reason VARCHAR(20),
    IN p_remarks VARCHAR(255),
    IN p_ended_at DATETIME,
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE equipment_assignments
    SET
        status = p_status,
        end_reason = p_end_reason,
        remarks = p_remarks,
        ended_at = p_ended_at
    WHERE assignment_id = p_id
      AND status = 'active';
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- EquipmentRepository.GetAssignmentHistoryAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_assignment_history;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_assignment_history(
    IN p_equipment_id CHAR(36)
)
proc: BEGIN
    SELECT
        ea.assignment_id,
        ea.equipment_id,
        e.equipment_name,
        ea.computer_unit_id,
        cu.unit_name AS computer_unit_name,
        ea.laboratory_id,
        l.lab_name AS direct_laboratory_name,
        cu_lab.lab_name AS unit_laboratory_name,
        ea.status,
        ea.end_reason,
        ea.remarks,
        ea.assigned_at,
        ea.ended_at
    FROM equipment_assignments ea
    INNER JOIN equipment e
        ON ea.equipment_id = e.equipment_id
    LEFT JOIN computer_units cu
        ON ea.computer_unit_id = cu.computer_unit_id
    LEFT JOIN laboratories l
        ON ea.laboratory_id = l.lab_id
    LEFT JOIN laboratories cu_lab
        ON cu.laboratory_id = cu_lab.lab_id
    WHERE ea.equipment_id = p_equipment_id
    ORDER BY ea.assigned_at DESC;
END$$
DELIMITER ;

-- EquipmentRepository.GetAssignmentsForComputerUnitAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_assignments_for_computer_unit;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_assignments_for_computer_unit(
    IN p_computer_unit_id CHAR(36)
)
proc: BEGIN
    SELECT
        ea.assignment_id,
        ea.equipment_id,
        e.equipment_name,
        ea.computer_unit_id,
        cu.unit_name AS computer_unit_name,
        ea.laboratory_id,
        l.lab_name AS direct_laboratory_name,
        cu_lab.lab_name AS unit_laboratory_name,
        ea.status,
        ea.end_reason,
        ea.remarks,
        ea.assigned_at,
        ea.ended_at
    FROM equipment_assignments ea
    INNER JOIN equipment e
        ON ea.equipment_id = e.equipment_id
    LEFT JOIN computer_units cu
        ON ea.computer_unit_id = cu.computer_unit_id
    LEFT JOIN laboratories l
        ON ea.laboratory_id = l.lab_id
    LEFT JOIN laboratories cu_lab
        ON cu.laboratory_id = cu_lab.lab_id
    WHERE ea.computer_unit_id = p_computer_unit_id
      AND ea.status = 'active'
    ORDER BY ea.assigned_at DESC;
END$$
DELIMITER ;

-- EquipmentRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_all;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_all()
proc: BEGIN
    SELECT
        e.equipment_id,
        e.equipment_no,
        e.serial_number,
        e.equipment_name,
        e.brand,
        e.model_number,
        e.equipment_type,
        e.status,
        e.needs_pm,
        e.needs_calibration,
        e.remarks,
        ea.computer_unit_id AS current_computer_unit_id,
        cu.unit_name AS current_computer_unit_name,
        COALESCE(cu.laboratory_id, ea.laboratory_id) AS current_laboratory_id,
        COALESCE(cu_lab.lab_name, direct_lab.lab_name) AS current_laboratory_name
    FROM equipment e
    LEFT JOIN equipment_assignments ea
        ON ea.equipment_id = e.equipment_id AND ea.status = 'active'
    LEFT JOIN computer_units cu
        ON ea.computer_unit_id = cu.computer_unit_id
    LEFT JOIN laboratories cu_lab
        ON cu.laboratory_id = cu_lab.lab_id
    LEFT JOIN laboratories direct_lab
        ON ea.laboratory_id = direct_lab.lab_id
    WHERE e.deleted_at IS NULL ORDER BY e.equipment_no;
END$$
DELIMITER ;

-- EquipmentRepository.GetByIdAsync
DROP PROCEDURE IF EXISTS sp_equipment_get_by_id;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_by_id(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT
        e.equipment_id,
        e.equipment_no,
        e.serial_number,
        e.equipment_name,
        e.brand,
        e.model_number,
        e.equipment_type,
        e.status,
        e.needs_pm,
        e.needs_calibration,
        e.remarks,
        ea.computer_unit_id AS current_computer_unit_id,
        cu.unit_name AS current_computer_unit_name,
        COALESCE(cu.laboratory_id, ea.laboratory_id) AS current_laboratory_id,
        COALESCE(cu_lab.lab_name, direct_lab.lab_name) AS current_laboratory_name
    FROM equipment e
    LEFT JOIN equipment_assignments ea
        ON ea.equipment_id = e.equipment_id AND ea.status = 'active'
    LEFT JOIN computer_units cu
        ON ea.computer_unit_id = cu.computer_unit_id
    LEFT JOIN laboratories cu_lab
        ON cu.laboratory_id = cu_lab.lab_id
    LEFT JOIN laboratories direct_lab
        ON ea.laboratory_id = direct_lab.lab_id
    WHERE e.equipment_id=p_id AND e.deleted_at IS NULL;
END$$
DELIMITER ;

-- EquipmentRepository.GetNextEquipmentNoAsync
-- A suggested number, not a reservation; retain repository duplicate-key handling.
DROP PROCEDURE IF EXISTS sp_equipment_get_next_equipment_no;
DELIMITER $$
CREATE PROCEDURE sp_equipment_get_next_equipment_no()
proc: BEGIN
    DECLARE v_next BIGINT UNSIGNED;
    SELECT COALESCE(MAX(CAST(SUBSTRING(equipment_no,4) AS UNSIGNED)),0)+1 INTO v_next FROM equipment WHERE equipment_no REGEXP '^EQ-[0-9]+$';
    SELECT CONCAT('EQ-',LPAD(v_next,GREATEST(3,CHAR_LENGTH(v_next)),'0')) AS equipment_no;
END$$
DELIMITER ;

-- UnitMonitoringRepository.GetAllAsync
DROP PROCEDURE IF EXISTS sp_unit_monitoring_get_all;
DELIMITER $$
CREATE PROCEDURE sp_unit_monitoring_get_all()
proc: BEGIN
    SELECT u.computer_unit_id, u.unit_name, u.unit_number, u.laboratory_id,
           l.lab_name, a.ip_address, a.last_heartbeat,
           a.is_online, s.time_in AS session_started,
           CASE WHEN s.student_id IS NOT NULL
                THEN NULLIF(TRIM(CONCAT_WS(' ', st.first_name, st.last_name)), '')
                ELSE g.guest_name END AS session_user_name
    FROM computer_units u
    INNER JOIN laboratories l ON l.lab_id = u.laboratory_id
    LEFT JOIN unit_availability a ON a.computer_unit_id = u.computer_unit_id
    LEFT JOIN unit_utilization_logs s ON s.unit_utilization_log_id = (
        SELECT latest.unit_utilization_log_id
        FROM unit_utilization_logs latest
        WHERE latest.computer_unit_id = u.computer_unit_id AND latest.time_out IS NULL
        ORDER BY latest.time_in DESC, latest.unit_utilization_log_id DESC
        LIMIT 1
    )
    LEFT JOIN students st ON st.student_id = s.student_id
    LEFT JOIN guests g ON g.guest_id = s.guest_id
    WHERE u.deleted_at IS NULL
    ORDER BY l.lab_name, u.unit_number, u.unit_name;
END$$
DELIMITER ;

-- LogbookRepository.GetPagedAsync: caller uses a repeatable-read snapshot.
DROP PROCEDURE IF EXISTS sp_logbook_get_paged;
DELIMITER $$
CREATE PROCEDURE sp_logbook_get_paged(
    IN p_search TEXT, IN p_filter VARCHAR(20),
    IN p_page INT, IN p_page_size INT, IN p_laboratory_id INT
)
BEGIN
    DECLARE v_offset BIGINT UNSIGNED;
    DECLARE v_start DATE;
    DECLARE v_end DATE;
    IF p_page IS NULL OR p_page < 1 OR p_page_size IS NULL OR p_page_size < 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Page and PageSize must be positive.';
    END IF;
    SET v_offset=(CAST(p_page AS SIGNED)-1)*p_page_size;
    SET v_start=sf_logbook_period_start(p_filter,CURDATE());
    SET v_end=CASE p_filter WHEN 'day' THEN DATE_ADD(v_start,INTERVAL 1 DAY)
        WHEN 'week' THEN DATE_ADD(v_start,INTERVAL 7 DAY)
        WHEN 'month' THEN DATE_ADD(v_start,INTERVAL 1 MONTH) ELSE NULL END;
    SELECT COUNT(*) AS total_count FROM vw_logbook_entries
    WHERE (p_laboratory_id IS NULL OR laboratory_id=p_laboratory_id)
      AND (p_search IS NULL OR TRIM(p_search)='' OR purpose_subject LIKE CONCAT('%',p_search,'%')
        OR student_id LIKE CONCAT('%',p_search,'%') OR student_first_name LIKE CONCAT('%',p_search,'%')
        OR student_last_name LIKE CONCAT('%',p_search,'%') OR guest_name LIKE CONCAT('%',p_search,'%'))
      AND (v_start IS NULL OR (log_date >= v_start AND log_date < v_end));
    SELECT * FROM vw_logbook_entries
    WHERE (p_laboratory_id IS NULL OR laboratory_id=p_laboratory_id)
      AND (p_search IS NULL OR TRIM(p_search)='' OR purpose_subject LIKE CONCAT('%',p_search,'%')
        OR student_id LIKE CONCAT('%',p_search,'%') OR student_first_name LIKE CONCAT('%',p_search,'%')
        OR student_last_name LIKE CONCAT('%',p_search,'%') OR guest_name LIKE CONCAT('%',p_search,'%'))
      AND (v_start IS NULL OR (log_date >= v_start AND log_date < v_end))
    ORDER BY time_in DESC, utilization_log_id DESC LIMIT p_page_size OFFSET v_offset;
END$$
DELIMITER ;

-- LogbookRepository.GetAllFilteredAsync: same laboratory scope as paging.
DROP PROCEDURE IF EXISTS sp_logbook_get_all_filtered;
DELIMITER $$
CREATE PROCEDURE sp_logbook_get_all_filtered(
    IN p_search TEXT, IN p_filter VARCHAR(20), IN p_laboratory_id INT
)
BEGIN
    DECLARE v_start DATE;
    DECLARE v_end DATE;
    SET v_start=sf_logbook_period_start(p_filter,CURDATE());
    SET v_end=CASE p_filter WHEN 'day' THEN DATE_ADD(v_start,INTERVAL 1 DAY)
        WHEN 'week' THEN DATE_ADD(v_start,INTERVAL 7 DAY)
        WHEN 'month' THEN DATE_ADD(v_start,INTERVAL 1 MONTH) ELSE NULL END;
    SELECT * FROM vw_logbook_entries
    WHERE (p_laboratory_id IS NULL OR laboratory_id=p_laboratory_id)
      AND (p_search IS NULL OR TRIM(p_search)='' OR purpose_subject LIKE CONCAT('%',p_search,'%')
        OR student_id LIKE CONCAT('%',p_search,'%') OR student_first_name LIKE CONCAT('%',p_search,'%')
        OR student_last_name LIKE CONCAT('%',p_search,'%') OR guest_name LIKE CONCAT('%',p_search,'%'))
      AND (v_start IS NULL OR (log_date >= v_start AND log_date < v_end))
    ORDER BY time_in DESC, utilization_log_id DESC;
END$$
DELIMITER ;

-- UserRepository.AddAsync
DROP PROCEDURE IF EXISTS sp_user_add;
DELIMITER $$
CREATE PROCEDURE sp_user_add(
    IN p_id CHAR(36),
    IN p_first VARCHAR(50),
    IN p_last VARCHAR(50),
    IN p_middle VARCHAR(50),
    IN p_suffix VARCHAR(5),
    IN p_email VARCHAR(100),
    IN p_hash VARCHAR(255),
    IN p_role VARCHAR(20),
    IN p_changed BOOLEAN,
    IN p_two_factor BOOLEAN
)
proc: BEGIN
    INSERT INTO users(user_id,first_name,last_name,middle_name,suffix,email,password_hash,role_type,password_changed,two_factor_enabled)
    VALUES(p_id,p_first,p_last,p_middle,p_suffix,p_email,p_hash,p_role,p_changed,p_two_factor);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UserRepository.DeleteAsync
DROP PROCEDURE IF EXISTS sp_user_delete;
DELIMITER $$
CREATE PROCEDURE sp_user_delete(
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE users SET deleted_at=UTC_TIMESTAMP() WHERE user_id=p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UserRepository.FindAllAsync
DROP PROCEDURE IF EXISTS sp_user_find_all;
DELIMITER $$
CREATE PROCEDURE sp_user_find_all()
proc: BEGIN
    SELECT user_id,first_name,last_name,middle_name,suffix,email,role_type,password_hash,password_changed,created_at,last_modified_at,deleted_at,failed_login_attempts,lockout_end,two_factor_enabled FROM users;
END$$
DELIMITER ;

-- UserRepository.FindAllByRoleAsync
DROP PROCEDURE IF EXISTS sp_user_find_all_by_role;
DELIMITER $$
CREATE PROCEDURE sp_user_find_all_by_role(
    IN p_role VARCHAR(20)
)
proc: BEGIN
    SELECT user_id,first_name,last_name,middle_name,suffix,email,role_type,password_hash,password_changed,created_at,last_modified_at,deleted_at,failed_login_attempts,lockout_end,two_factor_enabled FROM users WHERE role_type=p_role;
END$$
DELIMITER ;

-- UserRepository.FindByEmailAsync
DROP PROCEDURE IF EXISTS sp_user_find_by_email;
DELIMITER $$
CREATE PROCEDURE sp_user_find_by_email(
    IN p_email VARCHAR(100)
)
proc: BEGIN
    SELECT user_id,first_name,last_name,middle_name,suffix,email,role_type,password_hash,password_changed,created_at,last_modified_at,deleted_at,failed_login_attempts,lockout_end,two_factor_enabled FROM users WHERE LOWER(TRIM(email))=LOWER(TRIM(p_email)) LIMIT 1;
END$$
DELIMITER ;

-- UserRepository.FindByIdAsync
DROP PROCEDURE IF EXISTS sp_user_find_by_id;
DELIMITER $$
CREATE PROCEDURE sp_user_find_by_id(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT user_id,first_name,last_name,middle_name,suffix,email,role_type,password_hash,password_changed,created_at,last_modified_at,deleted_at,failed_login_attempts,lockout_end,two_factor_enabled FROM users WHERE user_id=p_id LIMIT 1;
END$$
DELIMITER ;

-- UserRepository.UpdateAsync
DROP PROCEDURE IF EXISTS sp_user_update;
DELIMITER $$
CREATE PROCEDURE sp_user_update(
    IN p_id CHAR(36),
    IN p_first VARCHAR(50),
    IN p_last VARCHAR(50),
    IN p_middle VARCHAR(50),
    IN p_suffix VARCHAR(5),
    IN p_email VARCHAR(100),
    IN p_role VARCHAR(20)
)
proc: BEGIN
    UPDATE users SET first_name=p_first,last_name=p_last,middle_name=p_middle,suffix=p_suffix,
    reset_token=IF(BINARY email=BINARY p_email,reset_token,NULL),
    reset_token_expiration=IF(BINARY email=BINARY p_email,reset_token_expiration,NULL),email=p_email,
    role_type=p_role,last_modified_at=UTC_TIMESTAMP() WHERE user_id=p_id AND deleted_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UserRepository.ChangePasswordAsync
-- Owns its transaction; preserves security snapshot comparison and revokes refresh tokens.
DROP PROCEDURE IF EXISTS sp_user_change_password;
DELIMITER $$
CREATE PROCEDURE sp_user_change_password(
    IN p_id CHAR(36),
    IN p_expected_hash VARCHAR(255),
    IN p_new_hash VARCHAR(255)
)
proc: BEGIN
    DECLARE v_rows INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    UPDATE users SET password_hash=p_new_hash,password_changed=1,last_modified_at=UTC_TIMESTAMP(),reset_token=NULL,reset_token_expiration=NULL
    WHERE user_id=p_id AND deleted_at IS NULL AND BINARY password_hash=BINARY p_expected_hash;
    SET v_rows=ROW_COUNT();
    IF v_rows<>1 THEN ROLLBACK; SELECT 0 AS succeeded; LEAVE proc; END IF;
    UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=p_id AND revoked_at IS NULL;
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- UserRepository.ChangeTwoFactorAsync
-- Owns its transaction; preserves security snapshot comparison and revokes refresh tokens.
DROP PROCEDURE IF EXISTS sp_user_change_two_factor;
DELIMITER $$
CREATE PROCEDURE sp_user_change_two_factor(
    IN p_id CHAR(36),
    IN p_enabled BOOLEAN,
    IN p_replacement VARCHAR(255),
    IN p_expected_hash VARCHAR(255),
    IN p_expected_email VARCHAR(100),
    IN p_expected_role VARCHAR(20),
    IN p_expected_setting BOOLEAN
)
proc: BEGIN
    DECLARE v_rows INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    UPDATE users SET two_factor_enabled=p_enabled,password_hash=p_replacement,reset_token=NULL,reset_token_expiration=NULL,last_modified_at=UTC_TIMESTAMP()
    WHERE user_id=p_id AND deleted_at IS NULL AND (lockout_end IS NULL OR lockout_end<=UTC_TIMESTAMP())
    AND BINARY password_hash=BINARY p_expected_hash AND BINARY email=BINARY p_expected_email
    AND role_type=p_expected_role AND password_changed=1 AND two_factor_enabled=p_expected_setting;
    SET v_rows=ROW_COUNT();
    IF v_rows<>1 THEN ROLLBACK; SELECT 0 AS succeeded; LEAVE proc; END IF;
    UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=p_id AND revoked_at IS NULL;
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- UserRepository.IncrementFailedLoginAttemptsAsync
DROP PROCEDURE IF EXISTS sp_user_increment_failed_login_attempts;
DELIMITER $$
CREATE PROCEDURE sp_user_increment_failed_login_attempts(
    IN p_user_id CHAR(36)
)
proc: BEGIN
    UPDATE users
    SET failed_login_attempts = failed_login_attempts + 1
    WHERE user_id = p_user_id;

    SELECT failed_login_attempts
    FROM users
    WHERE user_id = p_user_id;
END$$
DELIMITER ;

-- UserRepository.LockOutAsync
DROP PROCEDURE IF EXISTS sp_user_lock_out;
DELIMITER $$
CREATE PROCEDURE sp_user_lock_out(
    IN p_lockout_end DATETIME,
    IN p_user_id CHAR(36)
)
proc: BEGIN
    UPDATE users
    SET
        failed_login_attempts = 0,
        lockout_end = p_lockout_end
    WHERE user_id = p_user_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UserRepository.ResetFailedLoginAttemptsAsync
DROP PROCEDURE IF EXISTS sp_user_reset_failed_login_attempts;
DELIMITER $$
CREATE PROCEDURE sp_user_reset_failed_login_attempts(
    IN p_user_id CHAR(36)
)
proc: BEGIN
    UPDATE users
    SET
        failed_login_attempts = 0,
        lockout_end = NULL
    WHERE user_id = p_user_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ResetTokenRepository.FindByUserIdAsync
DROP PROCEDURE IF EXISTS sp_reset_token_find_by_user_id;
DELIMITER $$
CREATE PROCEDURE sp_reset_token_find_by_user_id(
    IN p_user_id CHAR(36)
)
proc: BEGIN
    SELECT
        user_id,
        reset_token,
        reset_token_expiration
    FROM users
    WHERE user_id = p_user_id
    LIMIT 1;
END$$
DELIMITER ;

-- ResetTokenRepository.CreateAsync
-- Caller validates a 64-character SHA-256 hexadecimal hash before calling.
DROP PROCEDURE IF EXISTS sp_reset_token_create;
DELIMITER $$
CREATE PROCEDURE sp_reset_token_create(
    IN p_reset_token VARCHAR(64),
    IN p_reset_token_expires_at DATETIME(6),
    IN p_user_id CHAR(36),
    IN p_email VARCHAR(100)
)
proc: BEGIN
    UPDATE users
    SET
        reset_token = p_reset_token,
        reset_token_expiration = p_reset_token_expires_at
    WHERE
        user_id = p_user_id AND deleted_at IS NULL AND BINARY email=BINARY p_email;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ResetTokenRepository.ConsumeAsync
-- Owns its transaction; preserves security snapshot comparison and revokes refresh tokens.
DROP PROCEDURE IF EXISTS sp_reset_token_consume;
DELIMITER $$
CREATE PROCEDURE sp_reset_token_consume(
    IN p_id CHAR(36),
    IN p_expected_token_hash VARCHAR(64),
    IN p_expected_password_hash VARCHAR(255),
    IN p_new_password_hash VARCHAR(255)
)
proc: BEGIN
    DECLARE v_rows INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    UPDATE users SET password_hash=p_new_password_hash,password_changed=1,reset_token=NULL,reset_token_expiration=NULL,
    failed_login_attempts=0,lockout_end=NULL,last_modified_at=UTC_TIMESTAMP()
    WHERE user_id=p_id AND deleted_at IS NULL AND BINARY reset_token=BINARY p_expected_token_hash
    AND BINARY password_hash=BINARY p_expected_password_hash AND reset_token_expiration>UTC_TIMESTAMP();
    SET v_rows=ROW_COUNT();
    IF v_rows<>1 THEN ROLLBACK; SELECT 0 AS succeeded; LEAVE proc; END IF;
    UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=p_id AND revoked_at IS NULL;
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- RefreshTokenRepository.GetActiveByUserIdAsync
DROP PROCEDURE IF EXISTS sp_refresh_token_get_active_by_user_id;
DELIMITER $$
CREATE PROCEDURE sp_refresh_token_get_active_by_user_id(
    IN p_user_id CHAR(36)
)
proc: BEGIN
    SELECT
        id,
        user_id,
        token_hash,
        created_at,
        expires_at,
        revoked_at
    FROM refresh_tokens
    WHERE user_id = p_user_id
      AND revoked_at IS NULL
      AND expires_at > UTC_TIMESTAMP()
    ORDER BY created_at ASC;
END$$
DELIMITER ;

-- RefreshTokenRepository.AddAsync
DROP PROCEDURE IF EXISTS sp_refresh_token_add;
DELIMITER $$
CREATE PROCEDURE sp_refresh_token_add(
    IN p_id CHAR(36),
    IN p_user_id CHAR(36),
    IN p_token_hash VARCHAR(255),
    IN p_expires_at DATETIME(6),
    IN p_revoked_at DATETIME(6)
)
proc: BEGIN
    INSERT INTO refresh_tokens
        (id, user_id, token_hash, expires_at, revoked_at)
    VALUES
        (p_id, p_user_id, p_token_hash, p_expires_at, p_revoked_at);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- RefreshTokenRepository.FindByHashAsync
DROP PROCEDURE IF EXISTS sp_refresh_token_find_by_hash;
DELIMITER $$
CREATE PROCEDURE sp_refresh_token_find_by_hash(
    IN p_token_hash VARCHAR(255)
)
proc: BEGIN
    SELECT
        id,
        user_id,
        token_hash,
        expires_at,
        created_at,
        revoked_at
    FROM refresh_tokens
    WHERE token_hash = p_token_hash
    ORDER BY created_at DESC
    LIMIT 1;
END$$
DELIMITER ;

-- RefreshTokenRepository.RevokeAsync
DROP PROCEDURE IF EXISTS sp_refresh_token_revoke;
DELIMITER $$
CREATE PROCEDURE sp_refresh_token_revoke(
    IN p_revoked_at DATETIME(6),
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE refresh_tokens
    SET revoked_at = p_revoked_at
    WHERE id = p_id
      AND revoked_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- RefreshTokenRepository.TryIssueAsync
-- Owns its transaction. Locks user before snapshot validation, rotation, eviction and issue.
DROP PROCEDURE IF EXISTS sp_refresh_token_try_issue;
DELIMITER $$
CREATE PROCEDURE sp_refresh_token_try_issue(
    IN p_id CHAR(36),
    IN p_expected_hash VARCHAR(255),
    IN p_expected_email VARCHAR(100),
    IN p_expected_role VARCHAR(20),
    IN p_expected_changed BOOLEAN,
    IN p_expected_two_factor BOOLEAN,
    IN p_replacement_id CHAR(36),
    IN p_replacement_user_id CHAR(36),
    IN p_token_hash VARCHAR(255),
    IN p_created_at DATETIME(6),
    IN p_expires_at DATETIME(6),
    IN p_predecessor_hash VARCHAR(255),
    IN p_max_sessions INT
)
proc: BEGIN
    DECLARE v_id CHAR(36) DEFAULT NULL;
    DECLARE v_hash VARCHAR(255);
    DECLARE v_email VARCHAR(100);
    DECLARE v_role VARCHAR(20);
    DECLARE v_changed BOOLEAN;
    DECLARE v_two_factor BOOLEAN;
    DECLARE v_deleted DATETIME;
    DECLARE v_lockout DATETIME;
    DECLARE v_revoke_count INT DEFAULT 0;
    DECLARE v_token CHAR(36);
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    IF p_max_sessions IS NULL OR p_max_sessions<1 OR NOT (p_replacement_user_id <=> p_id) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Invalid session limit or replacement user.';
    END IF;
    START TRANSACTION;
    SELECT user_id,password_hash,email,role_type,password_changed,two_factor_enabled,deleted_at,lockout_end
    INTO v_id,v_hash,v_email,v_role,v_changed,v_two_factor,v_deleted,v_lockout
    FROM users WHERE user_id=p_id FOR UPDATE;
    IF v_id IS NULL OR v_deleted IS NOT NULL OR v_lockout>UTC_TIMESTAMP()
    OR NOT (BINARY v_hash <=> BINARY p_expected_hash) OR NOT (BINARY v_email <=> BINARY p_expected_email)
    OR NOT (v_role <=> p_expected_role) OR NOT (v_changed <=> p_expected_changed) OR NOT (v_two_factor <=> p_expected_two_factor) THEN
        ROLLBACK; SELECT 0 AS succeeded; LEAVE proc;
    END IF;
    IF p_predecessor_hash IS NOT NULL THEN
        UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=p_id AND token_hash=p_predecessor_hash AND revoked_at IS NULL AND expires_at>UTC_TIMESTAMP(6);
        IF ROW_COUNT()<>1 THEN ROLLBACK; SELECT 0 AS succeeded; LEAVE proc; END IF;
    END IF;
    SELECT GREATEST(0,COUNT(*)-p_max_sessions+1) INTO v_revoke_count FROM refresh_tokens
    WHERE user_id=p_id AND revoked_at IS NULL AND expires_at>UTC_TIMESTAMP(6);
    WHILE v_revoke_count>0 DO
        SELECT id INTO v_token FROM refresh_tokens WHERE user_id=p_id AND revoked_at IS NULL AND expires_at>UTC_TIMESTAMP(6) ORDER BY created_at,id LIMIT 1;
        UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE id=v_token;
        SET v_revoke_count=v_revoke_count-1;
    END WHILE;
    INSERT INTO refresh_tokens(id,user_id,token_hash,created_at,expires_at) VALUES(p_replacement_id,p_replacement_user_id,p_token_hash,p_created_at,p_expires_at);
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- ExternalIdentityRepository.FindAsync
DROP PROCEDURE IF EXISTS sp_external_identity_find;
DELIMITER $$
CREATE PROCEDURE sp_external_identity_find(
    IN p_provider VARCHAR(50),
    IN p_provider_sub VARCHAR(255)
)
proc: BEGIN
    SELECT
        id,
        user_id
    FROM external_identities
    WHERE
        provider = p_provider AND
        provider_sub = p_provider_sub;
END$$
DELIMITER ;

-- ExternalIdentityRepository.FindByIdAsync
DROP PROCEDURE IF EXISTS sp_external_identity_find_by_id;
DELIMITER $$
CREATE PROCEDURE sp_external_identity_find_by_id(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT
        user_id,
        provider,
        provider_sub
    FROM external_identities
    WHERE
        id = p_id;
END$$
DELIMITER ;

-- ExternalIdentityRepository.CreateAsync
DROP PROCEDURE IF EXISTS sp_external_identity_create;
DELIMITER $$
CREATE PROCEDURE sp_external_identity_create(
    IN p_id CHAR(36),
    IN p_user_id CHAR(36),
    IN p_provider VARCHAR(50),
    IN p_provider_sub VARCHAR(255)
)
proc: BEGIN
    INSERT INTO external_identities
    (
         id,
         user_id,
         provider,
         provider_sub
    )
    VALUES
    (
         p_id,
         p_user_id,
         p_provider,
         p_provider_sub
    );
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ExternalIdentityRepository.LinkGoogleAsync
-- Owns its READ COMMITTED transaction; unique indexes arbitrate cross-account races.
DROP PROCEDURE IF EXISTS sp_external_identity_link_google;
DELIMITER $$
CREATE PROCEDURE sp_external_identity_link_google(
    IN p_user_id CHAR(36),
    IN p_email VARCHAR(100),
    IN p_subject VARCHAR(255),
    IN p_expected_password_hash VARCHAR(255)
)
proc: BEGIN
    DECLARE v_id CHAR(36) DEFAULT NULL;
    DECLARE v_linked INT DEFAULT 0;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;
    SELECT user_id INTO v_id FROM users WHERE user_id=p_user_id AND deleted_at IS NULL
    AND LOWER(TRIM(email))=LOWER(TRIM(p_email)) AND BINARY password_hash=BINARY p_expected_password_hash FOR UPDATE;
    IF v_id IS NULL THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='The account changed. Sign in again and retry.'; END IF;
    -- Lock matching identities and compare subjects exactly, as the C# reader does.
    BEGIN
        DECLARE v_done BOOLEAN DEFAULT FALSE;
        DECLARE v_user CHAR(36);
        DECLARE v_subject VARCHAR(255);
        DECLARE identities CURSOR FOR SELECT user_id,provider_sub FROM external_identities
            WHERE provider='google' AND (user_id=p_user_id OR provider_sub=p_subject) FOR UPDATE;
        DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done=TRUE;
        OPEN identities;
        identity_loop: LOOP
            FETCH identities INTO v_user,v_subject;
            IF v_done THEN LEAVE identity_loop; END IF;
            IF NOT(v_user <=> p_user_id) OR NOT(BINARY v_subject <=> BINARY p_subject) THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='This account or Google identity is already linked.';
            END IF;
            SET v_linked=1;
        END LOOP;
        CLOSE identities;
    END;
    IF v_linked=0 THEN INSERT INTO external_identities(id,user_id,provider,provider_sub) VALUES(UUID(),p_user_id,'google',p_subject); END IF;
    COMMIT;
    SELECT 1 AS succeeded;
END$$
DELIMITER ;

-- ScreenCastRepository.CreateAsync
-- Owns its transaction. Caller supplies the cryptographically random token generated by C#.
DROP PROCEDURE IF EXISTS sp_screen_cast_create;
DELIMITER $$
CREATE PROCEDURE sp_screen_cast_create(
    IN p_initiated_by CHAR(36),
    IN p_units JSON,
    IN p_started_at DATETIME,
    IN p_session_token VARCHAR(255)
)
proc: BEGIN
    DECLARE v_id INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    IF p_units IS NULL OR JSON_TYPE(p_units) <> 'ARRAY' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Units must be a JSON array.';
    END IF;

    START TRANSACTION;
    INSERT INTO screen_cast_sessions(initiated_by,status,started_at,session_token) VALUES(p_initiated_by,'active',p_started_at,p_session_token);
    SET v_id=LAST_INSERT_ID();
    INSERT INTO screen_cast_connections(cast_session_id,computer_unit_id,status)
    SELECT v_id,id,'disconnected' FROM JSON_TABLE(p_units,'$[*]' COLUMNS(id CHAR(36) PATH '$' ERROR ON ERROR)) units;
    COMMIT;
    SELECT v_id AS id;
END$$
DELIMITER ;

-- ScreenCastRepository.SetConnectionAsync
DROP PROCEDURE IF EXISTS sp_screen_cast_set_connection;
DELIMITER $$
CREATE PROCEDURE sp_screen_cast_set_connection(
    IN p_session_id INT,
    IN p_unit_id CHAR(36),
    IN p_connected BOOLEAN,
    IN p_at DATETIME
)
proc: BEGIN
    UPDATE screen_cast_connections c INNER JOIN screen_cast_sessions s ON s.cast_session_id=c.cast_session_id
    SET c.status=IF(p_connected,'connected','disconnected'),c.started_at=IF(p_connected,p_at,c.started_at),c.ended_at=IF(p_connected,NULL,p_at)
    WHERE c.cast_session_id=p_session_id AND c.computer_unit_id=p_unit_id AND s.status='active';
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- ScreenCastRepository.EndAsync
-- Owns its transaction. Null SessionId closes all active sessions, like recovery.
DROP PROCEDURE IF EXISTS sp_screen_cast_end;
DELIMITER $$
CREATE PROCEDURE sp_screen_cast_end(
    IN p_session_id INT,
    IN p_failed BOOLEAN,
    IN p_at DATETIME
)
proc: BEGIN
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    START TRANSACTION;
    UPDATE screen_cast_connections c INNER JOIN screen_cast_sessions s ON s.cast_session_id=c.cast_session_id
    SET c.status='disconnected',c.ended_at=COALESCE(c.ended_at,p_at)
    WHERE s.status='active' AND (p_session_id IS NULL OR s.cast_session_id=p_session_id);
    UPDATE screen_cast_sessions SET status=IF(p_failed,'failed','ended'),ended_at=p_at
    WHERE status='active' AND (p_session_id IS NULL OR cast_session_id=p_session_id);
    COMMIT;
END$$
DELIMITER ;

-- ScreenCastRepository.RecoverInterruptedAsync
DROP PROCEDURE IF EXISTS sp_screen_cast_recover_interrupted;
DELIMITER $$
CREATE PROCEDURE sp_screen_cast_recover_interrupted(
    IN p_at DATETIME
)
proc: BEGIN
    CALL sp_screen_cast_end(NULL,TRUE,p_at);
END$$
DELIMITER ;

-- UnitRegistryRepository.UnitsAsync
-- Two result sets: total_count, then the page.
DROP PROCEDURE IF EXISTS sp_unit_registry_units;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_units(
    IN p_lab INT,
    IN p_search VARCHAR(255),
    IN p_page INT,
    IN p_size INT
)
proc: BEGIN
    DECLARE v_offset BIGINT UNSIGNED;
    IF p_page IS NULL OR p_page<1 OR p_size IS NULL OR p_size<1 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Page and Size must be positive.'; END IF;
    SET v_offset=(CAST(p_page AS SIGNED)-1)*p_size;
    SELECT COUNT(*) AS total_count FROM computer_units u JOIN laboratories l ON l.lab_id=u.laboratory_id WHERE u.deleted_at IS NULL
    AND (p_lab IS NULL OR u.laboratory_id=p_lab)
    AND (p_search IS NULL OR TRIM(p_search)='' OR CAST(u.unit_number AS CHAR) LIKE CONCAT('%',TRIM(p_search),'%'));
    SELECT u.computer_unit_id AS Id,u.laboratory_id AS LaboratoryId,COALESCE(l.lab_name,'') AS Laboratory,
    u.unit_number AS UnitNumber,u.unit_name AS UnitName,u.hostname AS Hostname,u.operating_system AS OperatingSystem,
    u.agent_version AS AgentVersion,u.is_enabled AS Enabled,u.registered_at AS RegisteredAtUtc,u.last_seen_at AS LastSeenUtc,
    (u.is_enabled=1 AND u.last_seen_at>=UTC_TIMESTAMP(6)-INTERVAL 2 MINUTE) AS Online
    FROM computer_units u JOIN laboratories l ON l.lab_id=u.laboratory_id WHERE u.deleted_at IS NULL
    AND (p_lab IS NULL OR u.laboratory_id=p_lab)
    AND (p_search IS NULL OR TRIM(p_search)='' OR CAST(u.unit_number AS CHAR) LIKE CONCAT('%',TRIM(p_search),'%')) ORDER BY l.lab_name,u.unit_number LIMIT p_size OFFSET v_offset;
END$$
DELIMITER ;

-- UnitRegistryRepository.RegistrationsAsync
-- Two result sets; pending rows past expiry are reported as Expired.
DROP PROCEDURE IF EXISTS sp_unit_registry_registrations;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_registrations(
    IN p_status VARCHAR(20),
    IN p_page INT,
    IN p_size INT
)
proc: BEGIN
    DECLARE v_offset BIGINT UNSIGNED;
    DECLARE v_as_of_utc DATETIME(6);
    IF p_page IS NULL OR p_page<1 OR p_size IS NULL OR p_size<1 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Page and Size must be positive.'; END IF;
    SET v_offset=(CAST(p_page AS SIGNED)-1)*p_size;
    SET v_as_of_utc=UTC_TIMESTAMP(6);
    SELECT COUNT(*) AS total_count FROM unit_registrations r JOIN laboratories l ON l.lab_id=r.laboratory_id WHERE (p_status IS NULL OR (sf_registration_effective_status(r.status,r.expires_at,v_as_of_utc))=p_status);
    SELECT r.registration_id AS Id,r.laboratory_id AS LaboratoryId,COALESCE(l.lab_name,'') AS Laboratory,
    r.unit_number AS UnitNumber,r.registration_code AS Code,r.hostname AS Hostname,r.operating_system AS OperatingSystem,
    r.agent_version AS AgentVersion,sf_registration_effective_status(r.status,r.expires_at,v_as_of_utc) AS Status,r.created_at AS CreatedAtUtc,r.expires_at AS ExpiresAtUtc,
    r.decided_at AS DecidedAtUtc,r.key_fingerprint AS KeyFingerprint FROM unit_registrations r JOIN laboratories l ON l.lab_id=r.laboratory_id WHERE (p_status IS NULL OR (sf_registration_effective_status(r.status,r.expires_at,v_as_of_utc))=p_status)
    ORDER BY r.created_at DESC,r.registration_id LIMIT p_size OFFSET v_offset;
END$$
DELIMITER ;

-- UnitRegistryRepository.IsAuthorizedAsync
DROP PROCEDURE IF EXISTS sp_unit_registry_is_authorized;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_is_authorized(
    IN p_id CHAR(36),
    IN p_generation INT,
    IN p_credential CHAR(36)
)
proc: BEGIN
    SELECT EXISTS(SELECT 1 FROM computer_units u WHERE u.computer_unit_id=p_id AND u.deleted_at IS NULL
    AND u.is_enabled=1 AND u.credential_generation=p_generation AND EXISTS(
    SELECT 1 FROM device_credentials c WHERE c.computer_unit_id=u.computer_unit_id AND c.credential_generation=p_generation
    AND c.credential_id=p_credential AND c.revoked_at IS NULL AND c.activated_at IS NOT NULL)) AS is_authorized;
END$$
DELIMITER ;

-- UnitRegistryRepository.CleanupChallengesAsync
DROP PROCEDURE IF EXISTS sp_unit_registry_cleanup_challenges;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_cleanup_challenges()
proc: BEGIN
    DELETE FROM device_auth_challenges WHERE expires_at<UTC_TIMESTAMP(6)-INTERVAL 24 HOUR LIMIT 10000;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.TransactionAsync
-- Caller MUST start a transaction; this only resolves/locks the lab and returns the transaction clock. C# callback/retry stays outside SQL.
DROP PROCEDURE IF EXISTS sp_unit_registry_transaction;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_transaction(
    IN p_kind VARCHAR(20),
    IN p_key VARCHAR(36)
)
proc: BEGIN
    DECLARE v_lab INT DEFAULT NULL;
    DECLARE v_locked INT DEFAULT NULL;
    IF p_kind='Laboratory' THEN SET v_lab=CAST(p_key AS UNSIGNED);
    ELSEIF p_kind='Registration' THEN SELECT laboratory_id INTO v_lab FROM unit_registrations WHERE registration_id=p_key;
    ELSEIF p_kind='Unit' THEN SELECT laboratory_id INTO v_lab FROM computer_units WHERE computer_unit_id=p_key;
    ELSE SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Unknown registry lock kind.';
    END IF;
    IF v_lab IS NULL THEN SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='Registry target is unavailable.'; END IF;
    SELECT lab_id INTO v_locked FROM laboratories WHERE lab_id=v_lab FOR UPDATE;
    IF v_locked IS NULL THEN SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30002, MESSAGE_TEXT='Laboratory is unavailable.'; END IF;
    SELECT UTC_TIMESTAMP(6) AS now_utc;
END$$
DELIMITER ;

-- UnitRegistryRepository.ExpirePendingAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_expire_pending;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_expire_pending(
    IN p_laboratory_id INT,
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE unit_registrations SET status='Expired',last_modified_at=p_now WHERE laboratory_id=p_laboratory_id AND status='Pending' AND expires_at<=p_now;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.HasPendingAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_has_pending;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_has_pending(
    IN p_laboratory_id INT,
    IN p_unit_number TINYINT UNSIGNED
)
proc: BEGIN
    SELECT EXISTS(SELECT 1 FROM unit_registrations WHERE laboratory_id=p_laboratory_id AND unit_number=p_unit_number AND status='Pending') AS has_pending;
END$$
DELIMITER ;

-- UnitRegistryRepository.RegistrationAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_registration;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_registration(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT registration_id AS Id,laboratory_id AS LaboratoryId,unit_number AS UnitNumber,registration_code AS Code,
    secret_hash AS SecretHash,public_key AS PublicKey,key_fingerprint AS KeyFingerprint,device_credential_id AS DeviceCredentialId,
    hostname AS Hostname,operating_system AS OperatingSystem,agent_version AS AgentVersion,status AS Status,
    created_at AS CreatedAtUtc,expires_at AS ExpiresAtUtc,decided_at AS DecidedAtUtc,decided_by AS DecidedBy,
    computer_unit_id AS ComputerUnitId,credential_generation AS CredentialGeneration,claimed_at AS ClaimedAtUtc
    FROM unit_registrations WHERE registration_id=p_id FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.InsertRegistrationAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_insert_registration;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_insert_registration(
    IN p_id CHAR(36),
    IN p_laboratory_id INT,
    IN p_unit_number TINYINT UNSIGNED,
    IN p_code CHAR(8) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_secret_hash CHAR(44) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_public_key VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_key_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_hostname VARCHAR(255),
    IN p_operating_system VARCHAR(255),
    IN p_agent_version VARCHAR(64),
    IN p_created_at_utc DATETIME(6),
    IN p_expires_at_utc DATETIME(6)
)
proc: BEGIN
    INSERT INTO unit_registrations(registration_id,laboratory_id,unit_number,registration_code,secret_hash,public_key,key_fingerprint,hostname,operating_system,agent_version,status,created_at,expires_at,last_modified_at)
    VALUES(p_id,p_laboratory_id,p_unit_number,p_code,p_secret_hash,p_public_key,p_key_fingerprint,p_hostname,p_operating_system,p_agent_version,'Pending',p_created_at_utc,p_expires_at_utc,p_created_at_utc);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.SaveRegistrationAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_save_registration;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_save_registration(
    IN p_id CHAR(36),
    IN p_status VARCHAR(20),
    IN p_decided_by CHAR(36),
    IN p_decided_at_utc DATETIME(6),
    IN p_computer_unit_id CHAR(36),
    IN p_credential_generation INT,
    IN p_device_credential_id CHAR(36),
    IN p_claimed_at_utc DATETIME(6),
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE unit_registrations SET status=p_status,decided_by=p_decided_by,decided_at=p_decided_at_utc,computer_unit_id=p_computer_unit_id,
    credential_generation=p_credential_generation,device_credential_id=p_device_credential_id,claimed_at=p_claimed_at_utc,last_modified_at=p_now WHERE registration_id=p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.AssignmentAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_assignment;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_assignment(
    IN p_laboratory_id INT,
    IN p_number TINYINT UNSIGNED
)
proc: BEGIN
    SELECT computer_unit_id AS Id,unit_name AS UnitName,unit_number AS UnitNumber,laboratory_id AS LaboratoryId,created_at AS CreatedAt,last_modified_at AS LastModifiedAt,deleted_at AS DeletedAt,hostname AS Hostname,operating_system AS OperatingSystem,agent_version AS AgentVersion,is_enabled AS Enabled,credential_generation AS CredentialGeneration,registered_at AS RegisteredAtUtc FROM computer_units WHERE laboratory_id=p_laboratory_id AND unit_number=p_number FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.UnitAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_unit;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_unit(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT computer_unit_id AS Id,unit_name AS UnitName,unit_number AS UnitNumber,laboratory_id AS LaboratoryId,created_at AS CreatedAt,last_modified_at AS LastModifiedAt,deleted_at AS DeletedAt,hostname AS Hostname,operating_system AS OperatingSystem,agent_version AS AgentVersion,is_enabled AS Enabled,credential_generation AS CredentialGeneration,registered_at AS RegisteredAtUtc FROM computer_units WHERE computer_unit_id=p_id FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.SaveUnitAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_save_unit;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_save_unit(
    IN p_id CHAR(36),
    IN p_unit_name VARCHAR(100),
    IN p_unit_number TINYINT UNSIGNED,
    IN p_laboratory_id INT,
    IN p_hostname VARCHAR(255),
    IN p_operating_system VARCHAR(255),
    IN p_agent_version VARCHAR(64),
    IN p_enabled BOOLEAN,
    IN p_credential_generation INT,
    IN p_registered_at_utc DATETIME(6),
    IN p_created_at DATETIME(6),
    IN p_last_modified_at DATETIME(6),
    IN p_deleted_at DATETIME,
    IN p_create BOOLEAN
)
proc: BEGIN
    IF p_create THEN
    INSERT INTO computer_units(computer_unit_id,unit_name,unit_number,laboratory_id,hostname,operating_system,agent_version,is_enabled,credential_generation,registered_at,created_at,last_modified_at,deleted_at)
    VALUES(p_id,p_unit_name,p_unit_number,p_laboratory_id,p_hostname,p_operating_system,p_agent_version,p_enabled,p_credential_generation,p_registered_at_utc,p_created_at,p_last_modified_at,p_deleted_at);
    ELSE
    UPDATE computer_units SET hostname=p_hostname,operating_system=p_operating_system,agent_version=p_agent_version,is_enabled=p_enabled,
    credential_generation=p_credential_generation,registered_at=p_registered_at_utc,last_modified_at=p_last_modified_at,deleted_at=p_deleted_at WHERE computer_unit_id=p_id;
    END IF;
END$$
DELIMITER ;

-- UnitRegistryRepository.CredentialAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_credential;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_credential(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT credential_id AS Id,computer_unit_id AS ComputerUnitId,credential_generation AS Generation,public_key AS PublicKey,key_fingerprint AS Fingerprint,algorithm AS Algorithm,created_at AS CreatedAtUtc,activated_at AS ActivatedAtUtc,revoked_at AS RevokedAtUtc,replaced_by_id AS ReplacedById,replaces_id AS ReplacesId FROM device_credentials WHERE credential_id=p_id FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.CredentialByFingerprintAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_credential_by_fingerprint;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_credential_by_fingerprint(
    IN p_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin
)
proc: BEGIN
    SELECT credential_id AS Id,computer_unit_id AS ComputerUnitId,credential_generation AS Generation,public_key AS PublicKey,key_fingerprint AS Fingerprint,algorithm AS Algorithm,created_at AS CreatedAtUtc,activated_at AS ActivatedAtUtc,revoked_at AS RevokedAtUtc,replaced_by_id AS ReplacedById,replaces_id AS ReplacesId FROM device_credentials WHERE key_fingerprint=p_fingerprint FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.InsertCredentialAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_insert_credential;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_insert_credential(
    IN p_id CHAR(36),
    IN p_unit CHAR(36),
    IN p_generation INT,
    IN p_public_key VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_algorithm VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_created_at_utc DATETIME(6),
    IN p_activated_at_utc DATETIME(6),
    IN p_replaces CHAR(36)
)
proc: BEGIN
    INSERT INTO device_credentials(credential_id,computer_unit_id,credential_generation,public_key,key_fingerprint,algorithm,created_at,activated_at,replaces_id)
    VALUES(p_id,p_unit,p_generation,p_public_key,p_fingerprint,p_algorithm,p_created_at_utc,p_activated_at_utc,p_replaces);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.SaveCredentialAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_save_credential;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_save_credential(
    IN p_id CHAR(36),
    IN p_activated_at_utc DATETIME(6),
    IN p_revoked_at_utc DATETIME(6),
    IN p_replaced CHAR(36)
)
proc: BEGIN
    UPDATE device_credentials SET activated_at=p_activated_at_utc,revoked_at=p_revoked_at_utc,replaced_by_id=p_replaced WHERE credential_id=p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.RevokeCredentialsAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_revoke_credentials;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_revoke_credentials(
    IN p_id CHAR(36),
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE device_credentials SET revoked_at=COALESCE(revoked_at,p_now) WHERE computer_unit_id=p_id;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.ChallengeAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_challenge;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_challenge(
    IN p_id CHAR(36)
)
proc: BEGIN
    SELECT challenge_id AS Id,computer_unit_id AS ComputerUnitId,credential_id AS CredentialId,context_id AS ContextId,
    replacement_credential_id AS ReplacementCredentialId,purpose AS Purpose,audience AS Audience,nonce AS Nonce,
    created_at AS CreatedAtUtc,expires_at AS ExpiresAtUtc,consumed_at AS ConsumedAtUtc,verified_at AS VerifiedAtUtc
    FROM device_auth_challenges WHERE challenge_id=p_id FOR UPDATE;
END$$
DELIMITER ;

-- UnitRegistryRepository.InsertChallengeAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_insert_challenge;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_insert_challenge(
    IN p_id CHAR(36),
    IN p_unit CHAR(36),
    IN p_credential CHAR(36),
    IN p_context CHAR(36),
    IN p_replacement CHAR(36),
    IN p_purpose VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_audience VARCHAR(200),
    IN p_nonce CHAR(43) CHARACTER SET ascii COLLATE ascii_bin,
    IN p_created_at_utc DATETIME(6),
    IN p_expires_at_utc DATETIME(6)
)
proc: BEGIN
    INSERT INTO device_auth_challenges(challenge_id,computer_unit_id,credential_id,context_id,replacement_credential_id,purpose,audience,nonce,created_at,expires_at)
    VALUES(p_id,p_unit,p_credential,p_context,p_replacement,p_purpose,p_audience,p_nonce,p_created_at_utc,p_expires_at_utc);
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.ConsumeChallengeAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_consume_challenge;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_consume_challenge(
    IN p_id CHAR(36),
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE device_auth_challenges SET consumed_at=p_now WHERE challenge_id=p_id AND consumed_at IS NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.VerifyChallengeAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_verify_challenge;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_verify_challenge(
    IN p_id CHAR(36),
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE device_auth_challenges SET verified_at=p_now WHERE challenge_id=p_id AND consumed_at IS NOT NULL;
    SELECT ROW_COUNT() AS affected_rows;
END$$
DELIMITER ;

-- UnitRegistryRepository.OutstandingChallengesAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_outstanding_challenges;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_outstanding_challenges(
    IN p_id CHAR(36),
    IN p_now DATETIME(6)
)
proc: BEGIN
    SELECT COUNT(*) AS outstanding_challenges FROM device_auth_challenges WHERE computer_unit_id=p_id AND consumed_at IS NULL AND expires_at>p_now;
END$$
DELIMITER ;

-- UnitRegistryRepository.SetOfflineAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_set_offline;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_set_offline(
    IN p_id CHAR(36)
)
proc: BEGIN
    UPDATE computer_units SET last_seen_at=NULL WHERE computer_unit_id=p_id;
    UPDATE unit_availability SET is_online=0 WHERE computer_unit_id=p_id;
END$$
DELIMITER ;

-- UnitRegistryRepository.HeartbeatAsync
-- Registry primitive: call in the same caller transaction after sp_unit_registry_transaction; pass its now_utc as Now.
DROP PROCEDURE IF EXISTS sp_unit_registry_heartbeat;
DELIMITER $$
CREATE PROCEDURE sp_unit_registry_heartbeat(
    IN p_id CHAR(36),
    IN p_ip VARCHAR(45),
    IN p_now DATETIME(6)
)
proc: BEGIN
    UPDATE computer_units SET last_seen_at=p_now WHERE computer_unit_id=p_id;
    INSERT INTO unit_availability(computer_unit_id,is_online,last_heartbeat,ip_address) VALUES(p_id,1,p_now,p_ip)
    ON DUPLICATE KEY UPDATE is_online=1,last_heartbeat=p_now,ip_address=p_ip;
END$$
DELIMITER ;
