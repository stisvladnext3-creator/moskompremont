-- Независимая таблица: существующие пользователи и подписки не изменяются.
CREATE TABLE IF NOT EXISTS internet_monitor (
 id TINYINT UNSIGNED PRIMARY KEY,
 enabled TINYINT UNSIGNED NOT NULL DEFAULT 0,
 subscription_url TEXT NULL,
 token_hash CHAR(64) NULL,
 revision INT UNSIGNED NOT NULL DEFAULT 1,
 results MEDIUMTEXT NULL,
 reported_at DATETIME NULL,
 source_status VARCHAR(24) NULL,
 mail_checked_at DATETIME NULL,
 mail_last_sent INT UNSIGNED NOT NULL DEFAULT 0,
 mail_last_failed INT UNSIGNED NOT NULL DEFAULT 0,
 main_host VARCHAR(253) NULL,
 main_port SMALLINT UNSIGNED NOT NULL DEFAULT 443,
 main_state VARCHAR(16) NULL,
 main_latency_ms INT UNSIGNED NULL,
 main_checked_at DATETIME NULL,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
