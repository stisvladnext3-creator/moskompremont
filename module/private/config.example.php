<?php
// Существующий config.php НЕ заменять: добавьте в него новые настройки ниже.
// Конфиг с паролями храните на сервере; в GitHub его загружать не нужно.
return [
 'dsn' => 'mysql:host=localhost;dbname=YOUR_DATABASE;charset=utf8mb4',
 'db_user' => 'YOUR_DATABASE_USER',
 'db_password' => 'YOUR_DATABASE_PASSWORD',
 'origin' => 'https://YOUR-DOMAIN.ru',
 'secure_cookies' => true,
 // ID зарегистрированного аккаунта sania.iwannikow@yandex.ru из internet_users.
 // Существующие назначенные ID сохраняются. Почта сама по себе не даёт прав.
 'admin_user_ids' => [],
 'admin_email' => 'sania.iwannikow@yandex.ru',
 'app_path' => '', // если кабинет в подпапке, например internet; иначе оставить пустым
 'qr_image' => '', // сохранить ваш существующий путь, например images/payment/ozon-qr.png
 'recipient' => '', // сохранить имя получателя
 'payment_note' => 'После оплаты укажите время перевода или последние 4 цифры телефона.',
 'mail' => [
  'enabled' => false, // включите после заполнения данных отправки
  'transport' => 'smtp', // smtp или mail (если PHP mail настроена хостингом)
  'from' => 'sania.iwannikow@yandex.ru',
  'from_name' => 'МосКомпРемонт',
  'smtp_host' => 'smtp.yandex.ru',
  'smtp_port' => 465,
  'smtp_encryption' => 'ssl',
  'smtp_auth' => true,
  'smtp_user' => 'sania.iwannikow@yandex.ru',
  // Пароль приложения Яндекс.Почты, не пароль входа в кабинет сайта.
  'smtp_password' => getenv('INTERNET_SMTP_PASSWORD') ?: '',
 ],
];
