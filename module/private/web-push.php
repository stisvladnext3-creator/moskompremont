<?php
declare(strict_types=1);
function pushSettings(PDO $db): ?array {
 try {$s=query($db,'SELECT * FROM internet_push_settings WHERE id=1')->fetch();return $s ?: null;}
 catch(PDOException $e){if($e->getCode()==='42S02')return null;throw $e;}
}
function pushRequireReady(PDO $db): void {if(!pushSettings($db))problem('Импортируйте private/migrations/009-client-updates.sql для push-уведомлений.',503);}
function pushRuntimeIssues(): array {
 $issues=[];if(!extension_loaded('openssl'))$issues[]='Включите PHP OpenSSL.';if(!function_exists('curl_init'))$issues[]='Включите расширение PHP curl.';if(!extension_loaded('mbstring'))$issues[]='Включите расширение PHP mbstring.';
 if(!is_file(__DIR__.'/vendor/WebPush/vendor/autoload.php'))$issues[]='Загрузите папку private/vendor/WebPush из обновления.';return $issues;
}
function pushLoadLibrary(): bool {if(pushRuntimeIssues())return false;require_once __DIR__.'/vendor/WebPush/vendor/autoload.php';return class_exists(\Minishlink\WebPush\WebPush::class);}
function clientPushSettings(PDO $db): array {
 $s=pushSettings($db);$ready=$s&&(bool)$s['enabled']&&!empty($s['public_key'])&&!empty($s['private_key'])&&!pushRuntimeIssues();
 return ['ready'=>(bool)$ready,'public_key'=>$ready?$s['public_key']:null];
}
function adminPushSettings(PDO $db,array $config): array {
 pushRequireReady($db);$s=pushSettings($db);$counts=[];foreach(query($db,'SELECT status,COUNT(*) AS n FROM internet_push_outbox GROUP BY status')->fetchAll() as $row)$counts[$row['status']]=(int)$row['n'];
 $messages=query($db,'SELECT id,status,attempts,last_error,created_at,sent_at FROM internet_push_outbox ORDER BY id DESC LIMIT 20')->fetchAll();
 return ['enabled'=>(bool)$s['enabled'],'keys_ready'=>!empty($s['public_key'])&&!empty($s['private_key']),'ready'=>clientPushSettings($db)['ready'],'issues'=>pushRuntimeIssues(),'checked_at'=>$s['checked_at'],'counts'=>$counts,'messages'=>$messages,'devices'=>(int)query($db,'SELECT COUNT(*) FROM internet_push_devices')->fetchColumn()];
}
function setupClientPush(PDO $db,array $config): array {
 pushRequireReady($db);if(!pushLoadLibrary())problem(implode(' ',pushRuntimeIssues()),503);
 $db->beginTransaction();$s=query($db,'SELECT public_key,private_key FROM internet_push_settings WHERE id=1 FOR UPDATE')->fetch();
 if(!$s['public_key']||!$s['private_key']){$keys=\Minishlink\WebPush\VAPID::createVapidKeys();$email=$config['admin_email'] ?? ''; $subject=filter_var($email,FILTER_VALIDATE_EMAIL)?'mailto:'.$email:rtrim($config['origin'],'/');query($db,'UPDATE internet_push_settings SET public_key=?,private_key=?,subject=? WHERE id=1',[$keys['publicKey'],$keys['privateKey'],$subject]);}
 $db->commit();return adminPushSettings($db,$config);
}
function pushEndpoint(mixed $value): string {
 if(!is_string($value)||strlen($value)>4096||!filter_var($value,FILTER_VALIDATE_URL))problem('Некорректная подписка push-уведомлений.');
 $u=parse_url($value);$host=strtolower($u['host'] ?? '');
 $trusted=in_array($host,['fcm.googleapis.com','android.googleapis.com','push.services.mozilla.com','push.apple.com','notify.windows.com'],true);
 foreach(['.push.services.mozilla.com','.push.apple.com','.notify.windows.com'] as $suffix)if(str_ends_with($host,$suffix))$trusted=true;
 if(!$trusted||($u['scheme'] ?? '')!=='https'||isset($u['user'])||isset($u['pass'])||isset($u['fragment'])||isset($u['port'])&&(int)$u['port']!==443)problem('Этот сервис push-уведомлений не поддерживается.');return 'https://'.$host.($u['path'] ?? '/').(isset($u['query'])?'?'.$u['query']:'');
}
function pushKey(mixed $value,int $bytes): string {
 if(!is_string($value)||!preg_match('/\A[A-Za-z0-9_-]+={0,2}\z/D',$value))problem('Некорректные ключи push-уведомлений.');
 $plain=base64_decode(strtr($value,'-_','+/'),true);if($plain===false||strlen($plain)!==$bytes||$bytes===65&&$plain[0]!=="\x04")problem('Некорректные ключи push-уведомлений.');if($bytes===65){$der=hex2bin('3059301306072a8648ce3d020106082a8648ce3d030107034200').$plain;$pem="-----BEGIN PUBLIC KEY-----\n".chunk_split(base64_encode($der),64,"\n")."-----END PUBLIC KEY-----\n";if(!@openssl_pkey_get_public($pem))problem('Некорректный публичный ключ устройства.');}return rtrim($value,'=');
}
function pushDeviceStatus(PDO $db,int $userId,array $body): array {
 $endpoint=pushEndpoint($body['endpoint'] ?? null);$s=pushSettings($db);$registered=$s&&(bool)query($db,'SELECT id FROM internet_push_devices WHERE endpoint_hash=? AND user_id=?',[hash('sha256',$endpoint),$userId])->fetchColumn();return ['registered'=>(bool)$registered];
}
function registerPushDevice(PDO $db,int $userId,array $body): array {
 pushRequireReady($db);if(!clientPushSettings($db)['ready'])problem('Push-уведомления пока не включены администратором.',503);
 $endpoint=pushEndpoint($body['endpoint'] ?? null);$keys=$body['keys'] ?? [];if(!is_array($keys))problem('Некорректные данные устройства.');$public=pushKey($keys['p256dh'] ?? null,65);$auth=pushKey($keys['auth'] ?? null,16);$hash=hash('sha256',$endpoint);
 $db->beginTransaction();activeAccount($db,$userId,true);$existing=query($db,'SELECT id,user_id,p256dh,auth FROM internet_push_devices WHERE endpoint_hash=? FOR UPDATE',[$hash])->fetch();
 if($existing){if(!hash_equals($existing['p256dh'],$public)||!hash_equals($existing['auth'],$auth))problem('Подписка устройства изменилась. Отключите и включите push на этом устройстве.',409);
  if((int)$existing['user_id']!==$userId){if((int)query($db,'SELECT COUNT(*) FROM internet_push_devices WHERE user_id=?',[$userId])->fetchColumn()>=10)problem('Можно подключить до 10 устройств. Отключите одно из прежних.');query($db,'DELETE FROM internet_push_outbox WHERE device_id=?',[$existing['id']]);}
  query($db,'UPDATE internet_push_devices SET user_id=?,updated_at=CURRENT_TIMESTAMP WHERE id=?',[$userId,$existing['id']]);
 }else{query($db,'SELECT id FROM internet_users WHERE id=? FOR UPDATE',[$userId]);if((int)query($db,'SELECT COUNT(*) FROM internet_push_devices WHERE user_id=?',[$userId])->fetchColumn()>=10)problem('Можно подключить до 10 устройств. Отключите одно из прежних.');query($db,'INSERT INTO internet_push_devices(user_id,endpoint,endpoint_hash,p256dh,auth) VALUES (?,?,?,?,?)',[$userId,$endpoint,$hash,$public,$auth]);}
 $db->commit();return ['ok'=>true,'registered'=>true];
}
function removePushDevice(PDO $db,int $userId,array $body): array {
 $endpoint=pushEndpoint($body['endpoint'] ?? null);if(pushSettings($db))query($db,'DELETE FROM internet_push_devices WHERE endpoint_hash=? AND user_id=?',[hash('sha256',$endpoint),$userId]);return ['ok'=>true];
}
function queueClientPush(PDO $db,int $notificationId,int $userId): void {
 $s=pushSettings($db);if(!$s||!$s['enabled']||!$s['public_key']||!$s['private_key'])return;
 $q=query($db,'INSERT IGNORE INTO internet_push_outbox(notification_id,device_id) SELECT ?,id FROM internet_push_devices WHERE user_id=?',[$notificationId,$userId]);if($q->rowCount())$GLOBALS['internet_push_queued']=true;
}
function pushPayload(array $config,int $notificationId,string $type=''): string {
 $path=trim((string)($config['app_path'] ?? ''),'/');if($path!==''&&!preg_match('~\A[a-zA-Z0-9_/-]+\z~D',$path))$path='';
 $title=match($type){'admin_user'=>'Новый клиент','admin_payment'=>'Уведомление об оплате','admin_support'=>'Новое сообщение клиента','admin_system'=>'Почтовое уведомление','ticket_reply'=>'Ответ поддержки','subscription_updated'=>'Подписки обновлены','router_key_updated'=>'Ключи роутера обновлены',default=>'МосКомпРемонт'};
 return json_encode(['title'=>$title,'body'=>'В вашем личном кабинете есть новое уведомление.','url'=>($path!==''?'/'.$path:'').'/account.html?notification='.$notificationId.'#notifications','tag'=>'mkr-notification-'.$notificationId],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES|JSON_THROW_ON_ERROR);
}
function sendClientPush(PDO $db,array $config,int $limit=20,?object $sender=null): array {
 $s=pushSettings($db);if(!$s||!$s['enabled']||!$s['public_key']||!$s['private_key']||!pushLoadLibrary())return ['ready'=>false,'sent'=>0,'failed'=>0];
 if($db->inTransaction())return ['ready'=>true,'sent'=>0,'failed'=>0];
 query($db,'UPDATE internet_push_settings SET checked_at=CURRENT_TIMESTAMP WHERE id=1');
 query($db,"UPDATE internet_push_outbox p JOIN internet_client_notifications n ON n.id=p.notification_id JOIN internet_push_devices d ON d.id=p.device_id SET p.status='cancelled',p.lease_until=NULL WHERE p.status IN ('pending','retry','sending') AND (n.read_at IS NOT NULL OR n.user_id<>d.user_id OR p.created_at<DATE_SUB(CURRENT_TIMESTAMP,INTERVAL 1 DAY))");
 query($db,"UPDATE internet_push_outbox SET status='failed',lease_until=NULL,last_error='PUSH_RETRY_LIMIT' WHERE status='sending' AND lease_until<CURRENT_TIMESTAMP AND attempts>=6");
 $admins=array_values(array_filter(array_unique(array_map('intval',$config['admin_user_ids'] ?? [])),static fn($v)=>$v>0));$adminFilter=$admins?implode(',',$admins):'0';
 query($db,"UPDATE internet_push_outbox p JOIN internet_client_notifications n ON n.id=p.notification_id SET p.status='cancelled',p.lease_until=NULL WHERE p.status IN ('pending','retry','sending') AND n.type LIKE 'admin\\_%' AND n.user_id NOT IN ($adminFilter)");
 $limit=max(1,min(50,$limit));$db->beginTransaction();
 $items=query($db,"SELECT p.id,p.notification_id,p.attempts,n.type,d.endpoint,d.p256dh,d.auth,d.id AS device_id,d.user_id FROM internet_push_outbox p JOIN internet_push_devices d ON d.id=p.device_id JOIN internet_client_notifications n ON n.id=p.notification_id AND n.user_id=d.user_id WHERE ((p.status IN ('pending','retry') AND p.next_attempt_at<=CURRENT_TIMESTAMP) OR (p.status='sending' AND p.lease_until<CURRENT_TIMESTAMP)) AND p.attempts<6 AND n.read_at IS NULL AND NOT EXISTS (SELECT 1 FROM internet_push_outbox earlier WHERE earlier.device_id=p.device_id AND earlier.id<p.id AND earlier.status IN ('pending','retry','sending')) ORDER BY p.id LIMIT $limit FOR UPDATE")->fetchAll();
 foreach($items as $item)query($db,"UPDATE internet_push_outbox SET status='sending',attempts=attempts+1,lease_until=DATE_ADD(CURRENT_TIMESTAMP,INTERVAL 2 MINUTE) WHERE id=?",[$item['id']]);$db->commit();
 if(!$items)return ['ready'=>true,'sent'=>0,'failed'=>0];
 $sender ??=new \Minishlink\WebPush\WebPush(['VAPID'=>['subject'=>$s['subject'],'publicKey'=>$s['public_key'],'privateKey'=>$s['private_key']]],['TTL'=>3600],8,['allow_redirects'=>false,'verify'=>true,'connect_timeout'=>5]);
 $sender->setReuseVAPIDHeaders(true);$byEndpoint=[];$sent=0;$failed=0;
 foreach($items as $item){
  try{pushEndpoint($item['endpoint']);$subscription=\Minishlink\WebPush\Subscription::create(['endpoint'=>$item['endpoint'],'publicKey'=>$item['p256dh'],'authToken'=>$item['auth'],'contentEncoding'=>'aes128gcm']);$sender->queueNotification($subscription,pushPayload($config,(int)$item['notification_id'],$item['type']),['TTL'=>3600]);$byEndpoint[$item['endpoint']][]=$item;}
  catch(Throwable $e){pushJobFailed($db,$item,'PUSH_SUBSCRIPTION');$failed++;}
 }
 try{
  foreach($sender->flush() as $report){$endpoint=$report->getEndpoint();if(empty($byEndpoint[$endpoint]))continue;$item=array_shift($byEndpoint[$endpoint]);$code=$report->getResponse()?->getStatusCode();
   if($report->isSuccess()&&$code>=200&&$code<300){query($db,"UPDATE internet_push_outbox SET status='sent',sent_at=CURRENT_TIMESTAMP,lease_until=NULL,last_error=NULL WHERE id=?",[$item['id']]);$sent++;}
   elseif($report->isSubscriptionExpired()){query($db,'DELETE FROM internet_push_devices WHERE id=? AND user_id=?',[$item['device_id'],$item['user_id']]);$failed++;}
   else {$code=$report->getResponse()?->getStatusCode();pushJobFailed($db,$item,$code?'PUSH_HTTP_'.(int)$code:'PUSH_NETWORK');$failed++;}
  }
 }catch(Throwable $e){foreach($byEndpoint as $left)foreach($left as $item){pushJobFailed($db,$item,'PUSH_DELIVERY');$failed++;}}
 return ['ready'=>true,'sent'=>$sent,'failed'=>$failed];
}
function pushJobFailed(PDO $db,array $item,string $error): void {
 $attempts=(int)$item['attempts']+1;$state=$attempts>=6?'failed':'retry';$delay=min(3600,30*(2**min($attempts-1,7)));
 query($db,'UPDATE internet_push_outbox SET status=?,next_attempt_at=FROM_UNIXTIME(?),lease_until=NULL,last_error=? WHERE id=? AND status=\'sending\'',[$state,time()+$delay,$error,$item['id']]);
}
