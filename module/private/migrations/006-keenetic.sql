-- Дополнение к установленному кабинету: существующие данные сохраняются.
-- Импортировать в ту же базу, которая указана в private/config.php.
CREATE TABLE IF NOT EXISTS internet_keenetic_access (
 user_id BIGINT UNSIGNED PRIMARY KEY,
 enabled TINYINT UNSIGNED NOT NULL DEFAULT 0,
 updated_by BIGINT UNSIGNED NOT NULL,
 updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 FOREIGN KEY (updated_by) REFERENCES internet_users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS internet_keenetic_files (
 id CHAR(32) PRIMARY KEY,
 user_id BIGINT UNSIGNED NOT NULL,
 file_name VARCHAR(180) NOT NULL,
 byte_size INT UNSIGNED NOT NULL,
 content MEDIUMBLOB NOT NULL,
 uploaded_by BIGINT UNSIGNED NOT NULL,
 created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
 FOREIGN KEY (user_id) REFERENCES internet_users(id),
 FOREIGN KEY (uploaded_by) REFERENCES internet_users(id),
 INDEX (user_id,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
