<?php
declare(strict_types=1);
// Public status and authenticated agent transport share no account session.
$GLOBALS['monitor_buffer']=ob_get_level();ob_start();ini_set('display_errors','0');ini_set('log_errors','1');
function monitorAnswer(array $body,int $status=200): never {
 while(ob_get_level()>$GLOBALS['monitor_buffer']){if(!@ob_end_clean())break;}
 header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');header('X-Content-Type-Options: nosniff');http_response_code($status);echo json_encode($body,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE);exit;
}
register_shutdown_function(static function(){if(error_get_last() && in_array(error_get_last()['type'],[E_ERROR,E_PARSE,E_CORE_ERROR,E_COMPILE_ERROR],true))monitorAnswer(['error'=>'Мониторинг временно недоступен.'],500);});
require_once __DIR__.'/runtime.php';require_once __DIR__.'/monitoring.php';
function monitorDb(): PDO {
 $path=getenv('INTERNET_CONFIG') ?: __DIR__.'/config.php';if(!is_file($path))problem('Мониторинг ещё не настроен.',503);$config=require $path;$GLOBALS['monitor_app_config']=$config;
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);query($db,"SET time_zone='+00:00'");return $db;
}
