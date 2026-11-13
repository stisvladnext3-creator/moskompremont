<?php
declare(strict_types=1);
function authTablesReady(PDO $db): bool {
 static $cache=[];$key=spl_object_id($db);if(array_key_exists($key,$cache))return $cache[$key];
 try{query($db,'SELECT user_id FROM internet_auth_state LIMIT 0');query($db,'SELECT selector FROM internet_remember_tokens LIMIT 0');return $cache[$key]=true;}
 catch(PDOException $e){if($e->getCode()==='42S02')return $cache[$key]=false;throw $e;}
}
function authState(PDO $db,int $id): array {
 if(!authTablesReady($db))return ['version'=>1,'deleted_at'=>null];return query($db,'SELECT version,deleted_at FROM internet_auth_state WHERE user_id=?',[$id])->fetch() ?: ['version'=>1,'deleted_at'=>null];
}
function rememberCookie(array $config,string $value,int $expires): void {setcookie('mkr_remember',$value,['expires'=>$expires,'path'=>'/','secure'=>$config['secure_cookies'] ?? true,'httponly'=>true,'samesite'=>'Lax']);}
function rememberParts(): ?array {$raw=$_COOKIE['mkr_remember'] ?? '';return is_string($raw)&&preg_match('/\A([a-f0-9]{24})\.([a-f0-9]{64})\z/D',$raw,$m)?[$m[1],$m[2]]:null;}
function forgetRemember(PDO $db,array $config): void {$parts=rememberParts();if($parts&&authTablesReady($db))query($db,'DELETE FROM internet_remember_tokens WHERE selector=? AND (validator_hash=? OR previous_hash=?)',[$parts[0],hash('sha256',$parts[1]),hash('sha256',$parts[1])]);rememberCookie($config,'',time()-3600);unset($_COOKIE['mkr_remember']);}
function rawAccount(PDO $db,int $id,bool $lock=false): array|false {return query($db,'SELECT id,first_name,last_name,email,telegram,password_hash,created_at FROM internet_users WHERE id=?'.($lock?' FOR UPDATE':''),[$id])->fetch();}
function activeAccount(PDO $db,int $id,bool $lock=false): array {
 $user=rawAccount($db,$id,$lock);if(!$user||authState($db,$id)['deleted_at']!==null)problem('Аккаунт не найден или удалён.',404);return $user;
}
function establishAccountSession(PDO $db,array $config,array $user,bool $remember): void {
 session_regenerate_id(true);$_SESSION['user_id']=(int)$user['id'];$_SESSION['auth_version']=(int)authState($db,(int)$user['id'])['version'];$_SESSION['password_fingerprint']=hash('sha256',$user['password_hash']);$_SESSION['last_seen']=time();$_SESSION['auth_expires']=time()+($remember?30*86400:12*3600);$_SESSION['csrf']=bin2hex(random_bytes(32));
 if($remember&&authTablesReady($db)){
  forgetRemember($db,$config);$selector=bin2hex(random_bytes(12));$validator=bin2hex(random_bytes(32));$expires=time()+30*86400;
  query($db,'INSERT INTO internet_remember_tokens(selector,user_id,validator_hash,password_fingerprint,expires_at) VALUES (?,?,?,?,FROM_UNIXTIME(?))',[$selector,$user['id'],hash('sha256',$validator),$_SESSION['password_fingerprint'],$expires]);rememberCookie($config,$selector.'.'.$validator,$expires);
  query($db,'DELETE FROM internet_remember_tokens WHERE expires_at<CURRENT_TIMESTAMP');
 }else forgetRemember($db,$config);
}
function accountSessionUser(PDO $db,array $config): array|false {
 $id=(int)($_SESSION['user_id'] ?? 0);
 if($id){$user=rawAccount($db,$id);$state=authState($db,$id);$fingerprint=$user?hash('sha256',$user['password_hash']):'';
  if($user&&isset($_SESSION['auth_version'],$_SESSION['password_fingerprint'],$_SESSION['auth_expires'])&&$state['deleted_at']===null&&(int)$_SESSION['auth_version']===(int)$state['version']&&(!isset($_SESSION['password_fingerprint'])||hash_equals($_SESSION['password_fingerprint'],$fingerprint))&&time()<=(int)($_SESSION['auth_expires'] ?? (time()+1))){$_SESSION['auth_expires'] ??=time()+12*3600;$_SESSION['password_fingerprint']=$fingerprint;$_SESSION['last_seen']=time();return $user;}
  $_SESSION=[];session_regenerate_id(true);
 }
 $parts=rememberParts();if(!$parts||!authTablesReady($db))return false;
 $db->beginTransaction();$token=query($db,'SELECT * FROM internet_remember_tokens WHERE selector=? FOR UPDATE',[$parts[0]])->fetch();
 if(!$token||strtotime($token['expires_at'].' UTC')<=time()){$db->commit();rememberCookie($config,'',time()-3600);return false;}
 $hash=hash('sha256',$parts[1]);$current=hash_equals($token['validator_hash'],$hash);$previous=$token['previous_hash']&&$token['previous_until']&&strtotime($token['previous_until'].' UTC')>=time()&&hash_equals($token['previous_hash'],$hash);
 $user=rawAccount($db,(int)$token['user_id']);$state=authState($db,(int)$token['user_id']);
 if((!$current&&!$previous)||!$user||$state['deleted_at']!==null||!hash_equals($token['password_fingerprint'],hash('sha256',$user['password_hash']))){query($db,'DELETE FROM internet_remember_tokens WHERE selector=?',[$parts[0]]);$db->commit();rememberCookie($config,'',time()-3600);return false;}
 if($current){$validator=bin2hex(random_bytes(32));$expires=time()+30*86400;query($db,'UPDATE internet_remember_tokens SET previous_hash=validator_hash,previous_until=DATE_ADD(CURRENT_TIMESTAMP,INTERVAL 30 SECOND),validator_hash=?,expires_at=FROM_UNIXTIME(?),last_used_at=CURRENT_TIMESTAMP WHERE selector=?',[hash('sha256',$validator),$expires,$parts[0]]);rememberCookie($config,$parts[0].'.'.$validator,$expires);}
 $db->commit();session_regenerate_id(true);$_SESSION=['user_id'=>(int)$user['id'],'auth_version'=>(int)$state['version'],'password_fingerprint'=>$token['password_fingerprint'],'last_seen'=>time(),'auth_expires'=>time()+30*86400,'csrf'=>bin2hex(random_bytes(32))];return $user;
}
function adminAccountRows(PDO $db,array $params): array {
 if(!authTablesReady($db))problem('Импортируйте private/migrations/009-client-updates.sql для управления аккаунтами.',503);
 $q=isset($params['q'])?field(['q'=>$params['q']],'q',254):'';$where=' WHERE NOT EXISTS (SELECT 1 FROM internet_auth_state a WHERE a.user_id=u.id AND a.deleted_at IS NOT NULL)';$args=[];
 if($q!==''){$where.=" AND (u.email LIKE ? OR u.first_name LIKE ? OR u.last_name LIKE ? OR u.telegram LIKE ? OR u.id=?)";$like='%'.$q.'%';$args=[$like,$like,$like,$like,preg_match('/\A[0-9]+\z/D',$q)?(int)$q:0];}
 $offset=max(0,min(10000,(int)($params['page'] ?? 0)))*100;$total=(int)query($db,'SELECT COUNT(*) FROM internet_users u'.$where,$args)->fetchColumn();
 $rows=query($db,'SELECT u.id,u.first_name,u.last_name,u.email,u.telegram,u.created_at FROM internet_users u'.$where." ORDER BY u.id DESC LIMIT 100 OFFSET $offset",$args)->fetchAll();return ['accounts'=>$rows,'total'=>$total];
}
function resetAccountPassword(PDO $db,array $body,int $adminId): array {
 if(!authTablesReady($db))problem('Импортируйте private/migrations/009-client-updates.sql.',503);$id=$body['id'] ?? null;$password=$body['password'] ?? null;
 if(!is_int($id)||$id<1||!is_string($password)||!preg_match('//u',$password)||preg_match_all('/./us',$password)<6||strlen($password)>72)problem('Выберите аккаунт и пароль: от 6 символов, не более 72 байт.');
 $hash=password_hash($password,PASSWORD_DEFAULT);$db->beginTransaction();activeAccount($db,$id,true);query($db,'UPDATE internet_users SET password_hash=? WHERE id=?',[$hash,$id]);query($db,'INSERT INTO internet_auth_state(user_id,version) VALUES (?,2) ON DUPLICATE KEY UPDATE version=version+1',[$id]);query($db,'DELETE FROM internet_remember_tokens WHERE user_id=?',[$id]);if(pushSettings($db))query($db,'DELETE FROM internet_push_devices WHERE user_id=?',[$id]);$db->commit();return ['ok'=>true,'logged_out'=>$id===$adminId];
}
function deleteAccount(PDO $db,array $body,int $adminId): array {
 if(!authTablesReady($db))problem('Импортируйте private/migrations/009-client-updates.sql.',503);$id=$body['id'] ?? null;$mode=$body['resources'] ?? null;
 if(!is_int($id)||$id<1||!in_array($mode,['release','delete'],true))problem('Выберите аккаунт и способ обработки его подписок/ключей.');if($id===$adminId)problem('Свой текущий аккаунт удалить нельзя.',409);
 $db->beginTransaction();activeAccount($db,$id,true);$subs=query($db,'SELECT id,name,user_id FROM internet_subscriptions WHERE user_id=? FOR UPDATE',[$id])->fetchAll();$keys=query($db,'SELECT id,label FROM internet_router_keys WHERE user_id=? FOR UPDATE',[$id])->fetchAll();
 foreach($subs as $sub){if($mode==='delete')deleteSubscriptionData($db,$sub,$adminId);else{query($db,'INSERT INTO internet_subscription_revocations(subscription_id,user_id,revoked_by) VALUES (?,?,?) ON DUPLICATE KEY UPDATE revoked_at=CURRENT_TIMESTAMP,revoked_by=VALUES(revoked_by)',[$sub['id'],$id,$adminId]);query($db,'UPDATE internet_subscriptions SET user_id=NULL,assigned_at=NULL WHERE id=?',[$sub['id']]);}}
 foreach($keys as $key){if($mode==='delete'){query($db,'DELETE FROM internet_router_keys WHERE id=?',[$key['id']]);resourceRemoved($db,'router_key',(int)$key['id']);}else query($db,'UPDATE internet_router_keys SET user_id=NULL,assigned_at=NULL WHERE id=?',[$key['id']]);}
 query($db,'DELETE FROM internet_subscription_claims WHERE user_id=?',[$id]);if(function_exists('keeneticReady')&&keeneticReady($db)){query($db,'DELETE FROM internet_keenetic_files WHERE user_id=?',[$id]);query($db,'DELETE FROM internet_keenetic_access WHERE user_id=?',[$id]);}query($db,'DELETE FROM internet_remember_tokens WHERE user_id=?',[$id]);if(pushSettings($db))query($db,'DELETE FROM internet_push_devices WHERE user_id=?',[$id]);if(clientEventsReady($db))query($db,'DELETE FROM internet_client_notifications WHERE user_id=?',[$id]);
 query($db,'INSERT INTO internet_auth_state(user_id,version,deleted_at) VALUES (?,2,CURRENT_TIMESTAMP) ON DUPLICATE KEY UPDATE version=version+1,deleted_at=CURRENT_TIMESTAMP',[$id]);
 query($db,'UPDATE internet_users SET first_name=?,last_name=?,telegram=?,email=?,password_hash=? WHERE id=?',['Удалённый','аккаунт','—','deleted-'.$id.'-'.bin2hex(random_bytes(8)).'@account.invalid',password_hash(bin2hex(random_bytes(32)),PASSWORD_DEFAULT),$id]);
 $db->commit();return ['ok'=>true,'subscriptions'=>count($subs),'router_keys'=>count($keys)];
}
