<?php
declare(strict_types=1);
require_once __DIR__.'/router-hosts.php';

// Replace only the authority host; never rebuild/normalize the rest of the URI.
function subscriptionHostReplacement(string $value,string $host): ?array {
 $trimmed=trim($value);
 try{subscriptionUrl($trimmed);}catch(AccountError $e){return null;}
 if(!preg_match('~\A((?:https?|vless|trojan|ss|hysteria2)://(?:[^@\s/?#]+@)?)(\[[0-9a-f:.]+\]|[^:@\s/?#]+)(?::([0-9]{1,5}))?(?=[/?#]|\z)~i',$trimmed,$m,PREG_OFFSET_CAPTURE))return null;
 $http=(bool)preg_match('~\Ahttps?://~i',$trimmed);
 if(!$http&&(!str_contains($m[1][0],'@')||!isset($m[3])||$m[3][0]===''))return null;
 if(isset($m[3])&&$m[3][0]!==''&&((int)$m[3][0]<1||(int)$m[3][0]>65535))return null;
 try{$old=routerHost($m[2][0]);}catch(AccountError $e){return null;}
 $offset=strlen($value)-strlen(ltrim($value))+$m[2][1];$formatted=str_contains($host,':')?'['.$host.']':$host;
 $updated=$old===$host?$value:substr($value,0,$offset).$formatted.substr($value,$offset+strlen($m[2][0]));
 return ['old_host'=>$old,'value'=>$updated];
}
function subscriptionHostRows(PDO $db,bool $lock=false): array {
 $rows=query($db,'SELECT id,name,user_id,assigned_at,updated_at,SHA2(url,256) AS url_hash FROM internet_subscriptions ORDER BY id LIMIT 10001'.($lock?' FOR UPDATE':''))->fetchAll();
 if(count($rows)>10000)problem('За одну замену поддерживается до 10000 подписок.',413);return $rows;
}
function subscriptionHostSnapshot(array $rows): string {return hash('sha256',json_encode($rows,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE));}
function subscriptionHostValues(PDO $db,array $ids): Generator {
 foreach(array_chunk($ids,100) as $batch){$marks=implode(',',array_fill(0,count($batch),'?'));foreach(query($db,"SELECT id,url FROM internet_subscriptions WHERE id IN ($marks) ORDER BY id",$batch)->fetchAll() as $row)yield $row;}
}
function subscriptionHostPlan(PDO $db,array $rows,string $newHost,string $oldHost): array {
 $metadata=[];foreach($rows as $row)$metadata[(int)$row['id']]=$row;
 $changes=[];$items=[];$final=[];$same=0;$skipped=0;$seen=0;
 foreach(subscriptionHostValues($db,array_keys($metadata)) as $value){
  $seen++;$id=(int)$value['id'];$row=$metadata[$id];$change=subscriptionHostReplacement($value['url'],$newHost);$hash=$row['url_hash'];
  // Prevent a torn preview if a row was changed while the metadata was being read.
  if(!hash_equals($hash,hash('sha256',$value['url'])))problem('База подписок изменилась. Повторите предпросмотр.',409);
  if(!$change||($oldHost!==''&&$change['old_host']!==$oldHost))$skipped++;
  elseif($change['old_host']===$newHost)$same++;
  else{
   if(strlen($change['value'])>8192)problem('После замены ссылка подписки #'.$id.' превысит лимит 8192 байта. Уточните фильтр или сократите ссылку.',413);
   $hash=hash('sha256',$change['value']);$changes[$id]=$hash;
   if(count($items)<100)$items[]=['id'=>$id,'name'=>$row['name'],'user_id'=>$row['user_id']===null?null:(int)$row['user_id'],'old_host'=>$change['old_host'],'new_host'=>$newHost];
  }
  // Existing identical URLs are allowed by the subscription database. Block newly introduced collisions.
  if(isset($final[$hash])&&!hash_equals($final[$hash]['original'],$row['url_hash']))problem('После замены ссылки подписок #'.$final[$hash]['id'].' и #'.$id.' совпадут. Проверьте записи или уточните текущий домен/IP.',409);
  $final[$hash]=['id'=>$id,'original'=>$row['url_hash']];
 }
 if($seen!==count($rows))problem('База подписок изменилась. Повторите предпросмотр.',409);
 return ['changes'=>$changes,'items'=>$items,'total'=>count($rows),'changed'=>count($changes),'already_same'=>$same,'skipped'=>$skipped,'new_host'=>$newHost,'old_host'=>$oldHost];
}
function subscriptionHostPreview(PDO $db,array $body): array {
 $new=routerHost(field($body,'new_host',253));$old=field($body,'old_host',253);if($old!=='')$old=routerHost($old);
 $rows=subscriptionHostRows($db);$plan=subscriptionHostPlan($db,$rows,$new,$old);
 // Re-read after constructing the preview so insertions/deletions/ownership edits also invalidate it.
 $snapshot=subscriptionHostSnapshot($rows);if(!hash_equals($snapshot,subscriptionHostSnapshot(subscriptionHostRows($db))))problem('База подписок изменилась. Повторите предпросмотр.',409);
 $token=bin2hex(random_bytes(32));$_SESSION['subscription_host_preview']=['token'=>$token,'expires_at'=>time()+600,'new_host'=>$new,'old_host'=>$old,'snapshot'=>$snapshot];
 $owners=array_values(array_unique(array_filter(array_column($plan['items'],'user_id'))));$names=[];
 if($owners){$marks=implode(',',array_fill(0,count($owners),'?'));foreach(query($db,"SELECT id,first_name,last_name,email FROM internet_users WHERE id IN ($marks)",$owners)->fetchAll() as $owner)$names[(int)$owner['id']]=$owner['first_name'].' '.$owner['last_name'].' · '.$owner['email'];}
 foreach($plan['items'] as &$item)$item['owner']=$item['user_id']===null?'Свободная подписка':($names[$item['user_id']] ?? 'Клиент #'.$item['user_id']);unset($item);
 unset($plan['changes']);return $plan+['preview_token'=>$token,'expires_in'=>600];
}
function subscriptionHostApply(PDO $db,array $body,int $adminId): array {
 $preview=$_SESSION['subscription_host_preview'] ?? null;$token=$body['preview_token'] ?? null;
 if(!is_array($preview)||!is_string($token)||!hash_equals($preview['token'],$token)||$preview['expires_at']<time())problem('Предпросмотр устарел. Сначала проверьте замену ещё раз.',409);
 $db->beginTransaction();
 try{
  $rows=subscriptionHostRows($db,true);if(!hash_equals($preview['snapshot'],subscriptionHostSnapshot($rows)))problem('Ссылки или их владельцы изменились. Повторите предпросмотр перед заменой.',409);
  $plan=subscriptionHostPlan($db,$rows,$preview['new_host'],$preview['old_host']);$metadata=[];$affected=[];foreach($rows as $row)$metadata[(int)$row['id']]=$row;
  foreach(subscriptionHostValues($db,array_keys($plan['changes'])) as $value){
   $id=(int)$value['id'];$replacement=subscriptionHostReplacement($value['url'],$preview['new_host']);
   if(!$replacement||!hash_equals($plan['changes'][$id],hash('sha256',$replacement['value'])))problem('Ссылки изменились. Повторите предпросмотр.',409);
   query($db,'UPDATE internet_subscriptions SET url=? WHERE id=?',[$replacement['value'],$id]);resourceChanged($db,'subscription',$id);
   if($metadata[$id]['user_id']!==null)$affected[(int)$metadata[$id]['user_id']][]=$metadata[$id]['name'];
  }
  notifyResourceBatch($db,'subscription',$affected);
  if($plan['changed'])query($db,'INSERT INTO internet_admin_events(event_key,type,user_id,title,body,read_at) VALUES (?,?,?,?,?,CURRENT_TIMESTAMP)',[
   'subscription-host:'.bin2hex(random_bytes(12)),'system',$adminId,'Обновлён адрес в ссылках подписок','Администратор #'.$adminId."\nИзменено подписок: ".$plan['changed']."\nНовый адрес: ".$preview['new_host']."\nПолные ссылки в журнал не записываются."
  ]);
  $db->commit();unset($_SESSION['subscription_host_preview']);return ['ok'=>true,'changed'=>$plan['changed'],'new_host'=>$preview['new_host']];
 }catch(PDOException $e){if($db->inTransaction())$db->rollBack();if(in_array($e->getCode(),['23000','40001'],true))problem('База подписок изменилась. Повторите предпросмотр.',409);throw $e;}
}
