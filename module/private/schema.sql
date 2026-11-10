CREATE TABLE IF NOT EXISTS internet_users (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 first_name VARCHAR(80) NOT NULL,
 last_name VARCHAR(80) NOT NULL,
 telegram VARCHAR(100) NOT NULL,
 email VARCHAR(254) NOT NULL UNIQUE,
 password_hash VARCHAR(255) NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_orders (
 id CHAR(32) PRIMARY KEY,
 user_id BIGINT UNSIGNED NOT NULL,
 plan VARCHAR(20) NOT NULL,
 devices INT NOT NULL,
 amount INT NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'created',
 payment_reference VARCHAR(500) NOT NULL DEFAULT '',
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 submitted_at TIMESTAMP NULL,
 reviewed_at TIMESTAMP NULL,
 reviewed_by BIGINT UNSIGNED NULL,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 INDEX (user_id,created_at), INDEX (status,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_rate_limits (
 bucket CHAR(64) PRIMARY KEY,
 attempts INT NOT NULL,
 expires_at BIGINT NOT NULL,
 INDEX (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- Дополнение к установленной версии. Пользователи, оплаты и настройки сохраняются.
CREATE TABLE IF NOT EXISTS internet_subscriptions (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 name VARCHAR(100) NOT NULL UNIQUE,
 url TEXT NOT NULL,
 user_id BIGINT UNSIGNED NULL,
 assigned_at TIMESTAMP NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 INDEX (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_order_subscriptions (
 order_id CHAR(32) NOT NULL,
 subscription_id BIGINT UNSIGNED NOT NULL,
 issued_by BIGINT UNSIGNED NOT NULL,
 issued_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY(order_id,subscription_id),
 FOREIGN KEY (order_id) REFERENCES internet_orders(id),
 FOREIGN KEY (subscription_id) REFERENCES internet_subscriptions(id),
 FOREIGN KEY (issued_by) REFERENCES internet_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_subscription_claims (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 user_id BIGINT UNSIGNED NOT NULL,
 name VARCHAR(100) NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'pending',
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 reviewed_at TIMESTAMP NULL,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 UNIQUE (user_id,name), INDEX(name,status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_tickets (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 user_id BIGINT UNSIGNED NOT NULL,
 subject VARCHAR(160) NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'open',
 user_seen_message_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
 admin_seen_message_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 INDEX (user_id,updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_messages (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 ticket_id BIGINT UNSIGNED NOT NULL,
 sender_id BIGINT UNSIGNED NOT NULL,
 is_admin BOOLEAN NOT NULL DEFAULT FALSE,
 body TEXT NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 FOREIGN KEY (ticket_id) REFERENCES internet_tickets(id),
 FOREIGN KEY (sender_id) REFERENCES internet_users(id),
 INDEX (ticket_id,id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_admin_events (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 event_key VARCHAR(100) NOT NULL UNIQUE,
 type VARCHAR(20) NOT NULL,
 user_id BIGINT UNSIGNED NOT NULL,
 order_id CHAR(32) NULL,
 title VARCHAR(200) NOT NULL,
 body TEXT NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 read_at TIMESTAMP NULL,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 INDEX (type,read_at,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_mail_outbox (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 event_id BIGINT UNSIGNED NOT NULL UNIQUE,
 recipient VARCHAR(254) NOT NULL,
 subject VARCHAR(200) NOT NULL,
 body TEXT NOT NULL,
 status VARCHAR(20) NOT NULL DEFAULT 'pending',
 attempts INT NOT NULL DEFAULT 0,
 next_attempt_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 lease_until TIMESTAMP NULL,
 last_error VARCHAR(200) NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 sent_at TIMESTAMP NULL,
 FOREIGN KEY (event_id) REFERENCES internet_admin_events(id),
 INDEX (status,next_attempt_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
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
-- Отдельные ключи для роутеров, не подписочные ссылки.
CREATE TABLE IF NOT EXISTS internet_router_keys (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 label VARCHAR(100) NOT NULL,
 key_format VARCHAR(20) NOT NULL,
 key_value MEDIUMTEXT NOT NULL,
 key_hash CHAR(64) NOT NULL UNIQUE,
 user_id BIGINT UNSIGNED NULL,
 issued_by BIGINT UNSIGNED NOT NULL,
 assigned_at TIMESTAMP NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 FOREIGN KEY (issued_by) REFERENCES internet_users(id),
 INDEX (user_id), INDEX (label)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
