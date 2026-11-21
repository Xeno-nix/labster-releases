-- Test database only. Apply the student-kiosk migration first.
-- Replace the email below with a real test inbox you control.
-- Requires an existing, non-deleted class in a term covering today (Philippines).
USE `clms`;

SET @test_student_id = 'TEST-KIOSK01';
SET @test_email = LOWER(TRIM('replace-me@your-school.example'));
SET @test_class_id = (
    SELECT c.class_schedule_id
    FROM class_schedule c
    JOIN academic_terms t ON t.academic_term_id = c.term_id
    WHERE c.deleted_at IS NULL
      AND DATE(UTC_TIMESTAMP() + INTERVAL 8 HOUR) BETWEEN t.start_date AND t.end_date
    ORDER BY c.class_schedule_id
    LIMIT 1
);
-- Alternatively set @test_class_id to a specific eligible class ID here.
SET @ready = @test_class_id IS NOT NULL
    AND @test_email NOT LIKE '%.example' AND @test_email <> '';

START TRANSACTION;

INSERT INTO students (student_id, first_name, last_name)
SELECT @test_student_id, 'Kiosk', 'Test Student'
WHERE @ready AND NOT EXISTS (
    SELECT 1 FROM students WHERE student_id = @test_student_id
);

INSERT INTO student_enrollment (student_id, class_schedule_id)
SELECT @test_student_id, @test_class_id
WHERE @ready AND NOT EXISTS (
    SELECT 1 FROM student_enrollment
    WHERE student_id = @test_student_id AND class_schedule_id = @test_class_id
);

-- Leave password/code fields NULL: the app sends a code on first sign-in.
-- Rerunning this script preserves an existing account, email, and password.
INSERT INTO student_kiosk_accounts (student_id, email)
SELECT @test_student_id, @test_email
WHERE @ready AND NOT EXISTS (
    SELECT 1 FROM student_kiosk_accounts WHERE student_id = @test_student_id
);

COMMIT;

SELECT CASE
    WHEN @test_class_id IS NULL THEN 'No data inserted: create a current-term class first.'
    WHEN NOT @ready THEN 'No data inserted: replace the example email with your test inbox.'
    ELSE 'Ready: use the stored email below; initial password is TEST-KIOSK01 unless already changed.'
END AS seed_status, @test_class_id AS class_schedule_id;

SELECT student_id, email,
    CASE WHEN password_hash IS NULL THEN 'Student ID (first sign-in)'
         ELSE 'Existing password (preserved)' END AS login_password
FROM student_kiosk_accounts WHERE student_id = @test_student_id;

-- Do not insert fake sessions. Successful password sign-in creates both the
-- session and its linked unit_utilization_logs row for the registered unit.
-- Run these queries again after signing in / ending a session:
SELECT * FROM student_kiosk_sessions WHERE student_id = @test_student_id;
SELECT * FROM unit_utilization_logs WHERE student_id = @test_student_id
ORDER BY unit_utilization_log_id DESC LIMIT 10;
