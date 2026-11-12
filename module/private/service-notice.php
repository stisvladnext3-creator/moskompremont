<?php
declare(strict_types=1);
function serviceNoticeRead(PDO $db): ?array {
 try {$row=query($db,"SELECT n.enabled,n.message,n.updated_at,COALESCE(s.level,'warning') AS level FROM internet_service_notice n LEFT JOIN internet_service_state s ON s.id=n.id WHERE n.id=1")->fetch();if($row)$row['status_ready']=true;return $row ?: null;}
 catch(PDOException $e){if($e->getCode()!=='42S02')throw $e;}
 try {$row=query($db,'SELECT enabled,message,updated_at FROM internet_service_notice WHERE id=1')->fetch();if($row){$row['level']='warning';$row['status_ready']=false;}return $row ?: null;}
 catch(PDOException $e){if($e->getCode()==='42S02')return null;throw $e;}
}
function serviceNoticePublic(PDO $db): array {
 $notice=serviceNoticeRead($db);
 if(!$notice || !(bool)$notice['enabled'])return ['enabled'=>false,'message'=>''];
 $level=in_array($notice['level'],['ok','warning','down'],true)?$notice['level']:'warning';
 return ['enabled'=>true,'message'=>$notice['message'],'level'=>$level];
}
function serviceNoticeAdmin(PDO $db): array {
 $notice=serviceNoticeRead($db);
 if(!$notice)problem('Для объявления импортируйте private/migrations/007-service-notice.sql в базу из private/config.php.',503);
 return ['enabled'=>(bool)$notice['enabled'],'message'=>$notice['message'],'level'=>$notice['level'],'status_ready'=>$notice['status_ready'],'updated_at'=>$notice['updated_at']];
}
function serviceNoticeSave(PDO $db,array $body,int $adminId): array {
 if(!isset($body['enabled']) || !is_bool($body['enabled']))problem('Укажите, показывать ли объявление.');
 $message=str_replace(["\r\n","\r"],"\n",field($body,'message',8000,true));
 if(preg_match_all('/./us',$message)>2000)problem('Текст объявления — до 2000 символов.');
 $old=serviceNoticeAdmin($db);$level=$body['level'] ?? $old['level'];
 if(!is_string($level)||!in_array($level,['ok','warning','down'],true))problem('Выберите зелёный, жёлтый или красный статус.');
 if(array_key_exists('level',$body)&&!$old['status_ready'])problem('Для цвета статуса импортируйте private/migrations/008-service-status.sql в базу из private/config.php.',503);
 // A status can be shown with its standard label and no additional message.
 $db->beginTransaction();query($db,'SELECT id FROM internet_service_notice WHERE id=1 FOR UPDATE');
 query($db,'UPDATE internet_service_notice SET enabled=?,message=?,updated_by=? WHERE id=1',[(int)$body['enabled'],$message,$adminId]);
 if($old['status_ready'])query($db,'INSERT INTO internet_service_state(id,level) VALUES (1,?) ON DUPLICATE KEY UPDATE level=VALUES(level)',[$level]);
 $db->commit();
 return serviceNoticeAdmin($db);
}
