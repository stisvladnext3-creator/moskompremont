<?php
declare(strict_types=1);
function serviceNoticeRead(PDO $db): ?array {
 try {$row=query($db,'SELECT enabled,message,updated_at FROM internet_service_notice WHERE id=1')->fetch();return $row ?: null;}
 catch(PDOException $e){if($e->getCode()==='42S02')return null;throw $e;}
}
function serviceNoticePublic(PDO $db): array {
 $notice=serviceNoticeRead($db);
 if(!$notice || !(bool)$notice['enabled'] || trim($notice['message'])==='')return ['enabled'=>false,'message'=>''];
 return ['enabled'=>true,'message'=>$notice['message']];
}
function serviceNoticeAdmin(PDO $db): array {
 $notice=serviceNoticeRead($db);
 if(!$notice)problem('Для объявления импортируйте private/migrations/007-service-notice.sql в базу из private/config.php.',503);
 return ['enabled'=>(bool)$notice['enabled'],'message'=>$notice['message'],'updated_at'=>$notice['updated_at']];
}
function serviceNoticeSave(PDO $db,array $body,int $adminId): array {
 if(!isset($body['enabled']) || !is_bool($body['enabled']))problem('Укажите, показывать ли объявление.');
 $message=str_replace(["\r\n","\r"],"\n",field($body,'message',8000,true));
 if(preg_match_all('/./us',$message)>2000)problem('Текст объявления — до 2000 символов.');
 if($body['enabled'] && $message==='')problem('Введите текст объявления или снимите галочку показа.');
 serviceNoticeAdmin($db);
 query($db,'UPDATE internet_service_notice SET enabled=?,message=?,updated_by=? WHERE id=1',[(int)$body['enabled'],$message,$adminId]);
 return serviceNoticeAdmin($db);
}
