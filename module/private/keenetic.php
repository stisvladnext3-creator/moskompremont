<?php
declare(strict_types=1);
function keeneticLimits(): array {
 return ['max_files'=>3,'max_bytes'=>5*1024*1024,'extensions'=>['conf','ovpn','cfg','config','json','txt','yaml','yml','zip']];
}
function keeneticReady(PDO $db): bool {
 try {query($db,'SELECT a.user_id,f.id FROM internet_keenetic_access a LEFT JOIN internet_keenetic_files f ON 1=0 LIMIT 0');return true;}
 catch(PDOException $e){if($e->getCode()==='42S02')return false;throw $e;}
}
function keeneticRequireReady(PDO $db): void {
 if(!keeneticReady($db))problem('Для Keenetic импортируйте private/migrations/006-keenetic.sql в базу из private/config.php.',503);
}
function keeneticEnabled(PDO $db,int $userId): bool {
 try {return (bool)query($db,'SELECT enabled FROM internet_keenetic_access WHERE user_id=?',[$userId])->fetchColumn();}
 catch(PDOException $e){if($e->getCode()==='42S02')return false;throw $e;}
}
function keeneticFiles(PDO $db,int $userId): array {
 // List responses never contain file bytes, VPN credentials or public file URLs.
 return query($db,'SELECT id,user_id,file_name,byte_size,created_at FROM internet_keenetic_files WHERE user_id=? ORDER BY created_at DESC,id DESC',[$userId])->fetchAll();
}
function keeneticRecipient(PDO $db,mixed $value,bool $lock=false): array {
 if(is_string($value) && preg_match('/\A[1-9][0-9]{0,17}\z/D',$value))$value=(int)$value;
 if(!is_int($value)||$value<1)problem('Выберите клиента для Keenetic.');
 $row=query($db,'SELECT id,first_name,last_name,email,telegram FROM internet_users WHERE id=?'.($lock?' FOR UPDATE':''),[$value])->fetch();
 if(!$row||authState($db,(int)$row['id'])['deleted_at']!==null)problem('Клиент не найден.',404);return $row;
}
function keeneticFileId(mixed $id): string {
 if(!is_string($id)||!preg_match('/\A[a-f0-9]{32}\z/D',$id))problem('Файл не найден.',404);return $id;
}
function uploadedKeeneticFiles(array $uploads): array {
 $parts=$uploads['files'] ?? null;$limits=keeneticLimits();
 if(!is_array($parts)||!isset($parts['error'])||!is_array($parts['error']))problem('Выберите файлы конфигурации Keenetic.');
 if(count($parts['error'])>$limits['max_files'])problem('За одну загрузку можно добавить до 3 файлов.');
 $files=[];
 foreach($parts['error'] as $index=>$error){
  if($error===UPLOAD_ERR_NO_FILE)continue;
  if(in_array($error,[UPLOAD_ERR_INI_SIZE,UPLOAD_ERR_FORM_SIZE],true))problem('Один файл — до 5 МБ. Проверьте upload_max_filesize на хостинге.',413);
  if($error!==UPLOAD_ERR_OK)problem('Не удалось загрузить файл. Попробуйте ещё раз.');
  $path=$parts['tmp_name'][$index] ?? null;$original=$parts['name'][$index] ?? null;
  if(!is_string($path)||!is_string($original)||!is_uploaded_file($path))problem('Некорректная загрузка файла.');
  if(!preg_match('//u',$original)||strlen($original)>720||preg_match('/[\x00-\x1f\x7f]/u',$original))problem('Переименуйте файл: название должно быть в UTF-8 без служебных символов.');
  $name=basename(str_replace('\\','/',$original));
  if(!$name||preg_match_all('/./us',$name)>180||str_starts_with($name,'.'))problem('Название файла — до 180 символов, без точки в начале.');
  $extension=strtolower(pathinfo($name,PATHINFO_EXTENSION));
  if(!in_array($extension,$limits['extensions'],true))problem('Поддерживаются файлы .conf, .ovpn, .cfg, .config, .json, .txt, .yaml, .yml и .zip.',415);
  $size=filesize($path);if($size===false||$size<1||$size>$limits['max_bytes'])problem('Файл должен быть непустым и не больше 5 МБ.',413);
  // These are private opaque downloads: never execute, render or extract uploads.
  $files[]=['path'=>$path,'file_name'=>$name,'byte_size'=>$size];
 }
 if(!$files)problem('Выберите хотя бы один файл конфигурации.');return $files;
}
function saveKeeneticFiles(PDO $db,int $recipientId,int $adminId,array $files): array {
 $ids=[];
 foreach($files as $file){
  $bytes=file_get_contents($file['path']);if($bytes===false||strlen($bytes)!==$file['byte_size'])throw new RuntimeException('Cannot read Keenetic upload');
  $id=bin2hex(random_bytes(16));$q=$db->prepare('INSERT INTO internet_keenetic_files(id,user_id,file_name,byte_size,content,uploaded_by) VALUES (?,?,?,?,?,?)');
  $q->bindValue(1,$id);$q->bindValue(2,$recipientId,PDO::PARAM_INT);$q->bindValue(3,$file['file_name']);$q->bindValue(4,$file['byte_size'],PDO::PARAM_INT);$q->bindValue(5,$bytes,PDO::PARAM_LOB);$q->bindValue(6,$adminId,PDO::PARAM_INT);$q->execute();unset($bytes);$ids[]=$id;
 }
 return $ids;
}
