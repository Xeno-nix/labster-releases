-- Apply once before deploying. Existing users are not forced through first-login password change.
ALTER TABLE users ADD COLUMN password_changed TINYINT(1) NOT NULL DEFAULT 1;
ALTER TABLE users ALTER COLUMN password_changed SET DEFAULT 0;

-- Prevent concurrent registrations from creating two accounts for the same email.
SET @email_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics
    WHERE table_schema=DATABASE() AND table_name='users' AND column_name='email' AND non_unique=0
    AND index_name IN (SELECT index_name FROM information_schema.statistics WHERE table_schema=DATABASE()
        AND table_name='users' GROUP BY index_name HAVING COUNT(*)=1)),
    'SELECT 1', 'CREATE UNIQUE INDEX uq_users_email ON users(email)');
PREPARE account_migration FROM @email_sql;
EXECUTE account_migration;
DEALLOCATE PREPARE account_migration;

-- Add Google uniqueness only if it was not installed by the earlier Google migration.
-- These statements deliberately fail on duplicate identities: resolve duplicates manually; do not delete links.
SET @link_subject_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics
    WHERE table_schema=DATABASE() AND table_name='external_identities' AND index_name='uq_external_provider_subject'),
    'SELECT 1', 'CREATE UNIQUE INDEX uq_external_provider_subject ON external_identities(provider, provider_sub)');
PREPARE account_migration FROM @link_subject_sql;
EXECUTE account_migration;
DEALLOCATE PREPARE account_migration;
SET @link_user_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics
    WHERE table_schema=DATABASE() AND table_name='external_identities' AND index_name='uq_external_provider_user'),
    'SELECT 1', 'CREATE UNIQUE INDEX uq_external_provider_user ON external_identities(provider, user_id)');
PREPARE account_migration FROM @link_user_sql;
EXECUTE account_migration;
DEALLOCATE PREPARE account_migration;
