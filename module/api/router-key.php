<?php
declare(strict_types=1);
$GLOBALS['router_key_buffer_base']=ob_get_level();ob_start();ini_set('display_errors','0');ini_set('log_errors','1');
function cleanRouterKeyOutput(): void {while(ob_get_level()>($GLOBALS['router_key_buffer_base'] ?? 0)){if(!@ob_end_clean())break;}}
header('Cache-Control: private, no-store, max-age=0');header('X-Content-Type-Options: nosniff');header('Cross-Origin-Resource-Policy: same-origin');header('Referrer-Policy: no-referrer');
require_once dirname(__DIR__).'/private/runtime.php';
try {
 if (!in_array($_SERVER['REQUEST_METHOD'],['GET','HEAD'],true)){header('Allow: GET, HEAD');problem('Метод не поддерживается.',405);}
 $path=getenv('INTERNET_CONFIG') ?: dirname(__DIR__).'/private/config.php';if (!is_file($path))problem('Сервис не настроен.',503);$config=require $path;
 ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');session_name('internet_account');session_set_cookie_params(['lifetime'=>0,'path'=>'/','secure'=>$config['secure_cookies'] ?? true,'httponly'=>true,'samesite'=>'Lax']);session_start();
 if (isset($_SESSION['last_seen']) && time()-$_SESSION['last_seen']>7200){$_SESSION=[];session_regenerate_id(true);}
 $userId=(int)($_SESSION['user_id'] ?? 0);if (!$userId)problem('Сначала войдите в кабинет.',401);$_SESSION['last_seen']=time();session_write_close();
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);$user=query($db,'SELECT id,first_name,last_name,email,telegram FROM internet_users WHERE id=?',[$userId])->fetch();if (!$user)problem('Сначала войдите в кабинет.',401);
 $id=filter_var($_GET['id'] ?? null,FILTER_VALIDATE_INT);$key=authorizedRouterKey($db,$id!==false?(int)$id:0,$userId,publicUser($user,$config)['is_admin']);
 $extension=match($key['key_format']){'wireguard'=>'conf','openvpn'=>'ovpn',default=>'txt'};
 cleanRouterKeyOutput();header('Content-Type: text/plain; charset=utf-8');header('Content-Length: '.strlen($key['key_value']));header('Content-Disposition: attachment; filename="router-key-'.$key['id'].'.'.$extension.'"');
 if ($_SERVER['REQUEST_METHOD']==='GET')echo $key['key_value'];
} catch(AccountError $e){cleanRouterKeyOutput();http_response_code($e->httpStatus);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE);}
catch(Throwable $e){cleanRouterKeyOutput();error_log('Router key download failed');http_response_code(503);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>'Не удалось скачать ключ. Проверьте обновление кабинета.'],JSON_UNESCAPED_UNICODE);}
