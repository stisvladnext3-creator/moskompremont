<?php
declare(strict_types=1);

function orderRenewalsReady(PDO $db): void {
 try {query($db,'SELECT order_id FROM internet_order_renewals LIMIT 0');}
 catch(PDOException $e){if($e->getCode()==='42S02')problem('Продление пока недоступно. Обратитесь в поддержку.',503);throw $e;}
}
function orderRenewal(PDO $db,string $orderId,bool $lock=false): ?array {
 try {$row=query($db,'SELECT subscription_id,subscription_name,applied_at,previous_expires_at,new_expires_at FROM internet_order_renewals WHERE order_id=?'.($lock?' FOR UPDATE':''),[$orderId])->fetch();return $row ?: null;}
 catch(PDOException $e){if($e->getCode()==='42S02')return null;throw $e;}
}
function renewalSubscription(PDO $db,int $subscriptionId,int $userId): array {
 $sub=query($db,'SELECT id,name,user_id FROM internet_subscriptions WHERE id=? FOR UPDATE',[$subscriptionId])->fetch();
 if(!$sub||(int)$sub['user_id']!==$userId)problem('Подписка больше не закреплена за этим клиентом. Обновите список или обратитесь в поддержку.',409);
 $term=query($db,'SELECT kind,expires_at FROM internet_subscription_terms WHERE subscription_id=? FOR UPDATE',[$subscriptionId])->fetch();
 if($term&&$term['kind']==='lifetime')problem('Бессрочная подписка не требует продления.',409);
 return $sub+['current_expires_at'=>$term['expires_at'] ?? null];
}
function applyOrderRenewal(PDO $db,array $order,int $adminId): bool {
 // The caller holds the order lock. Dates, payment status, history and notice
 // commit together, so retries and concurrent confirmations cannot add time twice.
 $renewal=orderRenewal($db,$order['id'],true);if(!$renewal)return false;
 if($renewal['applied_at']!==null)problem('Продление по этой заявке уже выполнено.',409);
 if($order['plan']!=='monthly'||$renewal['subscription_id']===null)problem('Подписка для продления удалена. Свяжитесь с клиентом перед подтверждением оплаты.',409);
 activeAccount($db,(int)$order['user_id'],true);
 $sub=renewalSubscription($db,(int)$renewal['subscription_id'],(int)$order['user_id']);
 $months=orderMonths($db,$order['id']);extendSubscriptionTerm($db,(int)$sub['id'],$months,$adminId);
 $end=query($db,'SELECT expires_at FROM internet_subscription_terms WHERE subscription_id=?',[$sub['id']])->fetchColumn();
 query($db,'INSERT INTO internet_order_subscriptions(order_id,subscription_id,issued_by) VALUES (?,?,?)',[$order['id'],$sub['id'],$adminId]);
 query($db,'UPDATE internet_order_renewals SET applied_at=CURRENT_TIMESTAMP,previous_expires_at=?,new_expires_at=?,renewed_by=? WHERE order_id=?',[$sub['current_expires_at'],$end,$adminId,$order['id']]);
 $local=(new DateTimeImmutable((string)$end,new DateTimeZone('UTC')))->setTimezone(new DateTimeZone('Europe/Moscow'))->format('d.m.Y H:i');
 notifyClient($db,(int)$order['user_id'],'subscription-renewed:'.$order['id'],'subscription_renewed','Подписка продлена','Оплата подтверждена. Подписка «'.$sub['name'].'» продлена на '.$months.' мес., до '.$local.'.','subscriptions');
 return true;
}
