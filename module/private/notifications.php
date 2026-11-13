<?php
declare(strict_types=1);
function mailSettings(array $config): array {
 return array_replace([
  'enabled'=>false,'transport'=>'smtp','from'=>'','from_name'=>'МосКомпРемонт',
  'smtp_host'=>'smtp.yandex.ru','smtp_port'=>465,'smtp_encryption'=>'ssl',
  'smtp_auth'=>true,'smtp_user'=>'','smtp_password'=>'',
 ],is_array($config['mail'] ?? null)?$config['mail']:[]);
}
function mailIssues(array $config): array {
 $m=mailSettings($config);$issues=[];
 if(!$m['enabled'])$issues[]='Отправка писем выключена: mail.enabled должен быть true.';
 if(!filter_var($m['from'],FILTER_VALIDATE_EMAIL))$issues[]='Не заполнен корректный адрес отправителя mail.from.';
 if($m['transport']==='mail'){if(!function_exists('mail'))$issues[]='Функция PHP mail недоступна на хостинге.';return $issues;}
 if($m['transport']!=='smtp'){$issues[]='Укажите mail.transport: smtp или mail.';return $issues;}
 if(!is_string($m['smtp_host'])||trim($m['smtp_host'])==='')$issues[]='Не заполнен SMTP-сервер.';
 if(!is_numeric($m['smtp_port'])||(int)$m['smtp_port']<1||(int)$m['smtp_port']>65535)$issues[]='Проверьте порт SMTP.';
 if(!in_array($m['smtp_encryption'],['ssl','tls','none'],true))$issues[]='Проверьте шифрование SMTP.';
 if($m['smtp_encryption']!=='none'&&!extension_loaded('openssl'))$issues[]='На PHP не включено расширение OpenSSL.';
 if($m['smtp_auth']&&(!is_string($m['smtp_user'])||trim($m['smtp_user'])===''))$issues[]='Не заполнен логин SMTP.';
 if($m['smtp_auth']&&(!is_string($m['smtp_password'])||trim($m['smtp_password'])===''||preg_match('/СЮДА_|YOUR_|ПАРОЛЬ_ПРИЛОЖЕНИЯ/iu',$m['smtp_password'])))$issues[]='Укажите настоящий пароль приложения почты в mail.smtp_password.';
 return $issues;
}
function mailReady(array $config): bool {return !mailIssues($config);}
function mailErrorMessage(Throwable $error): string {
 $text=strtolower($error->getMessage());
 if(preg_match('/authenticat|password|535|login failed/',$text))return 'SMTP_AUTH: почтовый сервер отклонил вход. Проверьте логин и пароль приложения Яндекса.';
 if(preg_match('/certificate|crypto|tls|ssl/',$text))return 'SMTP_TLS: не удалось установить защищённое соединение. Проверьте порт, шифрование и сертификаты PHP.';
 if(preg_match('/connect|timed out|resolve|getaddrinfo|network|refused/',$text))return 'SMTP_CONNECT: не удалось подключиться к SMTP. Проверьте адрес, порт и разрешение исходящих соединений на хостинге.';
 if(preg_match('/recipient|rcpt|address|sender|550|553/',$text))return 'SMTP_ADDRESS: проверьте отправителя и получателя. Для Яндекса отправитель обычно совпадает с логином SMTP.';
 return 'SMTP_SEND: сервер не принял письмо. Проверьте настройки отправителя и ограничения почты.';
}
function mailHeartbeat(PDO $db,int $sent,int $failed): void {
 try{$db->prepare('INSERT INTO internet_monitor(id,mail_checked_at,mail_last_sent,mail_last_failed) VALUES (1,UTC_TIMESTAMP(),?,?) ON DUPLICATE KEY UPDATE mail_checked_at=UTC_TIMESTAMP(),mail_last_sent=VALUES(mail_last_sent),mail_last_failed=VALUES(mail_last_failed)')->execute([$sent,$failed]);}catch(PDOException $e){if($e->getCode()!=='42S02'&&$e->getCode()!=='42S22')throw $e;}
}
function mailDiagnostics(PDO $db,array $config): array {
 $worker=null;try{$worker=$db->query('SELECT mail_checked_at,mail_last_sent,mail_last_failed FROM internet_monitor WHERE id=1')->fetch();}catch(PDOException $e){if($e->getCode()!=='42S02'&&$e->getCode()!=='42S22')throw $e;}
 $rows=$db->query("SELECT id,status,attempts,last_error,next_attempt_at,sent_at FROM internet_mail_outbox ORDER BY id DESC LIMIT 20")->fetchAll();
 return ['ready'=>mailReady($config),'issues'=>mailIssues($config),'worker'=>$worker ?: null,'messages'=>$rows];
}
function notifyAdmin(PDO $db,array $config,string $key,string $type,int $userId,string $title,string $body,?string $orderId=null,bool $email=true): void {
 $q=$db->prepare('INSERT INTO internet_admin_events(event_key,type,user_id,order_id,title,body) VALUES (?,?,?,?,?,?)');
 $q->execute([$key,$type,$userId,$orderId,$title,$body]);$eventId=(int)$db->lastInsertId();
 if (!$email)return;
 if(function_exists('notifyAdminOnSite'))notifyAdminOnSite($db,$config,$key,$type,$userId,$title,$body,$orderId);
 $recipient=$config['admin_email'] ?? 'sania.iwannikow@yandex.ru';
 if (!is_string($recipient) || !filter_var($recipient,FILTER_VALIDATE_EMAIL)) throw new RuntimeException('Invalid notification recipient');
 $base=rtrim($config['origin'],'/').'/';
 if (!empty($config['app_path'])) $base.=trim($config['app_path'],'/').'/';
 $emailBody=$body."\n\nОткрыть кабинет администратора: ".$base.'account.html';
 $db->prepare('INSERT INTO internet_mail_outbox(event_id,recipient,subject,body) VALUES (?,?,?,?)')->execute([$eventId,$recipient,$title,$emailBody]);
 $GLOBALS['internet_mail_queued']=true;
}
function sendPendingMail(PDO $db,array $config,int $limit=3): array {
 if (!mailReady($config)){mailHeartbeat($db,0,0);return ['sent'=>0,'failed'=>0,'ready'=>false,'issues'=>mailIssues($config)];}
 foreach (['Exception','SMTP','PHPMailer'] as $class) require_once __DIR__.'/vendor/PHPMailer/src/'.$class.'.php';
 $settings=mailSettings($config);$sent=0;$failed=0;$errors=[];
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
   $reason=mailErrorMessage($e);$errors[]=$reason;$db->prepare('UPDATE internet_mail_outbox SET status=?,next_attempt_at=FROM_UNIXTIME(?),lease_until=NULL,last_error=? WHERE id=?')->execute([$status,time()+$delay,$reason,$item['id']]);$failed++;
  }
 }
 mailHeartbeat($db,$sent,$failed);return ['sent'=>$sent,'failed'=>$failed,'ready'=>true,'errors'=>array_values(array_unique($errors))];
}
