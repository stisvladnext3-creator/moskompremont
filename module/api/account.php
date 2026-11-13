<?php
declare(strict_types=1);
// Keep notices, a BOM in a local config and included-file output out of API JSON.
$GLOBALS['internet_json_buffer_base']=ob_get_level();$GLOBALS['internet_json_replied']=false;ob_start();
ini_set('display_errors','0');ini_set('log_errors','1');
header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');header('X-Content-Type-Options: nosniff');
function cleanJsonOutput(): void {while(ob_get_level()>($GLOBALS['internet_json_buffer_base'] ?? 0)){if(!@ob_end_clean())break;}}
register_shutdown_function(static function(){
 if (!empty($GLOBALS['internet_json_replied']))return;
 cleanJsonOutput();http_response_code(500);header('Content-Type: application/json; charset=utf-8');
 echo json_encode(['error'=>'Не удалось выполнить запрос. Обратитесь к администратору сайта.'],JSON_UNESCAPED_UNICODE);
});
function answer(array $body,int $status=200): never {
 $payload=json_encode($body,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_INVALID_UTF8_SUBSTITUTE);
 $GLOBALS['internet_json_replied']=true;cleanJsonOutput();http_response_code($status);echo $payload;exit;
}
require_once dirname(__DIR__).'/private/runtime.php';
require_once dirname(__DIR__).'/private/monitoring.php';
require_once dirname(__DIR__).'/private/keenetic.php';
require_once dirname(__DIR__).'/private/service-notice.php';
require_once dirname(__DIR__).'/private/router-hosts.php';
require_once dirname(__DIR__).'/private/subscription-hosts.php';
require_once dirname(__DIR__).'/private/subscription-delete.php';
$db=null;
try {
 $path=getenv('INTERNET_CONFIG') ?: dirname(__DIR__).'/private/config.php';if (!is_file($path))problem('Кабинет ещё не настроен. Обратитесь к владельцу сайта.',503);
 $config=require $path;if (!is_array($config) || empty($config['origin']))problem('Не настроен адрес сайта.',503);
 ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');ini_set('session.gc_maxlifetime','7200');session_name('internet_account');
 session_set_cookie_params(['lifetime'=>0,'path'=>'/','secure'=>$config['secure_cookies'] ?? true,'httponly'=>true,'samesite'=>'Lax']);session_start();

 $db=new PDO($config['dsn'],$config['db_user'],$config['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_DEFAULT_FETCH_MODE=>PDO::FETCH_ASSOC,PDO::ATTR_EMULATE_PREPARES=>false]);query($db,"SET time_zone='+00:00'");
 $user=accountSessionUser($db,$config);$_SESSION['last_seen']=time();$_SESSION['csrf'] ??=bin2hex(random_bytes(32));
 $action=$_GET['action'] ?? 'me';$method=$_SERVER['REQUEST_METHOD'];
 $writes=['register','login','logout','create_order','submit_payment','review_order','claim_subscriptions','issue_subscription','import_subscriptions','resolve_claim','create_ticket','reply_ticket','read_ticket','set_ticket_status','read_event','mail_test','mail_retry','unassign_subscription','direct_assign_subscriptions','admin_assign_router_key','admin_unassign_router_key','admin_monitor_save','admin_monitor_token','admin_delete_router_key','admin_keenetic_access','admin_keenetic_upload','admin_keenetic_delete','admin_service_notice_save','admin_router_host_preview','admin_router_host_apply','admin_subscription_host_preview','admin_subscription_host_apply','admin_delete_subscription','admin_subscription_term_save','notification_read','admin_template_save','admin_template_delete','push_subscribe','push_unsubscribe','push_device_status','admin_push_setup','admin_push_save','admin_push_retry','admin_account_password','admin_account_delete'];
 if (in_array($action,$writes,true)) {
  if ($method!=='POST')problem('Требуется POST.',405);
  if (rtrim($_SERVER['HTTP_ORIGIN'] ?? '','/')!==rtrim($config['origin'],'/'))problem('Недопустимый источник запроса.',403);
  if (!hash_equals($_SESSION['csrf'],$_SERVER['HTTP_X_CSRF_TOKEN'] ?? ''))answer(['error'=>'Сессия обновлена. Повторите запрос.','code'=>'csrf_refresh','csrf'=>$_SESSION['csrf']],403);
  $multipart=stripos($_SERVER['CONTENT_TYPE'] ?? '','multipart/form-data')===0;
  if ($multipart) {
   if (!in_array($action,['create_ticket','reply_ticket','admin_keenetic_upload'],true))problem('Для этого действия требуется JSON.',415);
   if ((int)($_SERVER['CONTENT_LENGTH'] ?? 0)>16*1024*1024 || ((int)($_SERVER['CONTENT_LENGTH'] ?? 0)>0 && !$_POST && !$_FILES))problem('Загрузка превышает лимит хостинга. Для трёх файлов до 5 МБ установите post_max_size=20M и upload_max_filesize=5M.',413);
   $body=$_POST;
  } else {
   if (stripos($_SERVER['CONTENT_TYPE'] ?? '','application/json')!==0)problem('Требуется JSON.',415);
   $max=match($action){'import_subscriptions'=>1500000,'direct_assign_subscriptions','admin_assign_router_key'=>200000,default=>20000};
   if ((int)($_SERVER['CONTENT_LENGTH'] ?? 0)>$max)problem('Слишком большой запрос.',413);
   $raw=file_get_contents('php://input',false,null,0,$max+1);if (strlen($raw)>$max)problem('Слишком большой запрос.',413);
   try {$body=json_decode($raw,true,16,JSON_THROW_ON_ERROR);}catch(Throwable $e){problem('Некорректный JSON.');}
   if (!is_array($body))problem('Некорректные данные.');
  }
 } else {if ($method!=='GET')problem('Метод не поддерживается.',405);$body=[];}
 register_shutdown_function(static function()use($db,$config){
  if ((empty($GLOBALS['internet_mail_queued'])&&empty($GLOBALS['internet_push_queued'])) || $db->inTransaction())return;
  if (session_status()===PHP_SESSION_ACTIVE)session_write_close();
  // CGI/CLI cannot reliably finish an HTTP response before SMTP; cron drains their queue.
  if (!function_exists('fastcgi_finish_request'))return;
  fastcgi_finish_request();$level=ob_get_level();ob_start();
  try {if(!empty($GLOBALS['internet_push_queued']))sendClientPush($db,$config,20);if(!empty($GLOBALS['internet_mail_queued']))sendPendingMail($db,$config,1);}catch(Throwable $e){error_log('Internet mail: queue processing failed.');}
  finally {while(ob_get_level()>$level){if(!@ob_end_clean())break;}}
 });
 if ($action==='me')answer(['user'=>$user?publicUser($user,$config):null,'csrf'=>$_SESSION['csrf'],'payment'=>payment($config),'version'=>17,'photo_limits'=>photoLimits()]);
 if ($action==='register') {
  limitRate($db,'register',10);$first=field($body,'first_name',320);$last=field($body,'last_name',320);
  if (!$first || !$last || preg_match_all('/./u',$first)>80 || preg_match_all('/./u',$last)>80)problem('Укажите имя и фамилию (до 80 символов).');
  $email=strtolower(field($body,'email',254));if (!filter_var($email,FILTER_VALIDATE_EMAIL))problem('Укажите корректную почту.');
  $telegram=registrationContact(field($body,'telegram',100));
  $password=$body['password'] ?? null;if (!is_string($password) || preg_match_all('/./us',$password)<6 || strlen($password)>72)problem('Пароль: не менее 6 символов и не более 72 байт.');
  $names=claimNames(field($body,'existing_subscriptions',2500,true));
  if (!empty($body['has_existing_subscriptions']) && !$names)problem('Укажите названия имеющихся подписок.');
  $hash=password_hash($password,PASSWORD_DEFAULT);$db->beginTransaction();
  try {query($db,'INSERT INTO internet_users(first_name,last_name,telegram,email,password_hash) VALUES (?,?,?,?,?)',[$first,$last,$telegram,$email,$hash]);}
  catch(PDOException $e){if ($e->getCode()==='23000')problem('Не удалось зарегистрироваться с этими данными. Попробуйте войти.',409);throw $e;}
  $id=(int)$db->lastInsertId();$newUser=['id'=>$id,'first_name'=>$first,'last_name'=>$last,'telegram'=>$telegram,'email'=>$email];claimSubscriptions($db,$id,$names);
  notifyAdmin($db,$config,'user:'.$id,'user',$id,'Новый клиент #'.$id,contactText($newUser)."\nИмеющиеся подписки: ".($names?implode(', ',$names):'не указаны'));
  $db->commit();establishAccountSession($db,$config,rawAccount($db,$id),($body['remember'] ?? false)===true);answer(['user'=>publicUser($newUser,$config),'csrf'=>$_SESSION['csrf']],201);
 }
 if ($action==='login') {
  limitRate($db,'login',25);$email=strtolower(field($body,'email',254));$password=$body['password'] ?? '';
  if (!is_string($password) || strlen($password)>72)problem('Неверная почта или пароль.',401);
  $found=query($db,'SELECT * FROM internet_users WHERE email=?',[$email])->fetch();$dummy='$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2uheWG/igi';$valid=password_verify($password,$found['password_hash'] ?? $dummy);
  if (!$found || !$valid || authState($db,(int)$found['id'])['deleted_at']!==null)problem('Неверная почта или пароль.',401);
  if (password_needs_rehash($found['password_hash'],PASSWORD_DEFAULT)){$found['password_hash']=password_hash($password,PASSWORD_DEFAULT);query($db,'UPDATE internet_users SET password_hash=? WHERE id=?',[$found['password_hash'],$found['id']]);}
  establishAccountSession($db,$config,$found,($body['remember'] ?? false)===true);answer(['user'=>publicUser($found,$config),'csrf'=>$_SESSION['csrf']]);
 }
 if ($action==='logout'){forgetRemember($db,$config);$_SESSION=[];session_regenerate_id(true);$_SESSION['csrf']=bin2hex(random_bytes(32));answer(['ok'=>true,'csrf'=>$_SESSION['csrf']]);}
 if (!$user)problem('Сначала войдите в кабинет.',401);$userId=(int)$user['id'];$admin=publicUser($user,$config)['is_admin'];
 if (str_starts_with($action,'admin_') || in_array($action,['review_order','issue_subscription','import_subscriptions','resolve_claim','set_ticket_status','read_event','mail_test','mail_retry','unassign_subscription','direct_assign_subscriptions'],true)) {if (!$admin)problem('Нет доступа.',403);}
 require __DIR__.'/../private/client-actions.php';
 if ($action==='admin_subscription_term_save')answer(saveSubscriptionTerm($db,$body,$userId));
 if ($action==='admin_subscription_terms') {
  subscriptionTermsReady($db);$search=isset($_GET['q'])?field(['q'=>$_GET['q']],'q',254):'';$offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $where=$search!==''?' WHERE s.name LIKE ? OR u.email LIKE ? OR u.first_name LIKE ? OR u.last_name LIKE ?':'';$args=$search!==''?array_fill(0,4,'%'.$search.'%'):[];
  $total=(int)query($db,'SELECT COUNT(*) FROM internet_subscriptions s LEFT JOIN internet_users u ON u.id=s.user_id'.$where,$args)->fetchColumn();
  $rows=query($db,'SELECT s.id,s.name,s.user_id,u.first_name,u.last_name,u.email FROM internet_subscriptions s LEFT JOIN internet_users u ON u.id=s.user_id'.$where." ORDER BY s.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();answer(['subscriptions'=>withSubscriptionTerms($db,$rows,true),'total'=>$total]);
 }
 if ($action==='admin_delete_subscription')answer(deleteSubscription($db,$body,$userId));
 if ($action==='admin_subscription_host_preview')answer(subscriptionHostPreview($db,$body));
 if ($action==='admin_subscription_host_apply')answer(subscriptionHostApply($db,$body,$userId));
 if ($action==='admin_router_host_preview')answer(routerHostPreview($db,$body));
 if ($action==='admin_router_host_apply')answer(routerHostApply($db,$body,$userId));
 if ($action==='admin_service_notice')answer(serviceNoticeAdmin($db));
 if ($action==='admin_service_notice_save')answer(serviceNoticeSave($db,$body,$userId));
 if ($action==='admin_monitor')answer(monitorAdmin($db));
 if ($action==='admin_monitor_save')answer(monitorSave($db,$body));
 if ($action==='admin_monitor_token')answer(['token'=>monitorToken($db)]);
 if ($action==='admin_clients') {
  [$where,$args]=clientDirectoryConditions($db,$_GET);$offset=max(0,min(1000000,(int)($_GET['offset'] ?? 0)));
  $total=(int)query($db,'SELECT COUNT(*) FROM internet_users u'.$where,$args)->fetchColumn();
  $rows=query($db,'SELECT u.id,u.first_name,u.last_name,u.email,u.telegram,u.created_at FROM internet_users u'.$where." ORDER BY u.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();
  $users=[];foreach($rows as $row){$id=(int)$row['id'];$row['subscriptions']=[];$row['router_keys']=[];$users[$id]=$row;}
  if($users){$ids=array_keys($users);$marks=implode(',',array_fill(0,count($ids),'?'));
   foreach(withSubscriptionTerms($db,query($db,"SELECT id,user_id,name,assigned_at FROM internet_subscriptions WHERE user_id IN ($marks) ORDER BY name",$ids)->fetchAll(),true) as $sub)$users[(int)$sub['user_id']]['subscriptions'][]=$sub;
   foreach(withResourceUpdates($db,query($db,"SELECT id,user_id,label,key_format,assigned_at FROM internet_router_keys WHERE user_id IN ($marks) ORDER BY label,id",$ids)->fetchAll(),'router_key') as $key)$users[(int)$key['user_id']]['router_keys'][]=$key;
  }
  $keeneticReady=keeneticReady($db);
  foreach($users as &$row){$row['keenetic_enabled']=false;$row['keenetic_files_count']=0;}unset($row);
  if($users && $keeneticReady){
   foreach(query($db,"SELECT user_id,enabled FROM internet_keenetic_access WHERE user_id IN ($marks)",$ids)->fetchAll() as $access)$users[(int)$access['user_id']]['keenetic_enabled']=(bool)$access['enabled'];
   foreach(query($db,"SELECT user_id,COUNT(*) AS n FROM internet_keenetic_files WHERE user_id IN ($marks) GROUP BY user_id",$ids)->fetchAll() as $file)$users[(int)$file['user_id']]['keenetic_files_count']=(int)$file['n'];
  }
  answer(['users'=>array_values($users),'total'=>$total,'keenetic_ready'=>$keeneticReady]);
 }
 if ($action==='admin_user_search') {
  $search=isset($_GET['q'])?field(['q'=>$_GET['q']],'q',254):'';
  $where='';$args=[];$order=' ORDER BY id DESC';
  if ($search!==''){$where=" WHERE email LIKE ? OR first_name LIKE ? OR last_name LIKE ? OR CONCAT(first_name,' ',last_name) LIKE ? OR telegram LIKE ? OR id=?";$like='%'.$search.'%';$contactLike='%'.(phoneContact($search) ?? $search).'%';$args=[$like,$like,$like,$like,$contactLike,preg_match('/\A[0-9]+\z/D',$search)?(int)$search:0,$search];$order=' ORDER BY CASE WHEN email=? THEN 0 ELSE 1 END,id DESC';}
  if(authTablesReady($db))$where=($where!==''?' WHERE ('.substr($where,7).') AND ':' WHERE ').'NOT EXISTS (SELECT 1 FROM internet_auth_state a WHERE a.user_id=internet_users.id AND a.deleted_at IS NOT NULL)';
  $rows=query($db,'SELECT id,first_name,last_name,email,telegram FROM internet_users'.$where.$order.' LIMIT 50',$args)->fetchAll();
  answer(['users'=>$rows,'limit'=>50]);
 }
 if ($action==='direct_assign_subscriptions') {
  if (!isset($body['user_id']) || !is_int($body['user_id']) || $body['user_id']<1)problem('Выберите клиента для назначения подписок.');
  $wanted=directAssignmentItems($body);$recipient=query($db,'SELECT id,first_name,last_name,email,telegram FROM internet_users WHERE id=?',[$body['user_id']])->fetch();
  if (!$recipient)problem('Клиент не найден. Обновите список пользователей.',404);
  $db->beginTransaction();
  try {$result=assignDirectly($db,$recipient,$userId,$wanted);$db->commit();}
  catch(PDOException $e){if ($db->inTransaction())$db->rollBack();if (in_array($e->getCode(),['23000','40001'],true))problem('База подписок изменилась. Обновите список и повторите назначение.',409);throw $e;}
  answer($result);
 }
 if ($action==='keenetic') {
  $enabled=keeneticEnabled($db,$userId);answer(['enabled'=>$enabled,'files'=>$enabled?keeneticFiles($db,$userId):[]]);
 }
 if ($action==='admin_keenetic') {
  keeneticRequireReady($db);$recipient=keeneticRecipient($db,$_GET['user_id'] ?? null);$id=(int)$recipient['id'];
  answer(['user'=>$recipient,'enabled'=>keeneticEnabled($db,$id),'files'=>keeneticFiles($db,$id),'limits'=>keeneticLimits()]);
 }
 if ($action==='admin_keenetic_access') {
  keeneticRequireReady($db);if(!isset($body['enabled'])||!is_bool($body['enabled']))problem('Укажите, включать ли вкладку Keenetic.');
  $db->beginTransaction();$recipient=keeneticRecipient($db,$body['user_id'] ?? null,true);$id=(int)$recipient['id'];
  query($db,'INSERT INTO internet_keenetic_access(user_id,enabled,updated_by) VALUES (?,?,?) ON DUPLICATE KEY UPDATE enabled=VALUES(enabled),updated_by=VALUES(updated_by)',[$id,(int)$body['enabled'],$userId]);
  $db->commit();answer(['ok'=>true,'user_id'=>$id,'enabled'=>$body['enabled']]);
 }
 if ($action==='admin_keenetic_upload') {
  keeneticRequireReady($db);$files=uploadedKeeneticFiles($_FILES);$db->beginTransaction();$recipient=keeneticRecipient($db,$body['user_id'] ?? null,true);$id=(int)$recipient['id'];
  $ids=saveKeeneticFiles($db,$id,$userId,$files);$db->commit();answer(['ok'=>true,'ids'=>$ids,'user_id'=>$id],201);
 }
 if ($action==='admin_keenetic_delete') {
  keeneticRequireReady($db);$id=keeneticFileId($body['id'] ?? null);$db->beginTransaction();$recipient=keeneticRecipient($db,$body['user_id'] ?? null,true);
  $file=query($db,'SELECT id FROM internet_keenetic_files WHERE id=? AND user_id=? FOR UPDATE',[$id,$recipient['id']])->fetch();if(!$file)problem('Файл не найден у выбранного клиента. Обновите список.',404);
  query($db,'DELETE FROM internet_keenetic_files WHERE id=?',[$id]);$db->commit();answer(['ok'=>true]);
 }
 if ($action==='router_keys') {
  $keys=query($db,'SELECT id,label,key_format,assigned_at FROM internet_router_keys WHERE user_id=? ORDER BY assigned_at DESC,id DESC',[$userId])->fetchAll();answer(['keys'=>withResourceUpdates($db,$keys,'router_key')]);
 }
 if ($action==='router_key') {
  $id=filter_var($_GET['id'] ?? null,FILTER_VALIDATE_INT);answer(['key'=>authorizedRouterKey($db,$id!==false?(int)$id:0,$userId,$admin)]);
 }
 if ($action==='admin_router_keys') {
  $search=isset($_GET['q'])?field(['q'=>$_GET['q']],'q',254):'';$offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $where=$search!==''?' WHERE k.label LIKE ? OR u.email LIKE ? OR u.first_name LIKE ? OR u.last_name LIKE ?':'';$like='%'.$search.'%';$args=$search!==''?[$like,$like,$like,$like]:[];
  $total=(int)query($db,'SELECT COUNT(*) FROM internet_router_keys k LEFT JOIN internet_users u ON u.id=k.user_id'.$where,$args)->fetchColumn();
  $rows=query($db,'SELECT k.id,k.label,k.key_format,k.user_id,k.assigned_at,k.created_at,u.first_name,u.last_name,u.email FROM internet_router_keys k LEFT JOIN internet_users u ON u.id=k.user_id'.$where." ORDER BY k.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();answer(['keys'=>withResourceUpdates($db,$rows,'router_key'),'total'=>$total]);
 }
 if ($action==='admin_assign_router_key') {
  if (!isset($body['user_id']) || !is_int($body['user_id']) || $body['user_id']<1)problem('Выберите клиента.');
  $recipientId=$body['user_id'];if (!query($db,'SELECT id FROM internet_users WHERE id=?',[$recipientId])->fetch())problem('Клиент не найден.',404);
  $input=routerKeyInput($body);$db->beginTransaction();activeAccount($db,$recipientId,true);
  try {
   $existing=query($db,'SELECT id,user_id,label FROM internet_router_keys WHERE key_hash=? FOR UPDATE',[$input['key_hash']])->fetch();
   if ($existing && $existing['user_id']!==null && (int)$existing['user_id']!==$recipientId)problem('Этот ключ уже закреплён за другим клиентом. Сначала снимите прежнюю привязку.',409);
   $already=$existing && (int)$existing['user_id']===$recipientId;
   if ($already){$id=(int)$existing['id'];}
   elseif ($existing){$id=(int)$existing['id'];query($db,'UPDATE internet_router_keys SET label=?,key_format=?,user_id=?,issued_by=?,assigned_at=CURRENT_TIMESTAMP WHERE id=?',[$input['label'],$input['key_format'],$recipientId,$userId,$id]);}
   else {query($db,'INSERT INTO internet_router_keys(label,key_format,key_value,key_hash,user_id,issued_by,assigned_at) VALUES (?,?,?,?,?,?,CURRENT_TIMESTAMP)',[$input['label'],$input['key_format'],$input['key_value'],$input['key_hash'],$recipientId,$userId]);$id=(int)$db->lastInsertId();resourceChanged($db,'router_key',$id);}
   if (!$already){notifyClient($db,$recipientId,'router-key-assigned:'.bin2hex(random_bytes(12)),'router_key_assigned','Ключ для роутера выдан','Ключ «'.$input['label'].'» доступен в вашем кабинете.','router-keys');}
   if (!$already)routerKeyJournal($db,$recipientId,$userId,$id,$input['label']);
   $db->commit();
  } catch(PDOException $e){if ($db->inTransaction())$db->rollBack();if (in_array($e->getCode(),['23000','40001'],true))problem('Ключи изменились. Обновите список и повторите выдачу.',409);throw $e;}
  answer(['ok'=>true,'id'=>$id,'already_assigned'=>(bool)$already,'user_id'=>$recipientId]);
 }
 if ($action==='admin_delete_router_key') {
  $id=$body['id'] ?? null;$owner=$body['user_id'] ?? null;
  if(!is_int($id)||$id<1||!array_key_exists('user_id',$body)||($owner!==null&&(!is_int($owner)||$owner<1)))problem('Выберите ключ и его текущего владельца.');
  $db->beginTransaction();$key=query($db,'SELECT id,label,user_id FROM internet_router_keys WHERE id=? FOR UPDATE',[$id])->fetch();if(!$key)problem('Ключ уже удалён.',404);
  $current=$key['user_id']===null?null:(int)$key['user_id'];if($current!==$owner)problem('Владелец ключа изменился. Обновите список перед удалением.',409);
  query($db,'DELETE FROM internet_router_keys WHERE id=?',[$id]);resourceRemoved($db,'router_key',$id);if($owner!==null)routerKeyJournal($db,$owner,$userId,$id,$key['label'],true,true);
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='admin_unassign_router_key') {
  $id=$body['id'] ?? null;$owner=$body['user_id'] ?? null;if (!is_int($id) || !is_int($owner) || $id<1 || $owner<1)problem('Выберите ключ и его текущего владельца.');
  $db->beginTransaction();$key=query($db,'SELECT id,label,user_id FROM internet_router_keys WHERE id=? FOR UPDATE',[$id])->fetch();
  if (!$key || $key['user_id']===null || (int)$key['user_id']!==$owner)problem('Привязка ключа уже изменилась. Обновите список.',409);
  query($db,'UPDATE internet_router_keys SET user_id=NULL,assigned_at=NULL,issued_by=? WHERE id=?',[$userId,$id]);routerKeyJournal($db,$owner,$userId,$id,$key['label'],true);$db->commit();answer(['ok'=>true]);
 }
 if ($action==='subscriptions') {
  $items=query($db,'SELECT id,name,url,assigned_at FROM internet_subscriptions WHERE user_id=? ORDER BY assigned_at DESC,id DESC',[$userId])->fetchAll();
  $claims=query($db,'SELECT id,name,status,created_at FROM internet_subscription_claims WHERE user_id=? ORDER BY id DESC',[$userId])->fetchAll();answer(['subscriptions'=>withSubscriptionTerms($db,$items),'claims'=>$claims]);
 }
 if ($action==='claim_subscriptions') {
  limitRate($db,'claims',20,$userId);$names=claimNames(field($body,'names',2500,true));if (!$names)problem('Укажите название подписки.');
  $existing=(int)query($db,'SELECT COUNT(*) FROM internet_subscription_claims WHERE user_id=?',[$userId])->fetchColumn();if ($existing+count($names)>100)problem('Слишком много запросов. Напишите в поддержку.');
  $db->beginTransaction();activeAccount($db,$userId,true);claimSubscriptions($db,$userId,$names);
  notifyAdmin($db,$config,'claim:'.bin2hex(random_bytes(12)),'subscription',$userId,'Запрос подписок от клиента #'.$userId,contactText($user)."\nНазвания: ".implode(', ',$names),null,false);$db->commit();answer(['ok'=>true]);
 }
 if ($action==='orders') {
  $rows=query($db,'SELECT * FROM internet_orders WHERE user_id=? ORDER BY created_at DESC,id DESC LIMIT 100',[$userId])->fetchAll();answer(['orders'=>withOrderSubscriptions($db,$rows)]);
 }
 if ($action==='create_order') {
  limitRate($db,'orders',30,$userId);if (!payment($config)['available'])problem('Владелец ещё не добавил реквизиты оплаты.',503);
  $plan=field($body,'plan',20);$devices=$body['devices'] ?? null;if (!in_array($plan,['monthly','lifetime'],true) || !is_int($devices) || $devices<1 || $devices>20)problem('Выберите тариф и от 1 до 20 устройств.');
  subscriptionTermsReady($db);$months=termMonthsInput($body['months'] ?? 1);if($plan==='lifetime'&&$months!==1)problem('Для бессрочного тарифа количество месяцев не требуется.');
  $rates=$plan==='monthly'?[1=>250,2=>225,3=>200,4=>175]:[1=>3000,2=>2500,3=>2000,4=>1650];$amount=$rates[min($devices,4)]*$devices*($plan==='monthly'?$months:1);
  $db->beginTransaction();activeAccount($db,$userId,true);
  if (query($db,"SELECT id FROM internet_orders WHERE user_id=? AND status IN ('created','submitted') LIMIT 1",[$userId])->fetch())problem('У вас уже есть незавершённая заявка. Откройте её в списке.',409);
  $id=bin2hex(random_bytes(16));query($db,'INSERT INTO internet_orders(id,user_id,plan,devices,amount) VALUES (?,?,?,?,?)',[$id,$userId,$plan,$devices,$amount]);query($db,'INSERT INTO internet_order_terms(order_id,months) VALUES (?,?)',[$id,$months]);$db->commit();answer(['id'=>$id,'amount'=>$amount,'months'=>$months],201);
 }
 if ($action==='submit_payment') {
  $id=field($body,'id',32);$reference=field($body,'reference',500);if (preg_match_all('/./us',$reference)<4)problem('Укажите время перевода или последние 4 цифры телефона.');
  $db->beginTransaction();$order=query($db,'SELECT * FROM internet_orders WHERE id=? AND user_id=? FOR UPDATE',[$id,$userId])->fetch();if (!$order || $order['status']!=='created')problem('Заявка не найдена или уже отправлена.',409);
  query($db,"UPDATE internet_orders SET status='submitted',payment_reference=?,submitted_at=CURRENT_TIMESTAMP WHERE id=?",[$reference,$id]);
  notifyAdmin($db,$config,'payment:'.$id,'payment',$userId,'Клиент сообщил об оплате',contactText($user)."\nЗаявка: $id\nТариф: ".($order['plan']==='monthly'?'На '.orderMonths($db,$id).' мес.':'Навсегда')."\nУстройств: ".$order['devices']."\nСумма: ".$order['amount']." ₽\nДанные перевода: ".$reference."\n\nПроверьте поступление в Озон Банке. Нажатие кнопки клиентом не подтверждает оплату.",$id);
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='review_order') {
  $id=field($body,'id',32);$status=field($body,'status',20);if (!in_array($status,['paid','rejected'],true))problem('Недопустимый статус.');
  $db->beginTransaction();$order=query($db,'SELECT * FROM internet_orders WHERE id=? FOR UPDATE',[$id])->fetch();if (!$order || $order['status']!=='submitted')problem('Заявка уже обработана или не отправлена на проверку.',409);
  query($db,'UPDATE internet_orders SET status=?,reviewed_at=CURRENT_TIMESTAMP,reviewed_by=? WHERE id=?',[$status,$userId,$id]);$db->commit();answer(['ok'=>true]);
 }
 if ($action==='issue_subscription') {
  $id=field($body,'order_id',32);$name=subscriptionName(field($body,'name',100));$url=field($body,'url',8192);if ($url!=='')$url=subscriptionUrl($url);
  $db->beginTransaction();$order=query($db,'SELECT * FROM internet_orders WHERE id=? FOR UPDATE',[$id])->fetch();if (!$order || $order['status']!=='paid')problem('Сначала подтвердите поступление оплаты.',409);
  activeAccount($db,(int)$order['user_id'],true);
  $sub=query($db,'SELECT * FROM internet_subscriptions WHERE name=? FOR UPDATE',[$name])->fetch();
  $alreadyIssued=$sub&&(bool)query($db,'SELECT order_id FROM internet_order_subscriptions WHERE order_id=? AND subscription_id=?',[$id,$sub['id']])->fetch();
  if ($sub && (int)$sub['user_id']===(int)$order['user_id'] && $alreadyIssued)problem('Эта подписка уже выдана по данной заявке.',409);
  if ($sub && $sub['user_id']!==null && (int)$sub['user_id']!==(int)$order['user_id'])problem('Эта подписка уже закреплена за другим клиентом.',409);
  if (!$sub && $url==='')problem('Вставьте ссылку новой подписки или укажите название из базы.');
  if ($sub) {
   $subId=(int)$sub['id'];query($db,'UPDATE internet_subscriptions SET user_id=?,url=?,assigned_at=COALESCE(assigned_at,CURRENT_TIMESTAMP) WHERE id=?',[$order['user_id'],$url!==''?$url:$sub['url'],$subId]);
  } else {query($db,'INSERT INTO internet_subscriptions(name,url,user_id,assigned_at) VALUES (?,?,?,CURRENT_TIMESTAMP)',[$name,$url,$order['user_id']]);$subId=(int)$db->lastInsertId();}
  if(!$sub||($url!==''&&$url!==$sub['url']))resourceChanged($db,'subscription',$subId);
  notifyClient($db,(int)$order['user_id'],'subscription-issued:'.bin2hex(random_bytes(12)),'subscription_assigned','Подписка выдана','Подписка «'.$name.'» выдана по оплаченной заявке. Откройте «Мои подписки».','subscriptions');
  issueOrderSubscriptionTerm($db,$subId,$order,$userId,(bool)$alreadyIssued);
  query($db,'INSERT INTO internet_order_subscriptions(order_id,subscription_id,issued_by) VALUES (?,?,?) ON DUPLICATE KEY UPDATE issued_by=VALUES(issued_by),issued_at=CURRENT_TIMESTAMP',[$id,$subId,$userId]);
  query($db,'DELETE FROM internet_subscription_revocations WHERE subscription_id=? AND user_id=?',[$subId,$order['user_id']]);
  query($db,"UPDATE internet_subscription_claims SET status='approved',reviewed_at=CURRENT_TIMESTAMP WHERE user_id=? AND name=? AND status IN ('pending','revoked','rejected')",[$order['user_id'],$name]);
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='unassign_subscription') {
  $id=(int)($body['id'] ?? 0);$expectedOwner=(int)($body['user_id'] ?? 0);if ($id<1 || $expectedOwner<1)problem('Выберите подписку и её текущего владельца.');
  $db->beginTransaction();$sub=query($db,'SELECT * FROM internet_subscriptions WHERE id=? FOR UPDATE',[$id])->fetch();
  if (!$sub || $sub['user_id']===null || (int)$sub['user_id']!==$expectedOwner)problem('Привязка уже изменилась. Обновите базу подписок.',409);
  query($db,'INSERT INTO internet_subscription_revocations(subscription_id,user_id,revoked_by) VALUES (?,?,?) ON DUPLICATE KEY UPDATE revoked_by=VALUES(revoked_by),revoked_at=CURRENT_TIMESTAMP',[$id,$expectedOwner,$userId]);
  query($db,'UPDATE internet_subscriptions SET user_id=NULL,assigned_at=NULL WHERE id=?',[$id]);
  query($db,"INSERT INTO internet_subscription_claims(user_id,name,status,reviewed_at) VALUES (?,?,'revoked',CURRENT_TIMESTAMP) ON DUPLICATE KEY UPDATE status='revoked',reviewed_at=CURRENT_TIMESTAMP",[$expectedOwner,$sub['name']]);
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='import_subscriptions') {
  limitRate($db,'imports',20,$userId);$records=importCsv(field($body,'csv',1048576,true));$added=0;$updated=0;$assigned=0;$unchanged=0;$affected=[];$db->beginTransaction();
  foreach ($records as $name=>$url) {
   $existing=query($db,'SELECT id,url,user_id FROM internet_subscriptions WHERE name=? FOR UPDATE',[$name])->fetch();
   if($existing){$id=(int)$existing['id'];if($existing['url']!==$url){query($db,'UPDATE internet_subscriptions SET url=? WHERE id=?',[$url,$id]);resourceChanged($db,'subscription',$id);$updated++;if($existing['user_id']!==null)$affected[(int)$existing['user_id']][]=$name;}else $unchanged++;}
   else {query($db,'INSERT INTO internet_subscriptions(name,url) VALUES (?,?)',[$name,$url]);resourceChanged($db,'subscription',(int)$db->lastInsertId());$added++;}
   $assigned+=reconcileClaims($db,$name);
  }
  notifyResourceBatch($db,'subscription',$affected);$db->commit();answer(['added'=>$added,'updated'=>$updated,'assigned'=>$assigned,'unchanged'=>$unchanged]);
 }
 if ($action==='resolve_claim') {
  $id=(int)($body['id'] ?? 0);$status=field($body,'status',20);if (!in_array($status,['approved','rejected'],true))problem('Недопустимый статус.');
  $db->beginTransaction();$claim=query($db,'SELECT * FROM internet_subscription_claims WHERE id=?',[$id])->fetch();if (!$claim)problem('Запрос уже обработан.',409);
  activeAccount($db,(int)$claim['user_id'],true);$sub=query($db,'SELECT * FROM internet_subscriptions WHERE name=? FOR UPDATE',[$claim['name']])->fetch();
  $claim=query($db,'SELECT * FROM internet_subscription_claims WHERE id=? FOR UPDATE',[$id])->fetch();if (!$claim || !in_array($claim['status'],['pending','revoked'],true))problem('Запрос уже обработан.',409);
  if ($status==='approved') {
   if (!$sub)problem('Сначала добавьте подписку в базу.',409);
   if ($sub['user_id']!==null && (int)$sub['user_id']!==(int)$claim['user_id'])problem('Подписка уже закреплена за другим клиентом.',409);
   query($db,'UPDATE internet_subscriptions SET user_id=?,assigned_at=COALESCE(assigned_at,CURRENT_TIMESTAMP) WHERE id=?',[$claim['user_id'],$sub['id']]);
   query($db,'DELETE FROM internet_subscription_revocations WHERE subscription_id=? AND user_id=?',[$sub['id'],$claim['user_id']]);
   if($sub['user_id']===null)notifyClient($db,(int)$claim['user_id'],'claim-approved:'.$id,'subscription_assigned','Подписка выдана','Подписка «'.$sub['name'].'» доступна в кабинете.','subscriptions');
  }
  query($db,'UPDATE internet_subscription_claims SET status=?,reviewed_at=CURRENT_TIMESTAMP WHERE id=?',[$status,$id]);$db->commit();answer(['ok'=>true]);
 }
 if ($action==='create_ticket') {
  limitRate($db,'tickets',10,$userId);$subject=field($body,'subject',640);$photos=uploadedPhotos($_FILES);$text=messageText($body,$photos);
  if (!$subject || preg_match_all('/./us',$subject)>160)problem('Укажите тему до 160 символов.');
  $db->beginTransaction();query($db,'SELECT id FROM internet_users WHERE id=? FOR UPDATE',[$userId]);if ((int)query($db,"SELECT COUNT(*) FROM internet_tickets WHERE user_id=? AND status IN ('open','waiting')",[$userId])->fetchColumn()>=10)problem('У вас уже 10 открытых обращений. Дополните существующее.');
  query($db,'INSERT INTO internet_tickets(user_id,subject) VALUES (?,?)',[$userId,$subject]);$id=(int)$db->lastInsertId();query($db,'INSERT INTO internet_messages(ticket_id,sender_id,is_admin,body) VALUES (?,?,0,?)',[$id,$userId,$text]);$messageId=(int)$db->lastInsertId();saveMessagePhotos($db,$messageId,$userId,$photos);
  notifyAdmin($db,$config,'message:'.$messageId,'support',$userId,'Новое обращение #'.$id,contactText($user)."\nТема: $subject\n\n$text".photoMailNote($photos));$db->commit();answer(['id'=>$id],201);
 }
 if ($action==='reply_ticket') {
  limitRate($db,'messages',50,$userId);$id=(int)($body['id'] ?? 0);
  $db->beginTransaction();$ticket=query($db,'SELECT * FROM internet_tickets WHERE id=? FOR UPDATE',[$id])->fetch();if (!$ticket || (!$admin && (int)$ticket['user_id']!==$userId))problem('Обращение не найдено.',404);
  $photos=uploadedPhotos($_FILES);$text=messageText($body,$photos);$asAdmin=$admin && ((int)$ticket['user_id']!==$userId || filter_var($body['as_admin'] ?? false,FILTER_VALIDATE_BOOLEAN));
  query($db,'INSERT INTO internet_messages(ticket_id,sender_id,is_admin,body) VALUES (?,?,?,?)',[$id,$userId,(int)$asAdmin,$text]);$messageId=(int)$db->lastInsertId();saveMessagePhotos($db,$messageId,$userId,$photos);query($db,'UPDATE internet_tickets SET status=?,updated_at=CURRENT_TIMESTAMP WHERE id=?',[$asAdmin?'waiting':'open',$id]);
  if($asAdmin)notifyClient($db,(int)$ticket['user_id'],'ticket-reply:'.$messageId,'ticket_reply','Поддержка ответила','На обращение «'.$ticket['subject'].'» поступил ответ.','ticket',$id);
  if (!$asAdmin)notifyAdmin($db,$config,'message:'.$messageId,'support',$userId,'Сообщение в обращении #'.$id,contactText($user)."\nТема: ".$ticket['subject']."\n\n$text".photoMailNote($photos));
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='read_ticket') {
  $id=(int)($body['id'] ?? 0);$ticket=query($db,'SELECT * FROM internet_tickets WHERE id=?',[$id])->fetch();if (!$ticket || (!$admin && (int)$ticket['user_id']!==$userId))problem('Обращение не найдено.',404);
  $asAdmin=$admin && ((int)$ticket['user_id']!==$userId || filter_var($body['as_admin'] ?? false,FILTER_VALIDATE_BOOLEAN));
  $messages=query($db,'SELECT m.id,m.is_admin,m.body,m.created_at,u.first_name,u.last_name FROM internet_messages m JOIN internet_users u ON u.id=m.sender_id WHERE m.ticket_id=? ORDER BY m.id DESC LIMIT 200',[$id])->fetchAll();$last=$messages?(int)$messages[0]['id']:0;
  if ($asAdmin)query($db,"UPDATE internet_admin_events e JOIN internet_messages m ON e.event_key=CONCAT('message:',m.id) SET e.read_at=COALESCE(e.read_at,CURRENT_TIMESTAMP) WHERE m.ticket_id=? AND e.type='support'",[$id]);
  if($asAdmin&&clientEventsReady($db))query($db,"UPDATE internet_client_notifications SET read_at=COALESCE(read_at,CURRENT_TIMESTAMP) WHERE user_id=? AND target='admin-ticket' AND target_id=?",[$userId,$id]);
  if(!$asAdmin&&clientEventsReady($db))query($db,"UPDATE internet_client_notifications SET read_at=COALESCE(read_at,CURRENT_TIMESTAMP) WHERE user_id=? AND target='ticket' AND target_id=?",[$userId,$id]);
  $column=$asAdmin?'admin_seen_message_id':'user_seen_message_id';query($db,"UPDATE internet_tickets SET $column=GREATEST($column,?) WHERE id=?",[$last,$id]);$ticket['status_label']=ticketStateLabel($ticket['status']);answer(['ticket'=>$ticket,'messages'=>withMessagePhotos($db,array_reverse($messages))]);
 }
 if ($action==='set_ticket_status') {
  $id=(int)($body['id'] ?? 0);$status=field($body,'status',20);if(!in_array($status,['open','closed'],true))problem('Недопустимый статус.');
  $db->beginTransaction();$ticket=query($db,'SELECT id,user_id,subject,status FROM internet_tickets WHERE id=? FOR UPDATE',[$id])->fetch();if(!$ticket)problem('Обращение не найдено.',404);
  if($ticket['status']!==$status){query($db,'UPDATE internet_tickets SET status=?,updated_at=CURRENT_TIMESTAMP WHERE id=?',[$status,$id]);notifyClient($db,(int)$ticket['user_id'],'ticket-status:'.bin2hex(random_bytes(12)),'ticket_status','Статус обращения изменён','Обращение «'.$ticket['subject'].'»: '.ticketStateLabel($status).'.','ticket',$id);}
  $db->commit();answer(['ok'=>true]);
 }
 if ($action==='tickets' || $action==='admin_tickets') {
  $asAdmin=$action==='admin_tickets';$column=$asAdmin?'admin_seen_message_id':'user_seen_message_id';$role=$asAdmin?0:1;$where=$asAdmin?'':' WHERE t.user_id=?';$args=$asAdmin?[]:[$userId];
  $scope=$_GET['scope'] ?? 'active';if($asAdmin){if(!in_array($scope,['active','archive'],true))problem('Проверьте раздел обращений.');$where=$scope==='archive'?" WHERE t.status='closed'":" WHERE t.status<>'closed'";}
  $offset=max(0,min(10000,(int)($_GET['page'] ?? 0)))*100;
  $total=(int)query($db,'SELECT COUNT(*) FROM internet_tickets t'.$where,$args)->fetchColumn();
  $items=query($db,"SELECT t.*,u.first_name,u.last_name,u.email,u.telegram,(SELECT COUNT(*) FROM internet_messages m WHERE m.ticket_id=t.id AND m.is_admin=$role AND m.id>t.$column) AS unread FROM internet_tickets t JOIN internet_users u ON u.id=t.user_id $where ORDER BY t.updated_at DESC,t.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();
  foreach($items as &$item)$item['status_label']=ticketStateLabel($item['status']);unset($item);answer(['tickets'=>$items,'total'=>$total]);
 }
 if ($action==='summary') {
  $unread=(int)query($db,'SELECT COUNT(*) FROM internet_messages m JOIN internet_tickets t ON t.id=m.ticket_id WHERE t.user_id=? AND m.is_admin=1 AND m.id>t.user_seen_message_id',[$userId])->fetchColumn();
  $issued=(int)query($db,'SELECT COUNT(*) FROM internet_subscriptions WHERE user_id=?',[$userId])->fetchColumn();answer(['unread_support'=>$unread,'subscriptions'=>$issued,'keenetic_enabled'=>keeneticEnabled($db,$userId)]+clientNotificationSummary($db,$userId,$admin));
 }
 if ($action==='admin_summary') {
  $counts=['user'=>0,'payment'=>0,'support'=>0,'subscription'=>0];foreach(query($db,'SELECT type,COUNT(*) AS n FROM internet_admin_events WHERE read_at IS NULL GROUP BY type')->fetchAll() as $row)$counts[$row['type']]=(int)$row['n'];
  $mail=query($db,"SELECT SUM(status IN ('pending','retry','sending')) AS pending,SUM(status='failed') AS failed,SUM(status='sent') AS sent FROM internet_mail_outbox")->fetch();
  answer(['unread'=>$counts,'support_archive'=>(int)query($db,"SELECT COUNT(*) FROM internet_tickets WHERE status='closed'")->fetchColumn(),'mail'=>['ready'=>mailReady($config),'recipient'=>$config['admin_email'] ?? 'sania.iwannikow@yandex.ru','pending'=>(int)$mail['pending'],'failed'=>(int)$mail['failed'],'sent'=>(int)$mail['sent']]]);
 }
 if ($action==='admin_events') {
  $type=$_GET['type'] ?? '';if (!in_array($type,['user','payment','support','subscription','system'],true))problem('Неизвестный раздел.');$offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $items=query($db,"SELECT e.*,u.first_name,u.last_name,u.email,u.telegram FROM internet_admin_events e JOIN internet_users u ON u.id=e.user_id WHERE e.type=? ORDER BY e.id DESC LIMIT 100 OFFSET $offset",[$type])->fetchAll();answer(['events'=>$items]);
 }
 if ($action==='read_event') {query($db,'UPDATE internet_admin_events SET read_at=COALESCE(read_at,CURRENT_TIMESTAMP) WHERE id=?',[(int)($body['id'] ?? 0)]);answer(['ok'=>true]);}
 if ($action==='admin_users') {
  $offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $visible=authTablesReady($db)?' WHERE NOT EXISTS (SELECT 1 FROM internet_auth_state a WHERE a.user_id=u.id AND a.deleted_at IS NOT NULL)':'';
  $rows=query($db,"SELECT u.id,u.first_name,u.last_name,u.email,u.telegram,u.created_at,e.id AS event_id,e.read_at AS event_read_at FROM internet_users u LEFT JOIN internet_admin_events e ON e.event_key=CONCAT('user:',u.id)$visible ORDER BY u.id DESC LIMIT 100 OFFSET $offset")->fetchAll();
  foreach($rows as &$row){$row['claims']=query($db,'SELECT name,status FROM internet_subscription_claims WHERE user_id=? ORDER BY id',[$row['id']])->fetchAll();$row['subscriptions']=query($db,'SELECT name FROM internet_subscriptions WHERE user_id=? ORDER BY name',[$row['id']])->fetchAll();}unset($row);answer(['users'=>$rows]);
 }
 if ($action==='admin_orders') {
  $offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $rows=query($db,"SELECT o.*,u.first_name,u.last_name,u.email,u.telegram,e.id AS event_id,e.read_at AS event_read_at FROM internet_orders o JOIN internet_users u ON u.id=o.user_id LEFT JOIN internet_admin_events e ON e.order_id=o.id AND e.type='payment' ORDER BY o.created_at DESC,o.id DESC LIMIT 100 OFFSET $offset")->fetchAll();answer(['orders'=>withOrderSubscriptions($db,$rows,true)]);
 }
 if ($action==='admin_subscriptions') {
  $search=isset($_GET['q'])?field(['q'=>$_GET['q']],'q',100):'';$offset=max(0,min((int)($_GET['page'] ?? 0),10000))*100;
  $where=$search!==''?' WHERE s.name LIKE ?':'';$args=$search!==''?['%'.$search.'%']:[];
  $total=(int)query($db,'SELECT COUNT(*) FROM internet_subscriptions s'.$where,$args)->fetchColumn();
  $rows=query($db,"SELECT s.*,u.first_name,u.last_name,u.email FROM internet_subscriptions s LEFT JOIN internet_users u ON u.id=s.user_id $where ORDER BY s.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();
  $claims=query($db,"SELECT c.*,u.first_name,u.last_name,u.email,u.telegram FROM internet_subscription_claims c JOIN internet_users u ON u.id=c.user_id WHERE c.status IN ('pending','revoked') ORDER BY c.id LIMIT 200")->fetchAll();answer(['subscriptions'=>withSubscriptionTerms($db,$rows,true),'claims'=>$claims,'total'=>$total]);
 }
 if ($action==='admin_mail_diagnostics')answer(mailDiagnostics($db,$config));
 if ($action==='mail_test') {
  limitRate($db,'mailtest',3,$userId);$db->beginTransaction();notifyAdmin($db,$config,'mail-test:'.bin2hex(random_bytes(8)),'system',$userId,'Проверка почтовых уведомлений','Письма из кабинета МосКомпРемонт отправляются на этот адрес.');$db->commit();answer(['ok'=>true,'ready'=>mailReady($config)]);
 }
 if ($action==='mail_retry') {
  limitRate($db,'mailretry',10,$userId);query($db,"UPDATE internet_mail_outbox SET status='retry',attempts=0,next_attempt_at=CURRENT_TIMESTAMP WHERE status='failed'");query($db,"UPDATE internet_mail_outbox SET next_attempt_at=CURRENT_TIMESTAMP WHERE status='retry'");answer(sendPendingMail($db,$config,1));
 }
 problem('Действие не найдено.',404);
} catch(AccountError $e) {if ($db instanceof PDO && $db->inTransaction())$db->rollBack();answer(['error'=>$e->getMessage()],$e->httpStatus);}
catch(Throwable $e) {
 if ($db instanceof PDO && $db->inTransaction())$db->rollBack();error_log('Internet account: '.$e->getMessage());
 $missing=$e instanceof PDOException && $e->getCode()==='42S02';answer(['error'=>$missing?'Нужно обновить базу: импортируйте private/schema.sql через phpMyAdmin в ту же базу, которая указана в config.php.':'Сервис временно недоступен. Попробуйте позже.'],503);
}
