-- JEP monitoring sample data. Run against a local development database only.
-- Adds an isolated DEMO laboratory; does not modify existing workstation data.
-- Re-running skips existing demo records instead of resetting their state.
-- Times are UTC. Online heartbeats become stale without a running agent.
-- API keys and student identities are deliberately not provisioned.
USE clms;
START TRANSACTION;

INSERT INTO laboratories (lab_name)
SELECT 'DEMO - Unit Monitoring'
WHERE NOT EXISTS (SELECT 1 FROM laboratories WHERE lab_name = 'DEMO - Unit Monitoring');

SET @demo_lab = (SELECT MIN(lab_id) FROM laboratories WHERE lab_name = 'DEMO - Unit Monitoring');
SET @seed_now = UTC_TIMESTAMP();

INSERT INTO computer_units (computer_unit_id, unit_name, unit_number, laboratory_id, serial_number)
SELECT sample.id, sample.name, sample.num, @demo_lab, sample.name
FROM (
    SELECT 'de000000-0000-0000-0000-000000000001' AS id, 'DEMO-PC-01' AS name, 1 AS num
    UNION ALL SELECT 'de000000-0000-0000-0000-000000000002', 'DEMO-PC-02', 2
    UNION ALL SELECT 'de000000-0000-0000-0000-000000000003', 'DEMO-PC-03', 3
    UNION ALL SELECT 'de000000-0000-0000-0000-000000000004', 'DEMO-PC-04', 4
    UNION ALL SELECT 'de000000-0000-0000-0000-000000000005', 'DEMO-PC-05', 5
    UNION ALL SELECT 'de000000-0000-0000-0000-000000000006', 'DEMO-PC-06', 6
) AS sample
WHERE NOT EXISTS (SELECT 1 FROM computer_units u WHERE u.computer_unit_id = sample.id)
  AND NOT EXISTS (SELECT 1 FROM computer_units u WHERE u.serial_number = sample.name)
  AND NOT EXISTS (SELECT 1 FROM computer_units u WHERE u.laboratory_id = @demo_lab AND u.unit_number = sample.num);

INSERT INTO unit_availability (computer_unit_id, is_online, last_heartbeat, ip_address)
SELECT u.computer_unit_id,
       IF(u.unit_number <= 4, 1, 0),
       IF(u.unit_number <= 4, @seed_now, @seed_now - INTERVAL 2 HOUR),
       CONCAT('192.0.2.', u.unit_number)
FROM computer_units u
WHERE u.laboratory_id = @demo_lab
  AND u.computer_unit_id IN (
      'de000000-0000-0000-0000-000000000001', 'de000000-0000-0000-0000-000000000002',
      'de000000-0000-0000-0000-000000000003', 'de000000-0000-0000-0000-000000000004',
      'de000000-0000-0000-0000-000000000005', 'de000000-0000-0000-0000-000000000006')
  AND NOT EXISTS (SELECT 1 FROM unit_availability a WHERE a.computer_unit_id = u.computer_unit_id);

-- Two active PC sessions; no claim is made about which person is signed in.
INSERT INTO unit_utilization_logs (computer_unit_id, log_date, time_in, time_out)
SELECT u.computer_unit_id, DATE(@seed_now - INTERVAL 45 MINUTE), @seed_now - INTERVAL 45 MINUTE, NULL
FROM computer_units u
WHERE u.laboratory_id = @demo_lab
  AND u.computer_unit_id IN ('de000000-0000-0000-0000-000000000001', 'de000000-0000-0000-0000-000000000002')
  AND NOT EXISTS (SELECT 1 FROM unit_utilization_logs l WHERE l.computer_unit_id = u.computer_unit_id);

-- Two completed sessions from yesterday, one for an idle PC and one offline PC.
INSERT INTO unit_utilization_logs (computer_unit_id, log_date, time_in, time_out)
SELECT u.computer_unit_id, UTC_DATE() - INTERVAL 1 DAY,
       TIMESTAMP(UTC_DATE() - INTERVAL 1 DAY, '08:00:00'),
       TIMESTAMP(UTC_DATE() - INTERVAL 1 DAY, '09:30:00')
FROM computer_units u
WHERE u.laboratory_id = @demo_lab
  AND u.computer_unit_id IN ('de000000-0000-0000-0000-000000000003', 'de000000-0000-0000-0000-000000000005')
  AND NOT EXISTS (SELECT 1 FROM unit_utilization_logs l WHERE l.computer_unit_id = u.computer_unit_id);

COMMIT;
