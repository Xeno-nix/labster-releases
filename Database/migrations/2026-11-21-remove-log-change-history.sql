-- Run manually only if the former history feature was already installed.
-- Removes recorded history permanently; existing CLMS usage/activity logs remain.
USE clms;

DROP TRIGGER IF EXISTS trg_attendance_logs_history_ai;
DROP TRIGGER IF EXISTS trg_attendance_logs_history_au;
DROP TRIGGER IF EXISTS trg_attendance_logs_history_ad;
DROP TRIGGER IF EXISTS trg_unit_utilization_logs_history_ai;
DROP TRIGGER IF EXISTS trg_unit_utilization_logs_history_au;
DROP TRIGGER IF EXISTS trg_unit_utilization_logs_history_ad;
DROP TRIGGER IF EXISTS trg_screen_cast_sessions_history_ai;
DROP TRIGGER IF EXISTS trg_screen_cast_sessions_history_au;
DROP TRIGGER IF EXISTS trg_screen_cast_sessions_history_ad;
DROP TRIGGER IF EXISTS trg_screen_cast_connections_history_ai;
DROP TRIGGER IF EXISTS trg_screen_cast_connections_history_au;
DROP TRIGGER IF EXISTS trg_screen_cast_connections_history_ad;
DROP TRIGGER IF EXISTS trg_unit_activity_history_ai;
DROP TRIGGER IF EXISTS trg_unit_activity_history_au;
DROP TRIGGER IF EXISTS trg_unit_activity_history_ad;

DROP TABLE IF EXISTS log_change_history;
