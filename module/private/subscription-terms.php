<?php
declare(strict_types=1);
function subscriptionTermsReady(PDO $db): void {
 try {query($db,'SELECT subscription_id FROM internet_subscription_terms LIMIT 0');query($db,'SELECT order_id FROM internet_order_terms LIMIT 0');}
 catch(PDOException $e){if($e->getCode()==='42S02')problem('Для сроков подписок импортируйте private/migrations/008-service-status.sql.',503);throw $e;}
}
function orderMonths(PDO $db,string $orderId): int {
 try {$months=query($db,'SELECT months FROM internet_order_terms WHERE order_id=?',[$orderId])->fetchColumn();return $months===false?1:max(1,min(12,(int)$months));}
 catch(PDOException $e){if($e->getCode()==='42S02')return 1;throw $e;}
}
function withSubscriptionTerms(PDO $db,array $subscriptions,bool $revealExpired=false): array {
 if(!$subscriptions)return [];$ids=array_map('intval',array_column($subscriptions,'id'));$marks=implode(',',array_fill(0,count($ids),'?'));$terms=[];
 try {foreach(query($db,"SELECT subscription_id,kind,expires_at FROM internet_subscription_terms WHERE subscription_id IN ($marks)",$ids)->fetchAll() as $term)$terms[(int)$term['subscription_id']]=$term;}
 catch(PDOException $e){if($e->getCode()!=='42S02')throw $e;}
 $now=time();foreach($subscriptions as &$sub){$term=$terms[(int)$sub['id']] ?? null;$sub['term_kind']=$term['kind'] ?? 'unspecified';$sub['expires_at']=$term['expires_at'] ?? null;
  $end=$sub['term_kind']==='fixed'&&$sub['expires_at']!==null?strtotime($sub['expires_at'].' UTC'):false;
  $sub['remaining_seconds']=$end!==false?max(0,$end-$now):null;$sub['is_expired']=$end!==false&&$end<=$now;
  if($sub['is_expired']&&!$revealExpired)unset($sub['url']);
 }unset($sub);return $subscriptions;
}
function termMonthsInput(mixed $value): int {if(!is_int($value)||$value<1||$value>12)problem('Выберите срок от 1 до 12 месяцев.');return $value;}
function addSubscriptionMonths(DateTimeImmutable $base,int $months,int $anchorDay): DateTimeImmutable {
 // Calendar months follow Moscow time. Clamp the day for February and restore it on the next extension.
 $local=$base->setTimezone(new DateTimeZone('Europe/Moscow'));$target=$local->modify('first day of this month')->modify('+'.$months.' months');
 return $target->setDate((int)$target->format('Y'),(int)$target->format('m'),min($anchorDay,(int)$target->format('t')))->setTime((int)$local->format('H'),(int)$local->format('i'),(int)$local->format('s'))->setTimezone(new DateTimeZone('UTC'));
}
function writeSubscriptionTerm(PDO $db,int $id,string $kind,?DateTimeImmutable $end,?int $anchor,int $adminId): void {
 query($db,'INSERT INTO internet_subscription_terms(subscription_id,kind,expires_at,anchor_day,updated_by) VALUES (?,?,?,?,?) ON DUPLICATE KEY UPDATE kind=VALUES(kind),expires_at=VALUES(expires_at),anchor_day=VALUES(anchor_day),updated_by=VALUES(updated_by)',[$id,$kind,$end?->setTimezone(new DateTimeZone('UTC'))->format('Y-m-d H:i:s'),$anchor,$adminId]);
}
function extendSubscriptionTerm(PDO $db,int $id,int $months,int $adminId,bool $preserveLifetime=false): void {
 $old=query($db,'SELECT kind,expires_at,anchor_day FROM internet_subscription_terms WHERE subscription_id=? FOR UPDATE',[$id])->fetch();
 if($old&&$old['kind']==='lifetime'){if($preserveLifetime)return;problem('Подписка бессрочная. Чтобы ограничить её, выберите конкретную дату окончания.',409);}
 $now=new DateTimeImmutable('now',new DateTimeZone('UTC'));$oldEnd=$old&&$old['kind']==='fixed'&&$old['expires_at']?new DateTimeImmutable($old['expires_at'],new DateTimeZone('UTC')):null;
 $base=$oldEnd&&$oldEnd>$now?$oldEnd:$now;$anchor=$oldEnd&&$oldEnd>$now?(int)($old['anchor_day'] ?: $base->setTimezone(new DateTimeZone('Europe/Moscow'))->format('j')):(int)$now->setTimezone(new DateTimeZone('Europe/Moscow'))->format('j');
 writeSubscriptionTerm($db,$id,'fixed',addSubscriptionMonths($base,$months,$anchor),$anchor,$adminId);
}
function issueOrderSubscriptionTerm(PDO $db,int $id,array $order,int $adminId,bool $alreadyIssued): void {
 // Existing mapping means this order has already granted this link's term, even after unassignment.
 if($alreadyIssued)return;subscriptionTermsReady($db);
 if($order['plan']==='lifetime')writeSubscriptionTerm($db,$id,'lifetime',null,null,$adminId);
 else extendSubscriptionTerm($db,$id,orderMonths($db,$order['id']),$adminId,true);
}
function saveSubscriptionTerm(PDO $db,array $body,int $adminId): array {
 subscriptionTermsReady($db);$id=$body['id'] ?? null;$owner=$body['user_id'] ?? null;$mode=field($body,'mode',20);
 if(!is_int($id)||$id<1||!array_key_exists('user_id',$body)||($owner!==null&&(!is_int($owner)||$owner<1)))problem('Выберите подписку и текущего владельца.');
 if(!in_array($mode,['lifetime','until','extend'],true))problem('Выберите бессрочную подписку, дату окончания или продление.');
 $end=null;$months=null;
 if($mode==='until'){$raw=field($body,'expires_at',30);$end=DateTimeImmutable::createFromFormat('!Y-m-d\TH:i:s\Z',$raw,new DateTimeZone('UTC'));$errors=DateTimeImmutable::getLastErrors();if(!$end||($errors&&($errors['warning_count']||$errors['error_count']))||$end->format('Y-m-d\TH:i:s\Z')!==$raw||(int)$end->format('Y')<2000||(int)$end->format('Y')>2100)problem('Укажите корректную дату и время окончания.');}
 if($mode==='extend')$months=termMonthsInput($body['months'] ?? null);
 $db->beginTransaction();$sub=query($db,'SELECT id,name,user_id FROM internet_subscriptions WHERE id=? FOR UPDATE',[$id])->fetch();if(!$sub)problem('Подписка не найдена.',404);
 $current=$sub['user_id']===null?null:(int)$sub['user_id'];if($current!==$owner)problem('Владелец изменился. Обновите список перед настройкой срока.',409);
 if($mode==='lifetime')writeSubscriptionTerm($db,$id,'lifetime',null,null,$adminId);
 elseif($mode==='until')writeSubscriptionTerm($db,$id,'fixed',$end,(int)$end->setTimezone(new DateTimeZone('Europe/Moscow'))->format('j'),$adminId);
 else extendSubscriptionTerm($db,$id,$months,$adminId);
 $db->commit();return ['ok'=>true,'subscription'=>withSubscriptionTerms($db,[$sub],true)[0]];
}
