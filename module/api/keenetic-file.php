<?php
declare(strict_types=1);
$GLOBALS['keenetic_buffer_base']=ob_get_level();ob_start();ini_set('display_errors','0');ini_set('log_errors','1');
function cleanKeeneticOutput(): void {while(ob_get_level()>($GLOBALS['keenetic_buffer_base'] ?? 0)){if(!@ob_end_clean())break;}}
header('Cache-Control: private, no-store, max-age=0');header('X-Content-Type-Options: nosniff');header('Cross-Origin-Resource-Policy: same-origin');header('Referrer-Policy: no-referrer');header("Content-Security-Policy: default-src 'none'; sandbox");
require_once dirname(__DIR__).'/private/runtime.php';require_once dirname(__DIR__).'/private/keenetic.php';
try {
 if(!in_array($_SERVER['REQUEST_METHOD'],['GET','HEAD'],true)){header('Allow: GET, HEAD');problem('Метод не поддерживается.',405);}
 $path=getenv('INTERNET_CONFIG') ?: dirname(__DIR__).'/private/config.php';if(!is_file($path))problem('Сервис не настроен.',503);$config=require $path;
 ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');session_name('internet_account');session_set_cookie_params(['lifetime'=>0,'path'=>'/','secure'=>$config['secure_cookies'] ?? true,'httponly'=>true,'samesite'=>'Lax']);session_start();
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);
 query($db,"SET time_zone='+00:00'");$user=accountSessionUser($db,$config);if(!$user)problem('Сначала войдите в кабинет.',401);$userId=(int)$user['id'];session_write_close();
 $id=keeneticFileId($_GET['id'] ?? null);$admin=publicUser($user,$config)['is_admin'];
 $columns=$_SERVER['REQUEST_METHOD']==='GET'?'f.file_name,f.byte_size,f.content':'f.file_name,f.byte_size';
 $access=$admin?'':' AND f.user_id=? AND EXISTS (SELECT 1 FROM internet_keenetic_access a WHERE a.user_id=f.user_id AND a.enabled=1)';
 $file=query($db,'SELECT '.$columns.' FROM internet_keenetic_files f WHERE f.id=?'.$access,$admin?[$id]:[$id,$userId])->fetch();if(!$file)problem('Файл не найден или доступ к Keenetic отключён.',404);
 cleanKeeneticOutput();header('Content-Type: application/octet-stream');header('Content-Length: '.(int)$file['byte_size']);
 $extension=strtolower(pathinfo($file['file_name'],PATHINFO_EXTENSION));if(!in_array($extension,keeneticLimits()['extensions'],true))$extension='txt';
 header('Content-Disposition: attachment; filename="keenetic-'.$id.'.'.$extension.'"; filename*=UTF-8\'\''.rawurlencode($file['file_name']));
 if($_SERVER['REQUEST_METHOD']==='GET')echo $file['content'];
} catch(AccountError $e){cleanKeeneticOutput();http_response_code($e->httpStatus);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE);}
catch(Throwable $e){cleanKeeneticOutput();error_log('Keenetic download failed');http_response_code(503);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>'Не удалось скачать конфигурацию. Обратитесь к администратору.'],JSON_UNESCAPED_UNICODE);}
