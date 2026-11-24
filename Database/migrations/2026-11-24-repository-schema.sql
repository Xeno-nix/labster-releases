-- Additive, rerunnable prerequisites for the repository objects.
-- Select the intended database. Existing historical rows remain nullable.
SET @clms_object_ddl = (SELECT IF(COUNT(*)=0,
    'ALTER TABLE class_schedule ADD COLUMN year_level TINYINT UNSIGNED NULL', 'DO 0')
    FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='class_schedule' AND column_name='year_level');
PREPARE clms_object_schema FROM @clms_object_ddl;
EXECUTE clms_object_schema;
DEALLOCATE PREPARE clms_object_schema;

SET @clms_object_ddl = (SELECT IF(COUNT(*)=0,
    'ALTER TABLE class_schedule ADD COLUMN section VARCHAR(10) NULL', 'DO 0')
    FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='class_schedule' AND column_name='section');
PREPARE clms_object_schema FROM @clms_object_ddl;
EXECUTE clms_object_schema;
DEALLOCATE PREPARE clms_object_schema;

SET @clms_object_ddl = (SELECT IF(COUNT(*)=0,
    'ALTER TABLE attendance_logs ADD COLUMN laboratory_id INT NULL, ADD CONSTRAINT attendance_logs_laboratory_fk FOREIGN KEY (laboratory_id) REFERENCES laboratories(lab_id)', 'DO 0')
    FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='attendance_logs' AND column_name='laboratory_id');
PREPARE clms_object_schema FROM @clms_object_ddl;
EXECUTE clms_object_schema;
DEALLOCATE PREPARE clms_object_schema;

-- Preserve the course value used by main's logbook export.
SET @clms_object_ddl = (SELECT IF(COUNT(*)=0,
    'ALTER TABLE attendance_logs ADD COLUMN course VARCHAR(255) NULL', 'DO 0')
    FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='attendance_logs' AND column_name='course');
PREPARE clms_object_schema FROM @clms_object_ddl;
EXECUTE clms_object_schema;
DEALLOCATE PREPARE clms_object_schema;
