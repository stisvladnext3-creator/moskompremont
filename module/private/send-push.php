<?php
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
require_once __DIR__.'/runtime.php';
try{$path=getenv('INTERNET_CONFIG') ?: __DIR__.'/config.php';$config=require $path;$db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);query($db,"SET time_zone='+00:00'");$result=sendClientPush($db,$config,50);echo json_encode($result,JSON_UNESCAPED_UNICODE).PHP_EOL;exit($result['ready']?0:2);}catch(Throwable $e){fwrite(STDERR,"Не удалось обработать push-очередь. Проверьте настройки базы и PHP.\n");exit(1);}
