CREATE TABLE refresh_tokens (
                                id CHAR(36) NOT NULL,
                                user_id CHAR(36) NOT NULL,
                                token_hash VARCHAR(255) NOT NULL,
                                created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                                expires_at DATETIME(6) NOT NULL,
                                revoked_at DATETIME(6) NULL,

                                PRIMARY KEY (id),

                                CONSTRAINT fk_refresh_tokens_user
                                    FOREIGN KEY (user_id)
                                        REFERENCES users(user_id)
                                        ON DELETE CASCADE,

                                UNIQUE KEY uq_refresh_tokens_token_hash (token_hash),
                                INDEX idx_refresh_tokens_user_id (user_id)
);