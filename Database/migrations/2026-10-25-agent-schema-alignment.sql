-- JEP: align the existing CLMS database with the current agent repositories.
-- Applied to local clms on MariaDB 10.4.32, 2026-10-25.
-- Run once after backing up and checking SHOW CREATE TABLE for these tables.
-- Precondition: at most one unit_availability row per computer_unit_id.
-- Attendance course/laboratory_id columns were already present in this database.
-- Existing unit, equipment, attendance and session records are preserved.
-- New serial/key fields remain NULL until real unit identities are provisioned.
-- This is a targeted alternative to the agent-related DDL in
-- 2026-10-11-agent-unit-usage.sql for the existing schema, not a follow-on
-- script to apply blindly after that migration.

USE clms;

ALTER TABLE computer_units
    ADD COLUMN serial_number VARCHAR(100) NULL,
    ADD COLUMN agent_api_key_hash CHAR(64) NULL,
    ADD UNIQUE KEY uq_computer_units_serial_number (serial_number),
    ADD UNIQUE KEY uq_computer_units_agent_key_hash (agent_api_key_hash);

-- Required for UnitAvailabilityRepository's ON DUPLICATE KEY UPDATE.
-- Keep the existing auto-increment primary key and foreign key intact.
ALTER TABLE unit_availability
    ADD UNIQUE KEY uq_unit_availability_unit (computer_unit_id);

CREATE INDEX idx_open_logs
    ON unit_utilization_logs (computer_unit_id, time_out);
