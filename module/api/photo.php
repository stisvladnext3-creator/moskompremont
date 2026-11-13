<?php
declare(strict_types=1);
$GLOBALS['internet_photo_buffer_base']=ob_get_level();ob_start();ini_set('display_errors','0');ini_set('log_errors','1');
function cleanPhotoOutput(): void {while(ob_get_level()>($GLOBALS['internet_photo_buffer_base'] ?? 0)){if(!@ob_end_clean())break;}}
header('Cache-Control: private, no-store, max-age=0');header('X-Content-Type-Options: nosniff');header('Cross-Origin-Resource-Policy: same-origin');header('Referrer-Policy: no-referrer');
require_once dirname(__DIR__).'/private/runtime.php';
try {
 if (!in_array($_SERVER['REQUEST_METHOD'],['GET','HEAD'],true)){header('Allow: GET, HEAD');problem('Метод не поддерживается.',405);}
 $path=getenv('INTERNET_CONFIG') ?: dirname(__DIR__).'/private/config.php';if (!is_file($path))problem('Сервис не настроен.',503);$config=require $path;
 ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');session_name('internet_account');
 session_set_cookie_params(['lifetime'=>0,'path'=>'/','secure'=>$config['secure_cookies'] ?? true,'httponly'=>true,'samesite'=>'Lax']);session_start();
 $id=$_GET['id'] ?? '';if (!is_string($id) || !preg_match('/\A[a-f0-9]{32}\z/D',$id))problem('Фото не найдено.',404);
 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);
 query($db,"SET time_zone='+00:00'");$user=accountSessionUser($db,$config);if(!$user)problem('Сначала войдите в кабинет.',401);$userId=(int)$user['id'];session_write_close();
 $photo=query($db,'SELECT p.id,p.mime,p.byte_size,t.user_id AS ticket_owner FROM internet_message_photos p JOIN internet_messages m ON m.id=p.message_id JOIN internet_tickets t ON t.id=m.ticket_id WHERE p.id=?',[$id])->fetch();
 if (!$photo || ((int)$photo['ticket_owner']!==$userId && !publicUser($user,$config)['is_admin']))problem('Фото не найдено.',404);
 $extensions=['image/jpeg'=>'jpg','image/png'=>'png','image/webp'=>'webp'];if (!isset($extensions[$photo['mime']]))problem('Фото не найдено.',404);
 $bytes=$_SERVER['REQUEST_METHOD']==='GET'?query($db,'SELECT content FROM internet_message_photos WHERE id=?',[$id])->fetchColumn():'';if ($bytes===false)problem('Фото не найдено.',404);
 cleanPhotoOutput();header('Content-Type: '.$photo['mime']);header('Content-Length: '.(int)$photo['byte_size']);header('Content-Disposition: inline; filename="photo-'.$id.'.'.$extensions[$photo['mime']].'"');
 if ($_SERVER['REQUEST_METHOD']==='GET')echo $bytes;
} catch(AccountError $e){cleanPhotoOutput();http_response_code($e->httpStatus);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE);}
catch(Throwable $e){cleanPhotoOutput();error_log('Internet photo: request failed');http_response_code(503);header('Content-Type: application/json; charset=utf-8');echo json_encode(['error'=>'Не удалось загрузить фото. Проверьте обновление базы и попробуйте позже.'],JSON_UNESCAPED_UNICODE);}
