<?php
declare(strict_types=1);
function clientEventsReady(PDO $db): bool {
 static $cache=[];$key=spl_object_id($db);if(array_key_exists($key,$cache))return $cache[$key];
 try {query($db,'SELECT id FROM internet_client_notifications LIMIT 0');query($db,'SELECT resource_id FROM internet_resource_updates LIMIT 0');return $cache[$key]=true;}
 catch(PDOException $e){if($e->getCode()==='42S02')return $cache[$key]=false;throw $e;}
}
function resourceChanged(PDO $db,string $type,int $id): void {
 if(!clientEventsReady($db))return;
 query($db,'INSERT INTO internet_resource_updates(resource_type,resource_id) VALUES (?,?) ON DUPLICATE KEY UPDATE content_updated_at=CURRENT_TIMESTAMP,revision=revision+1',[$type,$id]);
}
function resourceRemoved(PDO $db,string $type,int $id): void {
 if(clientEventsReady($db))query($db,'DELETE FROM internet_resource_updates WHERE resource_type=? AND resource_id=?',[$type,$id]);
}
function withResourceUpdates(PDO $db,array $rows,string $type): array {
 if(!$rows)return [];$updates=[];
 if(clientEventsReady($db)){$ids=array_map('intval',array_column($rows,'id'));$marks=implode(',',array_fill(0,count($ids),'?'));foreach(query($db,"SELECT resource_id,content_updated_at FROM internet_resource_updates WHERE resource_type=? AND resource_id IN ($marks)",array_merge([$type],$ids))->fetchAll() as $row)$updates[(int)$row['resource_id']]=$row['content_updated_at'];}
 foreach($rows as &$row)$row['content_updated_at']=$updates[(int)$row['id']] ?? null;unset($row);return $rows;
}
function notifyClient(PDO $db,int $userId,string $key,string $type,string $title,string $body,string $target,?int $targetId=null,?string $targetRef=null): ?int {
 if(!clientEventsReady($db))return null;
 if(!in_array($target,['subscriptions','router-keys','ticket','admin-users','admin-payments','admin-ticket','admin-mail'],true))throw new LogicException('Invalid client notification target');
 $q=query($db,'INSERT IGNORE INTO internet_client_notifications(user_id,event_key,type,title,body,target,target_id,target_ref) VALUES (?,?,?,?,?,?,?,?)',[$userId,$key,$type,$title,$body,$target,$targetId,$targetRef]);
 if(!$q->rowCount())return null;$id=(int)$db->lastInsertId();if(function_exists('queueClientPush'))queueClientPush($db,$id,$userId);return $id;
}
function notifyResourceBatch(PDO $db,string $type,array $changes): void {
 $key=bin2hex(random_bytes(16));$target=$type==='subscription'?'subscriptions':'router-keys';
 foreach($changes as $userId=>$names){$names=array_values(array_unique($names));if(!$names)continue;
  $title=$type==='subscription'?'Ваши подписки обновлены':'Ключи для роутера обновлены';
  $body=($type==='subscription'?'Обновлено подписок: ':'Обновлено ключей: ').count($names).'. '.implode(', ',array_slice($names,0,3)).(count($names)>3?' и другие.':'.').' Откройте раздел в кабинете, чтобы получить актуальные данные.';
  notifyClient($db,(int)$userId,'resource-update:'.$key.':'.$userId,$type.'_updated',$title,$body,$target);
 }
}
function clientNotificationSummary(PDO $db,int $userId,bool $admin=false): array {
 if(!clientEventsReady($db))return ['unread_notifications'=>0,'latest_notification_id'=>0];
 $where=$admin?'':" AND type NOT LIKE 'admin\_%'";
 $row=query($db,'SELECT COUNT(*) AS total,MAX(id) AS last_id FROM internet_client_notifications WHERE user_id=? AND read_at IS NULL'.$where,[$userId])->fetch();
 return ['unread_notifications'=>(int)$row['total'],'latest_notification_id'=>(int)($row['last_id'] ?? 0)];
}
function clientNotifications(PDO $db,int $userId,array $params,bool $admin=false): array {
 if(!clientEventsReady($db))problem('Уведомления ещё настраиваются. Попробуйте позже.',503);
 $page=max(0,min(10000,(int)($params['page'] ?? 0)));$offset=$page*50;
 $where=$admin?'':" AND type NOT LIKE 'admin\_%'";
 $rows=query($db,"SELECT id,type,title,body,target,target_id,target_ref,created_at,read_at FROM internet_client_notifications WHERE user_id=?$where ORDER BY id DESC LIMIT 50 OFFSET $offset",[$userId])->fetchAll();
 $total=(int)query($db,'SELECT COUNT(*) FROM internet_client_notifications WHERE user_id=?'.$where,[$userId])->fetchColumn();return ['notifications'=>$rows,'total'=>$total]+clientNotificationSummary($db,$userId,$admin);
}
function readClientNotification(PDO $db,int $userId,array $body,bool $admin=false): array {
 if(!clientEventsReady($db))problem('Уведомления ещё настраиваются.',503);
 $visibility=$admin?'':" AND type NOT LIKE 'admin\\_%'";
 if(($body['all'] ?? false)===true)query($db,'UPDATE internet_client_notifications SET read_at=COALESCE(read_at,CURRENT_TIMESTAMP) WHERE user_id=?'.$visibility,[$userId]);
 else {$id=$body['id'] ?? null;if(!is_int($id)||$id<1)problem('Выберите уведомление.');$item=query($db,'SELECT id FROM internet_client_notifications WHERE id=? AND user_id=?'.$visibility,[$id,$userId])->fetch();if(!$item)problem('Уведомление не найдено.',404);query($db,'UPDATE internet_client_notifications SET read_at=COALESCE(read_at,CURRENT_TIMESTAMP) WHERE id=? AND user_id=?',[$id,$userId]);}
 return ['ok'=>true]+clientNotificationSummary($db,$userId,$admin);
}

function notifyAdminOnSite(PDO $db,array $config,string $key,string $type,int $subjectUserId,string $title,string $body,?string $orderId): void {
 $target=match($type){'user'=>'admin-users','payment'=>'admin-payments','support'=>'admin-ticket',default=>'admin-mail'};$targetId=$type==='user'?$subjectUserId:null;
 if($type==='support'&&preg_match('/\Amessage:([0-9]+)\z/D',$key,$m)){$value=query($db,'SELECT ticket_id FROM internet_messages WHERE id=?',[(int)$m[1]])->fetchColumn();$targetId=$value!==false?(int)$value:null;}
 foreach(array_unique(array_map('intval',$config['admin_user_ids'] ?? [])) as $id){if($id<1)continue;$account=rawAccount($db,$id);if(!$account||authState($db,$id)['deleted_at']!==null)continue;notifyClient($db,$id,'admin-email:'.$key,'admin_'.$type,$title,$body,$target,$targetId,$orderId);}
}
