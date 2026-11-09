<?php
declare(strict_types=1);
function mailSettings(array $config): array {
 return array_replace([
  'enabled'=>false,'transport'=>'smtp','from'=>'','from_name'=>'МосКомпРемонт',
  'smtp_host'=>'smtp.yandex.ru','smtp_port'=>465,'smtp_encryption'=>'ssl',
  'smtp_auth'=>true,'smtp_user'=>'','smtp_password'=>'',
 ],is_array($config['mail'] ?? null)?$config['mail']:[]);
}
function mailReady(array $config): bool {
 $m=mailSettings($config);
 if (!$m['enabled'] || !filter_var($m['from'],FILTER_VALIDATE_EMAIL)) return false;
 if ($m['transport']==='mail') return function_exists('mail');
 return $m['transport']==='smtp' && is_string($m['smtp_host']) && $m['smtp_host']!=='' && (!$m['smtp_auth'] || ($m['smtp_user']!=='' && $m['smtp_password']!==''));
}
function notifyAdmin(PDO $db,array $config,string $key,string $type,int $userId,string $title,string $body,?string $orderId=null): void {
 $q=$db->prepare('INSERT INTO internet_admin_events(event_key,type,user_id,order_id,title,body) VALUES (?,?,?,?,?,?)');
 $q->execute([$key,$type,$userId,$orderId,$title,$body]);$eventId=(int)$db->lastInsertId();
 $recipient=$config['admin_email'] ?? 'sania.iwannikow@yandex.ru';
 if (!is_string($recipient) || !filter_var($recipient,FILTER_VALIDATE_EMAIL)) throw new RuntimeException('Invalid notification recipient');
 $base=rtrim($config['origin'],'/').'/';
 if (!empty($config['app_path'])) $base.=trim($config['app_path'],'/').'/';
 $emailBody=$body."\n\nОткрыть кабинет администратора: ".$base.'account.html';
 $db->prepare('INSERT INTO internet_mail_outbox(event_id,recipient,subject,body) VALUES (?,?,?,?)')->execute([$eventId,$recipient,$title,$emailBody]);
 $GLOBALS['internet_mail_queued']=true;
}
function sendPendingMail(PDO $db,array $config,int $limit=3): array {
 if (!mailReady($config)) return ['sent'=>0,'failed'=>0,'ready'=>false];
 foreach (['Exception','SMTP','PHPMailer'] as $class) require_once __DIR__.'/vendor/PHPMailer/src/'.$class.'.php';
 $settings=mailSettings($config);$sent=0;$failed=0;
 $db->exec("UPDATE internet_mail_outbox SET status='failed',lease_until=NULL,last_error='Отправка прервана после 10 попыток.' WHERE status='sending' AND lease_until<CURRENT_TIMESTAMP AND attempts>=10");
 for ($i=0;$i<max(1,min($limit,20));$i++) {
  $db->beginTransaction();
  $q=$db->query("SELECT * FROM internet_mail_outbox WHERE ((status IN ('pending','retry') AND next_attempt_at<=CURRENT_TIMESTAMP) OR (status='sending' AND lease_until<CURRENT_TIMESTAMP)) AND attempts<10 ORDER BY id LIMIT 1 FOR UPDATE");
  $item=$q->fetch();if (!$item) {$db->commit();break;}
  $db->prepare("UPDATE internet_mail_outbox SET status='sending',attempts=attempts+1,lease_until=DATE_ADD(CURRENT_TIMESTAMP,INTERVAL 2 MINUTE) WHERE id=?")->execute([$item['id']]);$db->commit();
  try {
   $mailer=new \PHPMailer\PHPMailer\PHPMailer(true);$mailer->CharSet='UTF-8';$mailer->Timeout=10;$mailer->Timelimit=15;$mailer->SMTPDebug=0;
   if ($settings['transport']==='smtp') {
    $mailer->isSMTP();$mailer->Host=$settings['smtp_host'];$mailer->Port=(int)$settings['smtp_port'];
    $mailer->SMTPAuth=(bool)$settings['smtp_auth'];$mailer->Username=$settings['smtp_user'];$mailer->Password=$settings['smtp_password'];
    $mailer->SMTPSecure=match($settings['smtp_encryption']) {'ssl'=>'ssl','tls'=>'tls','none'=>'',default=>throw new RuntimeException('Invalid encryption')};
    $mailer->SMTPAutoTLS=$settings['smtp_encryption']!=='none';
   } else {$mailer->isMail();}
   $mailer->setFrom($settings['from'],$settings['from_name']);$mailer->addAddress($item['recipient']);$mailer->Subject=$item['subject'];$mailer->Body=$item['body'];$mailer->isHTML(false);
   // Stable Message-ID lets recipients recognize retries after an interrupted SMTP exchange.
   $domain=parse_url($config['origin'],PHP_URL_HOST) ?: 'localhost';$mailer->MessageID='<internet-event-'.$item['event_id'].'@'.$domain.'>';
   $mailer->send();
   $db->prepare("UPDATE internet_mail_outbox SET status='sent',sent_at=CURRENT_TIMESTAMP,lease_until=NULL,last_error=NULL WHERE id=?")->execute([$item['id']]);$sent++;
  } catch (Throwable $e) {
   $attempts=(int)$item['attempts']+1;$delay=min(3600,60*(2**min($attempts-1,6)));
   $status=$attempts>=10?'failed':'retry';
   // SMTP diagnostics can contain credentials or message content: do not expose them.
   $db->prepare('UPDATE internet_mail_outbox SET status=?,next_attempt_at=FROM_UNIXTIME(?),lease_until=NULL,last_error=? WHERE id=?')->execute([$status,time()+$delay,'Ошибка отправки: проверьте настройки SMTP или почту хостинга.',$item['id']]);$failed++;
  }
 }
 return ['sent'=>$sent,'failed'=>$failed,'ready'=>true];
}
