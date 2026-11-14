<?php
declare(strict_types=1);
ob_start();ini_set('display_errors','0');ini_set('log_errors','1');
header('Content-Type: application/json; charset=utf-8');header('Cache-Control: private, no-store');header('X-Content-Type-Options: nosniff');header('Referrer-Policy: no-referrer');
require_once dirname(__DIR__).'/private/runtime.php';$db=null;
try {
 if(!in_array($_SERVER['REQUEST_METHOD'],['GET','POST'],true))problem('Требуется GET или POST.',405);
 $path=getenv('INTERNET_CONFIG') ?: dirname(__DIR__).'/private/config.php';if(!is_file($path))problem('Сервис не настроен.',503);$config=require $path;
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);query($db,"SET time_zone='+00:00'");
 $token=$_SERVER['HTTP_X_MKR_ROUTER_TOKEN'] ?? '';if(!is_string($token)||strlen($token)>128)problem('Код устройства недействителен.',401);
 if($token==='')limitRate($db,'router-agent-anonymous',100);
 $device=authenticateRouterDevice($db,$token);
 if($_SERVER['REQUEST_METHOD']==='GET')$result=routerDeviceSnapshot($db,$device);
 else {
  if((int)($_SERVER['CONTENT_LENGTH'] ?? 0)>2048)problem('Слишком большой запрос.',413);
  $raw=file_get_contents('php://input',false,null,0,2049);if($raw===false||strlen($raw)>2048)problem('Слишком большой запрос.',413);
  $body=json_decode($raw,true,8);if(!is_array($body))problem('Некорректный JSON.');
  $result=reportRouterDevice($db,$device,$body);
 }
 $status=200;
}catch(AccountError $e){if($db?->inTransaction())$db->rollBack();$status=$e->httpStatus;$result=['error'=>$e->getMessage()];}
catch(Throwable $e){if($db?->inTransaction())$db->rollBack();error_log('Router agent request failed');$status=503;$result=['error'=>'Не удалось проверить ссылку. Повторите позже.'];}
while(ob_get_level()>0)ob_end_clean();http_response_code($status);echo json_encode($result,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE);
