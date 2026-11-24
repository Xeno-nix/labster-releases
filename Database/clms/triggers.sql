-- CLMS PC usage and availability activity (MySQL 8.0.21+).
-- Run after all source tables exist in the selected database.
-- Existing application writers remain the source of usage and availability rows.
-- These triggers only add informational unit_activity entries (is_resolved=NULL).
-- Unchanged heartbeats produce no activity; generated rows share the source transaction.
-- DROP/CREATE supports reinstallation and implicitly commits.

DROP TRIGGER IF EXISTS trg_computer_units_registered_identity_bu;

DROP TRIGGER IF EXISTS trg_unit_utilization_activity_ai;
DELIMITER $$
CREATE TRIGGER trg_unit_utilization_activity_ai
AFTER INSERT ON unit_utilization_logs
FOR EACH ROW
BEGIN
    INSERT INTO unit_activity
        (computer_unit_id, student_id, guest_id, activity_type, description, is_resolved, detected_at)
    VALUES (NEW.computer_unit_id, NEW.student_id, NEW.guest_id, IF(NEW.time_in IS NOT NULL AND NEW.time_out IS NULL, 'usage_started', 'usage_recorded'), CONCAT('Usage record #', NEW.unit_utilization_log_id, IF(NEW.time_in IS NOT NULL AND NEW.time_out IS NULL, ' opened.', ' recorded.')), NULL, UTC_TIMESTAMP());
END$$
DELIMITER ;

DROP TRIGGER IF EXISTS trg_unit_utilization_activity_au;
DELIMITER $$
CREATE TRIGGER trg_unit_utilization_activity_au
AFTER UPDATE ON unit_utilization_logs
FOR EACH ROW
BEGIN
    IF OLD.time_out IS NULL AND NEW.time_out IS NOT NULL THEN
        INSERT INTO unit_activity
        (computer_unit_id, student_id, guest_id, activity_type, description, is_resolved, detected_at)
    VALUES (NEW.computer_unit_id, NEW.student_id, NEW.guest_id, 'usage_ended', CONCAT('Usage record #', NEW.unit_utilization_log_id, ' closed.'), NULL, UTC_TIMESTAMP());
    ELSEIF OLD.time_out IS NOT NULL AND NEW.time_out IS NULL THEN
        INSERT INTO unit_activity
        (computer_unit_id, student_id, guest_id, activity_type, description, is_resolved, detected_at)
    VALUES (NEW.computer_unit_id, NEW.student_id, NEW.guest_id, 'usage_reopened', CONCAT('Usage record #', NEW.unit_utilization_log_id, ' reopened.'), NULL, UTC_TIMESTAMP());
    END IF;
END$$
DELIMITER ;

DROP TRIGGER IF EXISTS trg_unit_availability_activity_ai;
DELIMITER $$
CREATE TRIGGER trg_unit_availability_activity_ai
AFTER INSERT ON unit_availability
FOR EACH ROW
BEGIN
    IF NEW.is_online IN (0, 1) THEN
        INSERT INTO unit_activity
        (computer_unit_id, activity_type, description, is_resolved, detected_at)
    VALUES (NEW.computer_unit_id, IF(NEW.is_online = 1, 'unit_online', 'unit_offline'),
        CONCAT('Availability recorded as ', IF(NEW.is_online = 1, 'online.', 'offline.')), NULL, UTC_TIMESTAMP());
    END IF;
END$$
DELIMITER ;

DROP TRIGGER IF EXISTS trg_unit_availability_activity_au;
DELIMITER $$
CREATE TRIGGER trg_unit_availability_activity_au
AFTER UPDATE ON unit_availability
FOR EACH ROW
BEGIN
    IF NOT (OLD.is_online <=> NEW.is_online) AND NEW.is_online IN (0, 1) THEN
        INSERT INTO unit_activity
        (computer_unit_id, activity_type, description, is_resolved, detected_at)
    VALUES (NEW.computer_unit_id, IF(NEW.is_online = 1, 'unit_online', 'unit_offline'),
        CONCAT('Availability recorded as ', IF(NEW.is_online = 1, 'online.', 'offline.')), NULL, UTC_TIMESTAMP());
    END IF;
END$$
DELIMITER ;
