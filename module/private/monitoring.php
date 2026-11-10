<?php
declare(strict_types=1);
function monitorSettings(PDO $db): array {
 return query($db,'SELECT * FROM internet_monitor WHERE id=1')->fetch() ?: ['enabled'=>0,'subscription_url'=>null,'token_hash'=>null,'revision'=>1,'results'=>null,'reported_at'=>null,'source_status'=>null,'main_host'=>null,'main_port'=>443,'main_state'=>null,'main_latency_ms'=>null,'main_checked_at'=>null];
}
function monitorAdmin(PDO $db): array {
 $s=monitorSettings($db);
 return ['enabled'=>(bool)$s['enabled'],'subscription_set'=>!empty($s['subscription_url']),'token_ready'=>!empty($s['token_hash']),'main_host'=>$s['main_host'] ?? '', 'main_port'=>(int)$s['main_port'],'reported_at'=>$s['reported_at'],'source_status'=>$s['source_status'],'revision'=>(int)$s['revision']];
}
function monitorSave(PDO $db,array $body): array {
 $url=field($body,'subscription_url',8192);
 if ($url!=='' && (!filter_var($url,FILTER_VALIDATE_URL) || parse_url($url,PHP_URL_SCHEME)!=='https' || parse_url($url,PHP_URL_USER)!==null || parse_url($url,PHP_URL_PASS)!==null || parse_url($url,PHP_URL_FRAGMENT)!==null))problem('Укажите HTTPS-ссылку подписки без логина и пароля в адресе.');
 $enabled=$body['enabled'] ?? null;if (!is_bool($enabled))problem('Проверьте включение мониторинга.');
 $host=field($body,'main_host',253);$port=$body['main_port'] ?? 443;
 if (!is_int($port) || $port<1 || $port>65535)problem('Порт: от 1 до 65535.');
 if ($host!=='' && !filter_var($host,FILTER_VALIDATE_IP) && (!preg_match('/\A(?=.{1,253}\z)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}\z/D',$host)))problem('Укажите адрес сервера: доменное имя или публичный IP, без https:// и пути.');
 $db->beginTransaction();
 try {
  query($db,'INSERT IGNORE INTO internet_monitor(id) VALUES (1)');$old=query($db,'SELECT * FROM internet_monitor WHERE id=1 FOR UPDATE')->fetch();
  $next=$url!==''?$url:($old['subscription_url'] ?? '');if ($enabled && $next==='')problem('Добавьте ссылку подписки.');
  $changed=$next!==($old['subscription_url'] ?? '') || $enabled!==(bool)$old['enabled'];
  $mainChanged=$host!==($old['main_host'] ?? '') || $port!==(int)$old['main_port'];
  query($db,'UPDATE internet_monitor SET enabled=?,subscription_url=?,main_host=?,main_port=?,revision=revision+?,results=IF(?,NULL,results),reported_at=IF(?,NULL,reported_at),source_status=IF(?,NULL,source_status),main_state=IF(?,NULL,main_state),main_checked_at=IF(?,NULL,main_checked_at) WHERE id=1',[(int)$enabled,$next,$host,$port,(int)$changed,(int)$changed,(int)$changed,(int)$changed,(int)$mainChanged,(int)$mainChanged]);
  $db->commit();
 }catch(Throwable $e){if($db->inTransaction())$db->rollBack();throw $e;}
 return monitorAdmin($db);
}
function monitorToken(PDO $db): string {
 $token=bin2hex(random_bytes(32));query($db,'INSERT INTO internet_monitor(id,token_hash) VALUES (1,?) ON DUPLICATE KEY UPDATE token_hash=VALUES(token_hash)',[hash('sha256',$token)]);return $token;
}
function monitorFresh(?string $value,int $seconds=180): bool {
 if (!$value)return false;$time=strtotime($value.' UTC');return $time!==false && $time<=time()+120 && $time>=time()-$seconds;
}
function monitorPublicAddress(string $ip): bool {
 $packed=@inet_pton($ip);if($packed!==false&&strlen($packed)===16&&substr($packed,0,12)===hex2bin('00000000000000000000ffff'))$ip=inet_ntop(substr($packed,12));
 return filter_var($ip,FILTER_VALIDATE_IP,FILTER_FLAG_NO_PRIV_RANGE|FILTER_FLAG_NO_RES_RANGE)!==false;
}
function monitorCheckMain(PDO $db,array $s): void {
 if(!$s['enabled']||empty($s['main_host']))return;
 $host=$s['main_host'];$port=(int)$s['main_port'];
 $claim=query($db,"UPDATE internet_monitor SET main_state='unknown',main_latency_ms=NULL,main_checked_at=UTC_TIMESTAMP() WHERE id=1 AND enabled=1 AND main_host=? AND main_port=? AND (main_checked_at IS NULL OR main_checked_at<DATE_SUB(UTC_TIMESTAMP(),INTERVAL 60 SECOND))",[$host,$port]);
 if(!$claim->rowCount())return;
 $addresses=[];if(filter_var($host,FILTER_VALIDATE_IP))$addresses=[$host];else foreach(@dns_get_record($host,DNS_A|DNS_AAAA) ?: [] as $record){$ip=$record['ip'] ?? $record['ipv6'] ?? null;if($ip)$addresses[]=$ip;}
 $addresses=array_values(array_filter(array_unique($addresses),'monitorPublicAddress'));$state='unknown';$latency=null;
 foreach(array_slice($addresses,0,2) as $ip){$state='down';$start=microtime(true);$target=str_contains($ip,':')?'['.$ip.']':$ip;$socket=@stream_socket_client('tcp://'.$target.':'.$port,$code,$error,2,STREAM_CLIENT_CONNECT);if($socket!==false){fclose($socket);$state='up';$latency=max(1,(int)ceil((microtime(true)-$start)*1000));break;}}
 query($db,'UPDATE internet_monitor SET main_state=?,main_latency_ms=?,main_checked_at=UTC_TIMESTAMP() WHERE id=1 AND enabled=1 AND main_host=? AND main_port=?',[$state,$latency,$host,$port]);
}
function monitorPublic(PDO $db): array {
 $s=monitorSettings($db);if (!$s['enabled'])return ['enabled'=>false,'nodes'=>[],'main'=>null,'reported_at'=>null];
 if(!empty($s['main_host'])){monitorCheckMain($db,$s);$s=monitorSettings($db);}
 $fresh=monitorFresh($s['reported_at']);$items=json_decode($s['results'] ?? '[]',true) ?: [];$nodes=[];
 foreach ($items as $node) {
  $valid=$fresh && monitorFresh($node['checked_at'] ?? null);$state=$valid?$node['status']:'unknown';
  $nodes[]=['id'=>$node['id'],'name'=>$node['name'],'status'=>$state,'latency_ms'=>$state==='up'?$node['latency_ms']:null,'checked_at'=>$node['checked_at']];
 }
 $main=null;if (!empty($s['main_host'])){$state=monitorFresh($s['main_checked_at'])?($s['main_state'] ?? 'unknown'):'unknown';$main=['name'=>'Основной сервер','status'=>$state,'latency_ms'=>$state==='up'?(int)$s['main_latency_ms']:null,'checked_at'=>$s['main_checked_at']];}
 return ['enabled'=>true,'nodes'=>$nodes,'main'=>$main,'reported_at'=>$s['reported_at'],'fresh'=>$fresh,'source_status'=>$s['source_status']];
}
function monitorReport(PDO $db,array $body,int $revision): void {
 $incoming=$body['revision'] ?? null;if (!is_int($incoming) || $incoming!==$revision)problem('Настройки изменились. Загрузите их повторно.',409);
 $source=$body['source_status'] ?? '';if (!in_array($source,['ok','fetch_error','parse_error','limit_exceeded'],true))problem('Некорректный статус источника.');
 $items=$body['nodes'] ?? null;if (!is_array($items) || !array_is_list($items) || count($items)>100)problem('Допустимо до 100 узлов.');
 $clean=[];$seen=[];
 foreach($items as $node){
  if(!is_array($node))problem('Некорректный узел.');$id=$node['id'] ?? '';if (!is_string($id)||!preg_match('/\A[0-9a-f]{64}\z/D',$id)||isset($seen[$id]))problem('Некорректный ID узла.');$seen[$id]=true;
  $name=field($node,'name',400);if($name===''||preg_match_all('/./us',$name)>100)problem('Проверьте название узла.');
  $state=$node['status'] ?? '';if(!in_array($state,['up','down','unknown','unsupported'],true))problem('Некорректный статус узла.');
  $latency=$node['latency_ms'] ?? null;if($state==='up' && (!is_int($latency)||$latency<1||$latency>120000))problem('Некорректное время ответа.');
  $stamp=$node['checked_at'] ?? null;if($stamp!==null && (!is_string($stamp)||!preg_match('/\A\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\z/D',$stamp)||!monitorFresh($stamp,86400)))problem('Некорректное время проверки.');
  if($state==='up' && $stamp===null)problem('Не указано время проверки.');
  $clean[]=['id'=>$id,'name'=>$name,'status'=>$state,'latency_ms'=>$state==='up'?$latency:null,'checked_at'=>$stamp];
 }
 $q=query($db,'UPDATE internet_monitor SET results=?,reported_at=UTC_TIMESTAMP(),source_status=? WHERE id=1 AND enabled=1 AND revision=?',[json_encode($clean,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$source,$revision]);
 if(!$q->rowCount()){ $current=monitorSettings($db);if(!$current['enabled'] || (int)$current['revision']!==$revision)problem('Мониторинг отключён или настройки изменились.',409); }
}
