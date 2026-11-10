<?php
declare(strict_types=1);
require_once __DIR__.'/notifications.php';
class AccountError extends RuntimeException { public function __construct(string $message,public int $httpStatus=400){parent::__construct($message);} }
function problem(string $message,int $status=400): never {throw new AccountError($message,$status);}
function query(PDO $db,string $sql,array $args=[]): PDOStatement {$q=$db->prepare($sql);$q->execute($args);return $q;}
function field(array $body,string $key,int $max,bool $multiline=false): string {
 $v=$body[$key] ?? '';
 if (!is_string($v) || strlen($v)>$max || !preg_match('//u',$v) || preg_match($multiline?'/[\x00-\x08\x0b\x0c\x0e-\x1f]/':'/[\x00-\x1f]/',$v)) problem('Проверьте поле: '.$key);
 return trim($v);
}
function publicUser(array $u,array $config): array {
 return ['id'=>(int)$u['id'],'first_name'=>$u['first_name'],'last_name'=>$u['last_name'],'telegram'=>$u['telegram'],'email'=>$u['email'],'is_admin'=>in_array((int)$u['id'],array_map('intval',$config['admin_user_ids'] ?? []),true)];
}
function contactText(array $u): string {return $u['first_name'].' '.$u['last_name']."\nПочта: ".$u['email']."\nTelegram: ".$u['telegram'];}
function payment(array $config): array {
 $image=$config['qr_image'] ?? '';$ready=is_string($image) && preg_match('~^images/payment/[a-zA-Z0-9_.-]+\.(png|jpe?g|webp)$~i',$image) && is_file(dirname(__DIR__).'/'.$image) && !empty($config['recipient']);
 return ['available'=>(bool)$ready,'qr_image'=>$ready?$image:null,'recipient'=>$ready?$config['recipient']:'','note'=>$config['payment_note'] ?? ''];
}
function limitRate(PDO $db,string $scope,int $maximum,?int $userId=null): void {
 $identity=$userId===null?($_SERVER['REMOTE_ADDR'] ?? 'unknown'):'user:'.$userId;
 $bucket=hash('sha256',$scope.'|'.$identity.'|'.intdiv(time(),900));
 query($db,'INSERT INTO internet_rate_limits(bucket,attempts,expires_at) VALUES (?,1,?) ON DUPLICATE KEY UPDATE attempts=attempts+1',[$bucket,time()+1800]);
 if ((int)query($db,'SELECT attempts FROM internet_rate_limits WHERE bucket=?',[$bucket])->fetchColumn()>$maximum) problem('Слишком много попыток. Повторите через 15 минут.',429);
 if (random_int(1,50)===1) query($db,'DELETE FROM internet_rate_limits WHERE expires_at < ?',[time()]);
}
function subscriptionName(string $value): string {
 $v=strtoupper(trim($value));if (!preg_match('/\A[A-Z0-9_.-]{1,100}\z/D',$v)) problem('Название подписки: латинские буквы, цифры, точка, дефис или подчёркивание (до 100 символов).');return $v;
}
function subscriptionUrl(string $value): string {
 if (!$value || strlen($value)>8192 || preg_match('/[\s\x00-\x1f]/u',$value) || !preg_match('~\A(?:https?|vless|vmess|trojan|ss|hysteria2)://~i',$value)) problem('Укажите корректную подписочную ссылку.');
 if (preg_match('~\Ahttps?://~i',$value) && (!filter_var($value,FILTER_VALIDATE_URL) || !parse_url($value,PHP_URL_HOST))) problem('Укажите корректную HTTP-ссылку.');return $value;
}
function claimNames(string $text): array {
 if ($text==='') return [];$parts=preg_split('/[\s,;]+/u',$text,-1,PREG_SPLIT_NO_EMPTY);if (count($parts)>20) problem('Можно указать до 20 подписок.');return array_values(array_unique(array_map('subscriptionName',$parts)));
}
function claimSubscriptions(PDO $db,int $userId,array $names): void {
 foreach ($names as $name) {
  // Match import, issuance and unbinding: lock the subscription before its claims.
  $sub=query($db,'SELECT * FROM internet_subscriptions WHERE name=? FOR UPDATE',[$name])->fetch();
  query($db,'INSERT INTO internet_subscription_claims(user_id,name) VALUES (?,?) ON DUPLICATE KEY UPDATE name=VALUES(name)',[$userId,$name]);
  $claim=query($db,'SELECT * FROM internet_subscription_claims WHERE user_id=? AND name=? FOR UPDATE',[$userId,$name])->fetch();
  if ($sub && query($db,'SELECT subscription_id FROM internet_subscription_revocations WHERE subscription_id=? AND user_id=?',[$sub['id'],$userId])->fetch()) {
   if ($claim['status']==='revoked')query($db,"UPDATE internet_subscription_claims SET status='pending' WHERE id=?",[$claim['id']]);
   continue;
  }
  if ($claim['status']!=='pending') continue;
  if ($sub && $sub['user_id']===null && (int)query($db,"SELECT COUNT(*) FROM internet_subscription_claims c WHERE c.name=? AND c.status='pending' AND NOT EXISTS (SELECT 1 FROM internet_subscription_revocations r WHERE r.subscription_id=? AND r.user_id=c.user_id)",[$name,$sub['id']])->fetchColumn()>1)continue;
  if ($sub && ($sub['user_id']===null || (int)$sub['user_id']===$userId)) {
   query($db,'UPDATE internet_subscriptions SET user_id=?,assigned_at=COALESCE(assigned_at,CURRENT_TIMESTAMP) WHERE id=?',[$userId,$sub['id']]);
   query($db,"UPDATE internet_subscription_claims SET status='approved',reviewed_at=CURRENT_TIMESTAMP WHERE id=?",[$claim['id']]);
  }
 }
}
function reconcileClaims(PDO $db,string $name): int {
 $sub=query($db,'SELECT * FROM internet_subscriptions WHERE name=? FOR UPDATE',[$name])->fetch();if (!$sub || $sub['user_id']!==null)return 0;
 $claims=query($db,"SELECT c.* FROM internet_subscription_claims c WHERE c.name=? AND c.status='pending' AND NOT EXISTS (SELECT 1 FROM internet_subscription_revocations r WHERE r.subscription_id=? AND r.user_id=c.user_id) ORDER BY c.id FOR UPDATE",[$name,$sub['id']])->fetchAll();
 // Several claimants need an administrator's choice; never award a contested name arbitrarily.
 if (count($claims)!==1)return 0;
 $claim=$claims[0];query($db,'UPDATE internet_subscriptions SET user_id=?,assigned_at=CURRENT_TIMESTAMP WHERE id=?',[$claim['user_id'],$sub['id']]);
 query($db,"UPDATE internet_subscription_claims SET status='approved',reviewed_at=CURRENT_TIMESTAMP WHERE id=?",[$claim['id']]);return 1;
}
function importCsv(string $csv): array {
 if (strlen($csv)>1048576 || !preg_match('//u',$csv))problem('Нужен CSV в UTF-8 размером до 1 МБ.');
 $csv=preg_replace('/\A\xEF\xBB\xBF/','',$csv);$stream=fopen('php://temp','r+');fwrite($stream,$csv);rewind($stream);
 $first=strtok($csv,"\r\n") ?: '';$delimiter=substr_count($first,';')>substr_count($first,',')?';':',';
 $header=fgetcsv($stream,0,$delimiter,'"','');if (!$header || count($header)!==2)problem('CSV должен содержать две колонки: name,url.');
 $h=array_map(fn($s)=>strtolower(trim($s)),$header);
 if (!in_array($h[0],['name','название'],true) || !in_array($h[1],['url','ссылка'],true))problem('Первая строка CSV: name,url');
 $records=[];$line=1;
 while (($row=fgetcsv($stream,0,$delimiter,'"',''))!==false) {
  $line++;if ($row===[null] || (count($row)===1 && trim((string)$row[0])===''))continue;
  if (count($row)!==2)problem('Проверьте CSV, строка '.$line.': должно быть две колонки.');
  $name=subscriptionName((string)$row[0]);$url=subscriptionUrl(trim((string)$row[1]));if (isset($records[$name]))problem('Название '.$name.' повторяется в CSV.');$records[$name]=$url;
  if (count($records)>1000)problem('За один импорт можно загрузить до 1000 подписок.');
 }
 fclose($stream);if (!$records)problem('В CSV пока нет подписок.');return $records;
}

function withOrderSubscriptions(PDO $db,array $orders): array {
 foreach($orders as &$order) {
  $order['revoked_subscriptions']=query($db,'SELECT s.name,r.revoked_at FROM internet_order_subscriptions os JOIN internet_subscriptions s ON s.id=os.subscription_id JOIN internet_subscription_revocations r ON r.subscription_id=s.id AND r.user_id=? WHERE os.order_id=? ORDER BY s.id',[$order['user_id'],$order['id']])->fetchAll();
  $order['subscriptions']=query($db,'SELECT s.id,s.name,s.url,os.issued_at FROM internet_order_subscriptions os JOIN internet_subscriptions s ON s.id=os.subscription_id WHERE os.order_id=? AND s.user_id=? ORDER BY os.issued_at,s.id',[$order['id'],$order['user_id']])->fetchAll();
 }
 unset($order);return $orders;
}

require_once __DIR__.'/support-photos.php';

function directAssignmentItems(array $body): array {
 $wanted=[];
 foreach(claimNames(field($body,'names',2500,true)) as $name)$wanted[$name]=null;
 $rows=$body['new_subscriptions'] ?? [];
 if (!is_array($rows) || !array_is_list($rows) || count($rows)>20)problem('Можно добавить до 20 новых подписок за раз.');
 foreach($rows as $row) {
  if (!is_array($row))problem('Проверьте данные новой подписки.');
  $name=field($row,'name',100);$url=field($row,'url',8192);
  if ($name==='' && $url==='')continue;
  if ($name==='' || $url==='')problem('Для новой подписки укажите и название, и ссылку.');
  $name=subscriptionName($name);$url=subscriptionUrl($url);
  if (isset($wanted[$name]) && $wanted[$name]!==$url)problem('Для названия '.$name.' указаны разные ссылки.');
  $wanted[$name]=$url;
 }
 if (!$wanted)problem('Укажите названия из базы или добавьте новую подписку со ссылкой.');
 if (count($wanted)>20)problem('За раз можно назначить до 20 подписок.');
 ksort($wanted,SORT_STRING);return $wanted;
}
function assignDirectly(PDO $db,array $recipient,int $adminId,array $wanted): array {
 if (!$db->inTransaction())throw new LogicException('Direct assignment requires a transaction');
 $recipientId=(int)$recipient['id'];$assigned=0;$created=0;$already=0;
 foreach($wanted as $name=>$url) {
  $sub=query($db,'SELECT * FROM internet_subscriptions WHERE name=? FOR UPDATE',[$name])->fetch();
  if ($sub) {
   if ($sub['user_id']!==null && (int)$sub['user_id']!==$recipientId)problem('Подписка '.$name.' уже закреплена за другим клиентом. Сначала снимите её привязку.',409);
   if ($url!==null && $url!==$sub['url'])problem('Название '.$name.' уже есть в базе с другой ссылкой. Выберите его по названию или укажите новое название.',409);
   $id=(int)$sub['id'];
   if ((int)$sub['user_id']===$recipientId)$already++;
   else {query($db,'UPDATE internet_subscriptions SET user_id=?,assigned_at=CURRENT_TIMESTAMP WHERE id=?',[$recipientId,$id]);$assigned++;}
  } else {
   if ($url===null)problem('Подписка '.$name.' не найдена в базе. Добавьте её название вместе со ссылкой.',404);
   query($db,'INSERT INTO internet_subscriptions(name,url,user_id,assigned_at) VALUES (?,?,?,CURRENT_TIMESTAMP)',[$name,$url,$recipientId]);$id=(int)$db->lastInsertId();$assigned++;$created++;
  }
  // An administrator may explicitly restore a previously removed assignment.
  query($db,'DELETE FROM internet_subscription_revocations WHERE subscription_id=? AND user_id=?',[$id,$recipientId]);
  query($db,"UPDATE internet_subscription_claims SET status='approved',reviewed_at=CURRENT_TIMESTAMP WHERE user_id=? AND name=? AND status IN ('pending','revoked','rejected')",[$recipientId,$name]);
 }
 if ($assigned>0)query($db,'INSERT INTO internet_admin_events(event_key,type,user_id,title,body) VALUES (?,?,?,?,?)',[
  'direct-assignment:'.bin2hex(random_bytes(12)),'subscription',$recipientId,'Подписки назначены вручную',
  contactText($recipient)."\nНазвания: ".implode(', ',array_keys($wanted))."\nНазначил администратор #".$adminId."\nВыдано без привязки к заявке на оплату.",
 ]);
 return ['assigned'=>$assigned,'created'=>$created,'already_owned'=>$already,'names'=>array_keys($wanted),'user_id'=>$recipientId];
}

require_once __DIR__.'/router-keys.php';
