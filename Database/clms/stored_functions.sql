-- CLMS stored functions (MySQL 8.0.21+).
-- Select the intended database, then install before stored_procedures.sql.
-- Shared scalar business rules only; no CRUD, table scans,
-- authorization, token generation or ordinary name formatting is wrapped here.
-- DROP/CREATE allows reinstallation but implicitly commits. Quiesce callers
-- during replacement and reapply routine-specific grants if necessary.
-- DEFINER and SQL SECURITY are omitted, using the requested MySQL defaults.

-- Used by sp_unit_registry_registrations for count/filter/display consistency.
-- A Pending request is effectively Expired at its expiry timestamp even if the
-- cleanup job has not persisted that transition yet. Terminal states stay fixed.
-- An explicit UTC reference time makes the function deterministic and allows
-- the procedure to use one clock snapshot for both of its result sets.
-- NULL expiry/reference time leaves Pending unchanged, matching the former CASE.
-- This is a display/filter rule, not a substitute for locked expiry checks in
-- the registration approval/authentication workflow. It never updates a row.
DROP FUNCTION IF EXISTS sf_registration_effective_status;
DELIMITER $$
CREATE FUNCTION sf_registration_effective_status(
    p_status VARCHAR(20),
    p_expires_at DATETIME(6),
    p_as_of_utc DATETIME(6)
)
RETURNS VARCHAR(20)
DETERMINISTIC
NO SQL
BEGIN
    RETURN CASE
        WHEN p_status = 'Pending' AND p_expires_at <= p_as_of_utc THEN 'Expired'
        ELSE p_status
    END;
END$$
DELIMITER ;

-- Compute a boundary once per request, keeping functions off indexed log_date.
-- MySQL mode-1 weeks start on Monday, including weeks crossing New Year.
DROP FUNCTION IF EXISTS sf_logbook_period_start;
DELIMITER $$
CREATE FUNCTION sf_logbook_period_start(p_filter VARCHAR(20), p_as_of DATE)
RETURNS DATE
DETERMINISTIC
NO SQL
BEGIN
    RETURN CASE p_filter
        WHEN 'day' THEN p_as_of
        WHEN 'week' THEN DATE_SUB(p_as_of, INTERVAL WEEKDAY(p_as_of) DAY)
        WHEN 'month' THEN DATE_SUB(p_as_of, INTERVAL (DAYOFMONTH(p_as_of)-1) DAY)
        ELSE NULL
    END;
END$$
DELIMITER ;
