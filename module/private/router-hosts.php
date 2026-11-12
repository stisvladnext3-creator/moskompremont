<?php
declare(strict_types=1);
function routerHost(string $value): string {
 $host=strtolower(trim($value));
 if(str_starts_with($host,'[')&&str_ends_with($host,']'))$host=substr($host,1,-1);
 if(filter_var($host,FILTER_VALIDATE_IP))return (string)inet_ntop(inet_pton($host));
 $host=rtrim($host,'.');
 if(strlen($host)>253||!preg_match('/\A(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\z/D',$host))problem('Введите домен или IP без https://, порта, пути и пробелов.');
 return $host;
}
function routerHostReplacement(string $value,string $host): ?array {
 $trimmed=trim($value);if(preg_match('/[\r\n]/',$trimmed))return null;
 if(!preg_match('~\Avless://[^@\s/?#]+@(\[[0-9a-f:.]+\]|[^:\s/?#]+):([0-9]{1,5})(?=[?#]|\z)~i',$trimmed,$m,PREG_OFFSET_CAPTURE))return null;
 $port=(int)$m[2][0];if($port<1||$port>65535)return null;
 try {$old=routerHost($m[1][0]);}catch(AccountError $e){return null;}
 $offset=strlen($value)-strlen(ltrim($value))+$m[1][1];$formatted=str_contains($host,':')?'['.$host.']':$host;
 $updated=substr($value,0,$offset).$formatted.substr($value,$offset+strlen($m[1][0]));
 return ['old_host'=>$old,'value'=>$old===$host?$value:$updated];
}
function routerHostRows(PDO $db,bool $lock=false): array {
 $rows=query($db,'SELECT id,label,key_format,key_hash,user_id,assigned_at,updated_at FROM internet_router_keys ORDER BY id LIMIT 10001'.($lock?' FOR UPDATE':''))->fetchAll();
 if(count($rows)>10000)problem('За одну замену поддерживается до 10000 ключей.',413);return $rows;
}
function routerHostSnapshot(array $rows): string {return hash('sha256',json_encode($rows,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE));}
function routerHostPlan(PDO $db,array $rows,string $newHost,string $oldHost): array {
 $changes=[];$items=[];$final=[];$same=0;$skipped=0;
 foreach($rows as $row){
  $id=(int)$row['id'];$hash=$row['key_hash'];$change=null;
  if(in_array($row['key_format'],['connection','text'],true)){
   $value=query($db,'SELECT key_value FROM internet_router_keys WHERE id=?',[$id])->fetchColumn();
   $change=is_string($value)?routerHostReplacement($value,$newHost):null;
  }
  if(!$change||($oldHost!==''&&$change['old_host']!==$oldHost))$skipped++;
  elseif($change['old_host']===$newHost)$same++;
  else {
   if(strlen($change['value'])>65536)problem('После замены ключ #'.$id.' превысит лимит 64 КБ. Уточните фильтр или сократите ключ.',413);
   $hash=hash('sha256',str_replace(["\r\n","\r"],"\n",trim($change['value'])));
   $changes[$id]=['hash'=>$hash];
   if(count($items)<100)$items[]=['id'=>$id,'label'=>$row['label'],'user_id'=>$row['user_id']===null?null:(int)$row['user_id'],'old_host'=>$change['old_host'],'new_host'=>$newHost];
  }
  if(isset($final[$hash]))problem('После замены ключи #'.$final[$hash].' и #'.$id.' станут одинаковыми. Уточните текущий домен/IP или уберите дубликат.',409);
  $final[$hash]=$id;unset($value,$change);
 }
 return ['changes'=>$changes,'items'=>$items,'total'=>count($rows),'changed'=>count($changes),'already_same'=>$same,'skipped'=>$skipped,'new_host'=>$newHost,'old_host'=>$oldHost];
}
function routerHostPreview(PDO $db,array $body): array {
 $new=routerHost(field($body,'new_host',253));$old=field($body,'old_host',253);if($old!=='')$old=routerHost($old);
 $rows=routerHostRows($db);$plan=routerHostPlan($db,$rows,$new,$old);$token=bin2hex(random_bytes(32));
 $_SESSION['router_host_preview']=['token'=>$token,'expires_at'=>time()+600,'new_host'=>$new,'old_host'=>$old,'snapshot'=>routerHostSnapshot($rows)];
 $owners=array_values(array_unique(array_filter(array_column($plan['items'],'user_id'))));$names=[];
 if($owners){$marks=implode(',',array_fill(0,count($owners),'?'));foreach(query($db,"SELECT id,first_name,last_name,email FROM internet_users WHERE id IN ($marks)",$owners)->fetchAll() as $owner)$names[(int)$owner['id']]=$owner['first_name'].' '.$owner['last_name'].' · '.$owner['email'];}
 foreach($plan['items'] as &$item)$item['owner']=$item['user_id']===null?'Свободный ключ':($names[$item['user_id']] ?? 'Клиент #'.$item['user_id']);unset($item);
 unset($plan['changes']);return $plan+['preview_token'=>$token,'expires_in'=>600];
}
function routerHostApply(PDO $db,array $body,int $adminId): array {
 $preview=$_SESSION['router_host_preview'] ?? null;$token=$body['preview_token'] ?? null;
 if(!is_array($preview)||!is_string($token)||!hash_equals($preview['token'],$token)||$preview['expires_at']<time())problem('Предпросмотр устарел. Сначала проверьте замену ещё раз.',409);
 $db->beginTransaction();
 try {
  $rows=routerHostRows($db,true);if(!hash_equals($preview['snapshot'],routerHostSnapshot($rows)))problem('Ключи или их владельцы изменились. Повторите предпросмотр перед заменой.',409);
  $plan=routerHostPlan($db,$rows,$preview['new_host'],$preview['old_host']);
  foreach($plan['changes'] as $id=>$change){
   $value=query($db,'SELECT key_value FROM internet_router_keys WHERE id=?',[$id])->fetchColumn();$replacement=routerHostReplacement($value,$preview['new_host']);
   query($db,'UPDATE internet_router_keys SET key_value=?,key_hash=? WHERE id=?',[$replacement['value'],$change['hash'],$id]);
  }
  if($plan['changed'])query($db,'INSERT INTO internet_admin_events(event_key,type,user_id,title,body,read_at) VALUES (?,?,?,?,?,CURRENT_TIMESTAMP)',[
   'router-host:'.bin2hex(random_bytes(12)),'system',$adminId,'Обновлён адрес в ключах для роутеров','Администратор #'.$adminId."\nИзменено ключей: ".$plan['changed']."\nНовый адрес: ".$preview['new_host']."\nТексты ключей в журнал не записываются."
  ]);
  $db->commit();unset($_SESSION['router_host_preview']);return ['ok'=>true,'changed'=>$plan['changed'],'new_host'=>$preview['new_host']];
 }catch(PDOException $e){if($db->inTransaction())$db->rollBack();if(in_array($e->getCode(),['23000','40001'],true))problem('Ключи изменились или возник дубликат. Повторите предпросмотр.',409);throw $e;}
}
