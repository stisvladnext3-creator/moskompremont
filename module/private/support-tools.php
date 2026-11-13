<?php
declare(strict_types=1);
function requireReplyTemplates(PDO $db): void {
 try {query($db,'SELECT id FROM internet_reply_templates LIMIT 0');}catch(PDOException $e){if($e->getCode()==='42S02')problem('Импортируйте private/migrations/009-client-updates.sql для шаблонов ответов.',503);throw $e;}
}
function replyTemplates(PDO $db): array {requireReplyTemplates($db);return query($db,'SELECT id,title,body,updated_at FROM internet_reply_templates ORDER BY title,id LIMIT 500')->fetchAll();}
function saveReplyTemplate(PDO $db,array $body,int $adminId): array {
 requireReplyTemplates($db);$title=field($body,'title',400);$text=field($body,'body',16000,true);
 if(!$title||preg_match_all('/./us',$title)>100||preg_match_all('/./us',$text)<2||preg_match_all('/./us',$text)>4000)problem('Название — до 100 символов, текст ответа — от 2 до 4000 символов.');
 $id=$body['id'] ?? null;
 if($id!==null){if(!is_int($id)||$id<1)problem('Выберите шаблон.');if(!query($db,'SELECT id FROM internet_reply_templates WHERE id=?',[$id])->fetch())problem('Шаблон не найден.',404);query($db,'UPDATE internet_reply_templates SET title=?,body=?,updated_by=? WHERE id=?',[$title,$text,$adminId,$id]);}
 else {if((int)query($db,'SELECT COUNT(*) FROM internet_reply_templates')->fetchColumn()>=500)problem('Можно сохранить до 500 шаблонов.');query($db,'INSERT INTO internet_reply_templates(title,body,updated_by) VALUES (?,?,?)',[$title,$text,$adminId]);$id=(int)$db->lastInsertId();}
 return ['ok'=>true,'id'=>$id];
}
function clientDirectoryConditions(PDO $db,array $params): array {
 $clauses=[];if(authTablesReady($db))$clauses[]='NOT EXISTS (SELECT 1 FROM internet_auth_state a WHERE a.user_id=u.id AND a.deleted_at IS NOT NULL)';$args=[];$search=isset($params['q'])?field(['q'=>$params['q']],'q',254):'';
 if($search!==''){$clauses[]="(u.email LIKE ? OR u.first_name LIKE ? OR u.last_name LIKE ? OR CONCAT(u.first_name,' ',u.last_name) LIKE ? OR u.telegram LIKE ? OR u.id=?)";$like='%'.$search.'%';$args=[$like,$like,$like,$like,'%'.(phoneContact($search) ?? $search).'%',preg_match('/\A[0-9]+\z/D',$search)?(int)$search:0];}
 $keys=$params['router_keys'] ?? 'all';$terms=$params['terms'] ?? 'all';
 if(!in_array($keys,['all','with','without'],true)||!in_array($terms,['all','unspecified','fixed','lifetime'],true))problem('Проверьте фильтры клиентов.');
 if($keys!=='all')$clauses[]=($keys==='without'?'NOT ':'').'EXISTS (SELECT 1 FROM internet_router_keys k WHERE k.user_id=u.id)';
 if($terms==='unspecified')$clauses[]="EXISTS (SELECT 1 FROM internet_subscriptions s LEFT JOIN internet_subscription_terms t ON t.subscription_id=s.id WHERE s.user_id=u.id AND t.subscription_id IS NULL)";
 elseif($terms!=='all'){$clauses[]='EXISTS (SELECT 1 FROM internet_subscriptions s JOIN internet_subscription_terms t ON t.subscription_id=s.id WHERE s.user_id=u.id AND t.kind=?)';$args[]=$terms;}
 return [$clauses?' WHERE '.implode(' AND ',$clauses):'',$args];
}
function ticketStateLabel(string $status): string {return ['open'=>'В обработке','waiting'=>'Ожидает вашего ответа','closed'=>'Решено'][$status] ?? 'В обработке';}
