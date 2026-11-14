<?php
declare(strict_types=1);

function routerDevicesReady(PDO $db,bool $required=true): bool {
 try {query($db,'SELECT id FROM internet_router_devices LIMIT 0');return true;}
 catch(PDOException $e){if($e->getCode()==='42S02'){if($required)problem('Подключение роутеров пока не настроено. Обратитесь к администратору.',503);return false;}throw $e;}
}
function routerDeviceEndpoint(array $config): string {
 $path=trim((string)($config['app_path'] ?? ''),'/');
 if($path!==''&&!preg_match('~\A[a-zA-Z0-9_/-]+\z~D',$path))throw new RuntimeException('Invalid app path');
 return rtrim($config['origin'],'/').($path!==''?'/'.$path:'').'/api/router-agent.php';
}
function routerDeviceSource(PDO $db,string $type,int $id,int $owner,bool $lock=false): ?array {
 if(!in_array($type,['router_key','subscription'],true)||$id<1)return null;
 if($type==='router_key') {
  $row=query($db,'SELECT id,user_id,label,key_value,key_format FROM internet_router_keys WHERE id=?'.($lock?' FOR UPDATE':''),[$id])->fetch();
  if(!$row||(int)$row['user_id']!==$owner||$row['key_format']!=='connection')return null;
  return ['id'=>(int)$row['id'],'type'=>$type,'label'=>$row['label'],'value'=>trim($row['key_value']),'expired'=>false];
 }
 $row=query($db,'SELECT id,user_id,name,url FROM internet_subscriptions WHERE id=?'.($lock?' FOR UPDATE':''),[$id])->fetch();
 if(!$row||(int)$row['user_id']!==$owner)return null;
 $row=withSubscriptionTerms($db,[$row],true)[0];
 return ['id'=>(int)$row['id'],'type'=>$type,'label'=>$row['name'],'value'=>$row['url'],'expired'=>(bool)$row['is_expired']];
}
function routerDeviceSources(PDO $db,int $owner): array {
 $result=[];
 foreach(query($db,"SELECT id,label FROM internet_router_keys WHERE user_id=? AND key_format='connection' ORDER BY label,id",[$owner])->fetchAll() as $row)$result[]=['id'=>(int)$row['id'],'type'=>'router_key','label'=>$row['label'],'group'=>'Ключи роутеров'];
 foreach(query($db,'SELECT id,name FROM internet_subscriptions WHERE user_id=? ORDER BY name,id',[$owner])->fetchAll() as $row)$result[]=['id'=>(int)$row['id'],'type'=>'subscription','label'=>$row['name'],'group'=>'Подписки'];
 return $result;
}
function routerDeviceRows(PDO $db,int $owner,bool $admin=false): array {
 routerDevicesReady($db);
 $where=$admin?'':' WHERE d.user_id=?';$args=$admin?[]:[$owner];
 $rows=query($db,'SELECT d.id,d.user_id,d.resource_type,d.resource_id,d.label,d.created_at,d.expires_at,d.revoked_at,d.last_seen_at,d.last_state,d.client_version,u.email,u.first_name,u.last_name FROM internet_router_devices d JOIN internet_users u ON u.id=d.user_id'.$where.' ORDER BY d.id DESC LIMIT 200',$args)->fetchAll();
 foreach($rows as &$row){$resource=routerDeviceSource($db,$row['resource_type'],(int)$row['resource_id'],(int)$row['user_id']);$row['resource_label']=$resource['label'] ?? 'Привязка снята';$row['resource_expired']=$resource['expired'] ?? false;}unset($row);
 return $rows;
}
function createRouterDevice(PDO $db,array $body,int $actor,bool $admin,array $config): array {
 routerDevicesReady($db);$owner=$admin?($body['user_id'] ?? $actor):$actor;
 if(!is_int($owner)||$owner<1)problem('Выберите клиента.');
 $type=field($body,'resource_type',20);$id=$body['resource_id'] ?? null;$label=field($body,'label',400);
 if(!is_int($id)||$id<1||$label===''||preg_match_all('/./us',$label)>100)problem('Выберите ссылку и укажите название роутера до 100 символов.');
 $db->beginTransaction();$account=activeAccount($db,$owner,true);
 if((int)query($db,'SELECT COUNT(*) FROM internet_router_devices WHERE user_id=? AND revoked_at IS NULL AND expires_at>CURRENT_TIMESTAMP',[$owner])->fetchColumn()>=20)problem('Можно подключить до 20 роутеров. Отключите неиспользуемое устройство.',409);
 if(!routerDeviceSource($db,$type,$id,$owner,true))problem('Ссылка или ключ не найдены у выбранного клиента.',404);
 $selector=bin2hex(random_bytes(12));$secret=bin2hex(random_bytes(32));$token='mkr1.'.$selector.'.'.$secret;
 query($db,'INSERT INTO internet_router_devices(selector,token_hash,user_id,password_fingerprint,resource_type,resource_id,label,expires_at) VALUES (?,?,?,?,?,?,?,DATE_ADD(CURRENT_TIMESTAMP,INTERVAL 1 YEAR))',[$selector,hash('sha256',$secret),$owner,hash('sha256',$account['password_hash']),$type,$id,$label]);
 $deviceId=(int)$db->lastInsertId();$db->commit();
 return ['id'=>$deviceId,'token'=>$token,'endpoint'=>routerDeviceEndpoint($config)];
}
function revokeRouterDevice(PDO $db,int $id,int $actor,bool $admin): array {
 routerDevicesReady($db);
 if($id<1)problem('Выберите устройство.');
 $q=query($db,'UPDATE internet_router_devices SET revoked_at=COALESCE(revoked_at,CURRENT_TIMESTAMP) WHERE id=?'.($admin?'':' AND user_id=?'),$admin?[$id]:[$id,$actor]);
 if(!$q->rowCount()&&!query($db,'SELECT id FROM internet_router_devices WHERE id=?'.($admin?'':' AND user_id=?'),$admin?[$id]:[$id,$actor])->fetch())problem('Устройство не найдено.',404);
 return ['ok'=>true];
}
function revokeResourceDevices(PDO $db,string $type,int $id): void {
 if(routerDevicesReady($db,false))query($db,'UPDATE internet_router_devices SET revoked_at=COALESCE(revoked_at,CURRENT_TIMESTAMP) WHERE resource_type=? AND resource_id=?',[$type,$id]);
}
function authenticateRouterDevice(PDO $db,string $token): array {
 routerDevicesReady($db);
 if(!preg_match('/\Amkr1\.([a-f0-9]{24})\.([a-f0-9]{64})\z/D',$token,$m)){limitRate($db,'router-agent-auth',120);problem('Код устройства недействителен.',401);}
 $row=query($db,'SELECT * FROM internet_router_devices WHERE selector=?',[$m[1]])->fetch();
 if(!$row||!hash_equals($row['token_hash'],hash('sha256',$m[2]))||$row['revoked_at']!==null||strtotime($row['expires_at'].' UTC')<=time()){limitRate($db,'router-agent-auth',120);problem('Код устройства отключён или истёк. Создайте новый код в кабинете.',401);}
 $owner=rawAccount($db,(int)$row['user_id']);
 if(!$owner||authState($db,(int)$row['user_id'])['deleted_at']!==null||!hash_equals($row['password_fingerprint'],hash('sha256',$owner['password_hash'])))problem('Нужно заново подключить роутер в кабинете.',401);
 limitRate($db,'router-agent',64,(int)$row['id']);
 return $row;
}
function routerDeviceSnapshot(PDO $db,array $device): array {
 $source=routerDeviceSource($db,$device['resource_type'],(int)$device['resource_id'],(int)$device['user_id']);
 if(!$source){query($db,'UPDATE internet_router_devices SET revoked_at=CURRENT_TIMESTAMP WHERE id=?',[$device['id']]);problem('Привязка ссылки или ключа снята. Создайте новый код устройства.',410);}
 if($source['expired'])problem('Срок подписки истёк. Продлите её в кабинете.',410);
 if(strlen($source['value'])>8192||preg_match('/[\x00-\x20\x7f]/',$source['value']))problem('Ссылка требует ручной настройки.',422);
 query($db,'UPDATE internet_router_devices SET last_seen_at=CURRENT_TIMESTAMP WHERE id=?',[$device['id']]);
 $source['revision']=hash('sha256',$source['value']);unset($source['expired']);
 return ['version'=>1,'device_id'=>(int)$device['id'],'resource'=>$source];
}
function reportRouterDevice(PDO $db,array $device,array $body): array {
 $state=field($body,'state',30);$revision=field($body,'revision',64);$version=field($body,'client_version',30);
 if(!in_array($state,['current','available','applied','failed','manual'],true)||!preg_match('/\A[a-f0-9]{64}\z/D',$revision)||!preg_match('/\A[0-9]+\.[0-9]+\.[0-9]+\z/D',$version))problem('Некорректное состояние устройства.');
 $snapshot=routerDeviceSnapshot($db,$device);$resource=$snapshot['resource'];
 query($db,'UPDATE internet_router_devices SET last_state=?,last_revision=?,client_version=? WHERE id=?',[$state,$revision,$version,$device['id']]);
 if($state!=='current') {
  $titles=['available'=>'Для роутера есть новая ссылка','applied'=>'Ссылка роутера обновлена','failed'=>'Роутер не применил обновление','manual'=>'Роутеру нужна ручная настройка'];
  $text=match($state){'applied'=>'Роутер применил актуальное подключение.','failed'=>'Автоматическое обновление не завершено. Откройте настройки роутера и проверьте состояние.',default=>'Откройте «Сервисы → МосКомпРемонт» на роутере, чтобы посмотреть ссылку и состояние обновления.'};
  notifyClient($db,(int)$device['user_id'],'router-device:'.$device['id'].':'.$revision.':'.$state,'router_device_'.$state,$titles[$state],$device['label'].' · '.$resource['label'].'. '.$text,$resource['type']==='router_key'?'router-keys':'subscriptions');
 }
 return ['ok'=>true];
}
