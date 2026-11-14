-- Коды устройств OpenWrt: доступ только к одной ссылке/ключу конкретного клиента.
CREATE TABLE IF NOT EXISTS internet_router_devices (
 id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
 selector CHAR(24) NOT NULL UNIQUE,
 token_hash CHAR(64) NOT NULL,
 user_id BIGINT UNSIGNED NOT NULL,
 password_fingerprint CHAR(64) NOT NULL,
 resource_type VARCHAR(20) NOT NULL,
 resource_id BIGINT UNSIGNED NOT NULL,
 label VARCHAR(100) NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 expires_at DATETIME NOT NULL,
 revoked_at DATETIME NULL,
 last_seen_at DATETIME NULL,
 last_state VARCHAR(30) NULL,
 last_revision CHAR(64) NULL,
 client_version VARCHAR(30) NULL,
 FOREIGN KEY(user_id) REFERENCES internet_users(id) ON DELETE CASCADE,
 INDEX(user_id), INDEX(resource_type,resource_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
