<?php
declare(strict_types=1);
function routerKeyFormats(): array {return ['connection'=>'Ключ подключения','wireguard'=>'WireGuard','openvpn'=>'OpenVPN','text'=>'Текстовый ключ / конфигурация'];}
function routerKeyInput(array $body): array {
 $label=field($body,'label',400);if ($label==='' || preg_match_all('/./us',$label)>100)problem('Укажите название ключа до 100 символов, например «Роутер дома».');
 $format=field($body,'key_format',20);if (!isset(routerKeyFormats()[$format]))problem('Выберите формат ключа.');
 $value=$body['key_value'] ?? null;
 if (!is_string($value) || trim($value)==='' || strlen($value)>65536 || !preg_match('//u',$value) || preg_match('/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/',$value))problem('Вставьте текст ключа или конфигурации размером до 64 КБ.');
 if ($format==='connection' && preg_match('/[\r\n]/',trim($value)))problem('Для многострочной конфигурации выберите WireGuard, OpenVPN или текстовый формат.');
 // Only the fingerprint is normalized; the original material is stored unchanged.
 $canonical=str_replace(["\r\n","\r"],"\n",trim($value));
 return ['label'=>$label,'key_format'=>$format,'key_value'=>$value,'key_hash'=>hash('sha256',$canonical)];
}
function authorizedRouterKey(PDO $db,int $id,int $userId,bool $admin): array {
 if ($id<1)problem('Ключ не найден.',404);
 $key=query($db,'SELECT id,label,key_format,key_value,user_id,assigned_at FROM internet_router_keys WHERE id=? AND (user_id=? OR ?=1)',[$id,$userId,(int)$admin])->fetch();
 if (!$key)problem('Ключ не найден.',404);return $key;
}
function routerKeyJournal(PDO $db,int $recipientId,int $adminId,int $keyId,string $label,bool $removed=false,bool $deleted=false): void {
 query($db,'INSERT INTO internet_admin_events(event_key,type,user_id,title,body,read_at) VALUES (?,?,?,?,?,CURRENT_TIMESTAMP)',[
  'router-key:'.bin2hex(random_bytes(12)),'subscription',$recipientId,$deleted?'Ключ роутера удалён из базы':($removed?'Снята привязка ключа роутера':'Ключ для роутера выдан'),
  'Ключ #'.$keyId.' · '.$label."\nКлиент #".$recipientId."\nАдминистратор #".$adminId."\nСодержимое ключа в журнал не записывается.",
 ]);
}
