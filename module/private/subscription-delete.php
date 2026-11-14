<?php
declare(strict_types=1);
function deleteSubscription(PDO $db,array $body,int $adminId): array {
 $id=$body['id'] ?? null;$owner=$body['user_id'] ?? null;
 if(!is_int($id)||$id<1||!array_key_exists('user_id',$body)||($owner!==null&&(!is_int($owner)||$owner<1)))problem('Выберите подписку и её текущего владельца.');
 try {query($db,'SELECT order_id FROM internet_order_deleted_subscriptions LIMIT 0');}
 catch(PDOException $e){if($e->getCode()==='42S02')problem('Для удаления с сохранением истории оплат импортируйте private/migrations/008-service-status.sql.',503);throw $e;}
 $db->beginTransaction();
 try {
  $sub=query($db,'SELECT id,name,user_id FROM internet_subscriptions WHERE id=? FOR UPDATE',[$id])->fetch();if(!$sub)problem('Подписка уже удалена.',404);
  $current=$sub['user_id']===null?null:(int)$sub['user_id'];if($current!==$owner)problem('Владелец подписки изменился. Обновите список перед удалением.',409);
  deleteSubscriptionData($db,$sub,$adminId);
  $db->commit();return ['ok'=>true];
 }catch(PDOException $e){if($db->inTransaction())$db->rollBack();if(in_array($e->getCode(),['23000','40001'],true))problem('Связи подписки изменились. Обновите список и повторите удаление.',409);throw $e;}
}

function deleteSubscriptionData(PDO $db,array $sub,int $adminId): void {
 $id=(int)$sub['id'];$owner=$sub['user_id']===null?null:(int)$sub['user_id'];
  revokeResourceDevices($db,'subscription',$id);
  foreach(query($db,'SELECT order_id FROM internet_order_subscriptions WHERE subscription_id=? FOR UPDATE',[(int)$sub['id']])->fetchAll() as $issued)query($db,'INSERT INTO internet_order_deleted_subscriptions(order_id,name,deleted_by) VALUES (?,?,?) ON DUPLICATE KEY UPDATE deleted_at=CURRENT_TIMESTAMP,deleted_by=VALUES(deleted_by)',[$issued['order_id'],$sub['name'],$adminId]);
  query($db,'DELETE FROM internet_order_subscriptions WHERE subscription_id=?',[(int)$sub['id']]);
  query($db,'DELETE FROM internet_subscription_revocations WHERE subscription_id=?',[(int)$sub['id']]);
  query($db,'DELETE FROM internet_subscription_claims WHERE name=?',[$sub['name']]);
  query($db,'DELETE FROM internet_subscriptions WHERE id=?',[(int)$sub['id']]);resourceRemoved($db,'subscription',(int)$sub['id']);
  query($db,'INSERT INTO internet_admin_events(event_key,type,user_id,title,body,read_at) VALUES (?,?,?,?,?,CURRENT_TIMESTAMP)',[
   'subscription-delete:'.bin2hex(random_bytes(12)),'subscription',$owner ?? $adminId,'Подписка удалена из базы',
   'Подписка #'.$sub['id'].' · '.$sub['name']."\nУдалил администратор #".$adminId."\nСсылка удалена, история оплат сохранена."
  ]);
}
