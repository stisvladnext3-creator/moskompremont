-- Только новые таблицы. Имеющиеся аккаунты, заявки и подписки сохраняются.
CREATE TABLE IF NOT EXISTS internet_subscription_revocations (
 subscription_id BIGINT UNSIGNED NOT NULL,
 user_id BIGINT UNSIGNED NOT NULL,
 revoked_by BIGINT UNSIGNED NOT NULL,
 revoked_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY (subscription_id,user_id),
 FOREIGN KEY (subscription_id) REFERENCES internet_subscriptions(id),
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 FOREIGN KEY (revoked_by) REFERENCES internet_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_message_photos (
 id CHAR(32) PRIMARY KEY,
 message_id BIGINT UNSIGNED NOT NULL,
 uploader_id BIGINT UNSIGNED NOT NULL,
 mime VARCHAR(32) NOT NULL,
 file_name VARCHAR(180) NOT NULL,
 byte_size INT UNSIGNED NOT NULL,
 width INT UNSIGNED NOT NULL,
 height INT UNSIGNED NOT NULL,
 content MEDIUMBLOB NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 FOREIGN KEY (message_id) REFERENCES internet_messages(id),
 FOREIGN KEY (uploader_id) REFERENCES internet_users(id),
 INDEX (message_id), INDEX (uploader_id,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
