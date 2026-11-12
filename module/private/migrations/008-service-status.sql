-- Цвет общего статуса. Текст и видимость объявления из v14 сохраняются.
CREATE TABLE IF NOT EXISTS internet_service_state (
 id TINYINT UNSIGNED PRIMARY KEY,
 level VARCHAR(12) NOT NULL DEFAULT 'warning',
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT IGNORE INTO internet_service_state(id,level) VALUES (1,'warning');
-- В истории оплаты сохраняется только название удалённой подписки, без ссылки.
CREATE TABLE IF NOT EXISTS internet_order_deleted_subscriptions (
 order_id CHAR(32) NOT NULL,
 name VARCHAR(100) NOT NULL,
 deleted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 deleted_by BIGINT UNSIGNED NOT NULL,
 PRIMARY KEY (order_id,name),
 FOREIGN KEY (order_id) REFERENCES internet_orders(id),
 FOREIGN KEY (deleted_by) REFERENCES internet_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
-- Сроки ссылок и количество оплаченных месяцев. Старые ссылки остаются без срока до настройки администратором.
CREATE TABLE IF NOT EXISTS internet_subscription_terms (
 subscription_id BIGINT UNSIGNED PRIMARY KEY,
 kind VARCHAR(16) NOT NULL,
 expires_at DATETIME NULL,
 anchor_day TINYINT UNSIGNED NULL,
 updated_by BIGINT UNSIGNED NOT NULL,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 FOREIGN KEY (subscription_id) REFERENCES internet_subscriptions(id) ON DELETE CASCADE,
 FOREIGN KEY (updated_by) REFERENCES internet_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_order_terms (
 order_id CHAR(32) PRIMARY KEY,
 months TINYINT UNSIGNED NOT NULL DEFAULT 1,
 FOREIGN KEY (order_id) REFERENCES internet_orders(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
