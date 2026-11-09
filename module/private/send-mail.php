<?php
// Запускать только из cron/CLI. Никаких общедоступных URL и токенов.
if (PHP_SAPI!=='cli') {http_response_code(404);exit;}
require_once __DIR__.'/notifications.php';
$path=getenv('INTERNET_CONFIG') ?: __DIR__.'/config.php';
if (!is_file($path)){fwrite(STDERR,"Не найден config.php\n");exit(1);}
$config=require $path;
try {
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);$db->exec("SET time_zone='+00:00'");
 $result=sendPendingMail($db,$config,10);echo json_encode($result,JSON_UNESCAPED_UNICODE).PHP_EOL;exit($result['ready']?0:2);
} catch(Throwable $e){fwrite(STDERR,"Не удалось обработать очередь. Проверьте настройки базы и почты.\n");exit(1);}
