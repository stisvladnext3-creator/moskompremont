-- Продление существующей подписки после ручного подтверждения оплаты.
-- Импортировать в действующую базу кабинета после предыдущих миграций.
CREATE TABLE IF NOT EXISTS internet_order_renewals (
 order_id CHAR(32) PRIMARY KEY,
 subscription_id BIGINT UNSIGNED NULL,
 subscription_name VARCHAR(100) NOT NULL,
 applied_at DATETIME NULL,
 previous_expires_at DATETIME NULL,
 new_expires_at DATETIME NULL,
 renewed_by BIGINT UNSIGNED NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 FOREIGN KEY (order_id) REFERENCES internet_orders(id) ON DELETE CASCADE,
 FOREIGN KEY (subscription_id) REFERENCES internet_subscriptions(id) ON DELETE SET NULL,
 FOREIGN KEY (renewed_by) REFERENCES internet_users(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
