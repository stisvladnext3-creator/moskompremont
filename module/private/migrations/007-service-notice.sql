-- Общее объявление сайта. Повторный импорт сохраняет изменённый текст и видимость.
CREATE TABLE IF NOT EXISTS internet_service_notice (
 id TINYINT UNSIGNED PRIMARY KEY,
 enabled TINYINT UNSIGNED NOT NULL DEFAULT 1,
 message TEXT NOT NULL,
 updated_by BIGINT UNSIGNED NULL,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
INSERT IGNORE INTO internet_service_notice(id,enabled,message) VALUES
 (1,1,'Сервис работает исправно только через Wi-Fi. На мобильной связи наблюдается сбой или ограничение доступа.');
