<?php
declare(strict_types=1);
require_once dirname(__DIR__).'/private/monitor-http.php';
try {
 $db=monitorDb();$s=monitorSettings($db);$token=$_SERVER['HTTP_X_MONITOR_TOKEN'] ?? '';
 if(!is_string($token)||!preg_match('/\A[0-9a-f]{64}\z/D',$token)||empty($s['token_hash'])||!hash_equals($s['token_hash'],hash('sha256',$token)))problem('Нет доступа.',401);
 if($_SERVER['REQUEST_METHOD']==='GET')monitorAnswer(['enabled'=>(bool)$s['enabled'],'revision'=>(int)$s['revision'],'subscription_url'=>$s['enabled']?$s['subscription_url']:null,'interval'=>60,'refresh_interval'=>300,'max_nodes'=>100]);
 if($_SERVER['REQUEST_METHOD']!=='POST')problem('Метод не поддерживается.',405);
 if(stripos($_SERVER['CONTENT_TYPE'] ?? '','application/json')!==0)problem('Требуется JSON.',415);
 $raw=file_get_contents('php://input',false,null,0,200001);if(strlen($raw)>200000)problem('Слишком большой отчёт.',413);
 try{$body=json_decode($raw,true,12,JSON_THROW_ON_ERROR);}catch(Throwable $e){problem('Некорректный JSON.');}if(!is_array($body))problem('Некорректный отчёт.');
 if(($body['task'] ?? '')==='mail'){$mail=sendPendingMail($db,$GLOBALS['monitor_app_config'],1);$mail['push']=sendClientPush($db,$GLOBALS['monitor_app_config'],20);monitorAnswer($mail);}
 if(!$s['enabled'])problem('Мониторинг отключён.',409);
 monitorReport($db,$body,(int)$s['revision']);monitorAnswer(['ok'=>true]);
}catch(AccountError $e){monitorAnswer(['error'=>$e->getMessage()],$e->httpStatus);}
catch(Throwable $e){error_log('Monitor agent: '.$e->getMessage());monitorAnswer(['error'=>'Мониторинг временно недоступен.'],503);}
