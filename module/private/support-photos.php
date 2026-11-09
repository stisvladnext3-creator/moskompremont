<?php
declare(strict_types=1);
function photoLimits(): array {return ['max_files'=>3,'max_bytes'=>5*1024*1024,'formats'=>['JPEG','PNG','WebP']];}
function uploadedPhotos(array $uploads): array {
 if (!$uploads)return [];
 $parts=$uploads['photos'] ?? null;if ($parts===null)return [];
 if (!is_array($parts) || !isset($parts['error']) || !is_array($parts['error']))problem('Некорректная загрузка фотографий.');
 if (count($parts['error'])>3)problem('К одному сообщению можно прикрепить до 3 фотографий.');
 $photos=[];
 foreach($parts['error'] as $index=>$error) {
  if ($error===UPLOAD_ERR_NO_FILE)continue;
  if (in_array($error,[UPLOAD_ERR_INI_SIZE,UPLOAD_ERR_FORM_SIZE],true))problem('Фото превышает лимит загрузки. Допускается до 5 МБ; проверьте также upload_max_filesize на хостинге.',413);
  if ($error!==UPLOAD_ERR_OK)problem('Не удалось загрузить фото. Попробуйте ещё раз.');
  $path=$parts['tmp_name'][$index] ?? null;$original=$parts['name'][$index] ?? null;
  if (!is_string($path) || !is_string($original) || !is_uploaded_file($path))problem('Некорректная загрузка фотографии.');
  $size=filesize($path);if ($size===false || $size<1 || $size>5*1024*1024)problem('Размер одного фото — до 5 МБ.',413);
  if (!class_exists('finfo'))problem('Для загрузки фото включите расширение PHP fileinfo в панели хостинга.',503);
  $mime=(new finfo(FILEINFO_MIME_TYPE))->file($path);
  $extensions=['image/jpeg'=>['jpg','jpeg'],'image/png'=>['png'],'image/webp'=>['webp']];
  $extension=strtolower(pathinfo(str_replace('\\','/',$original),PATHINFO_EXTENSION));
  if (!isset($extensions[$mime]) || !in_array($extension,$extensions[$mime],true))problem('Прикрепите фотографию в формате JPEG, PNG или WebP.',415);
  $info=@getimagesize($path);
  if (!$info || ($info['mime'] ?? '')!==$mime || $info[0]<1 || $info[1]<1 || $info[0]>12000 || $info[1]>12000 || $info[0]*$info[1]>25000000)problem('Не удалось прочитать изображение или оно слишком большое. Допускается до 25 мегапикселей.');
  $name=preg_replace('/[\x00-\x1f\x7f]/','',basename(str_replace('\\','/',$original)));
  if (!preg_match('//u',$name) || strlen($name)>180)$name='photo.'.$extensions[$mime][0];
  $photos[]=['path'=>$path,'mime'=>$mime,'name'=>$name,'size'=>$size,'width'=>(int)$info[0],'height'=>(int)$info[1]];
 }
 return $photos;
}
function messageText(array $body,array $photos): string {
 $text=field($body,'message',16000,true);$length=preg_match_all('/./us',$text);
 if ($length>4000 || (!$photos && $length<2))problem('Напишите сообщение (от 2 до 4000 символов) или прикрепите фото.');return $text;
}
function saveMessagePhotos(PDO $db,int $messageId,int $uploaderId,array $photos): void {
 foreach($photos as $photo) {
  $bytes=file_get_contents($photo['path']);if ($bytes===false || strlen($bytes)!==$photo['size'])throw new RuntimeException('Cannot read uploaded photo');
  $q=$db->prepare('INSERT INTO internet_message_photos(id,message_id,uploader_id,mime,file_name,byte_size,width,height,content) VALUES (?,?,?,?,?,?,?,?,?)');
  foreach([bin2hex(random_bytes(16)),$messageId,$uploaderId,$photo['mime'],$photo['name'],$photo['size'],$photo['width'],$photo['height']] as $i=>$value)$q->bindValue($i+1,$value,is_int($value)?PDO::PARAM_INT:PDO::PARAM_STR);
  $q->bindValue(9,$bytes,PDO::PARAM_LOB);$q->execute();unset($bytes);
 }
}
function withMessagePhotos(PDO $db,array $messages): array {
 if (!$messages)return [];$ids=array_column($messages,'id');$placeholders=implode(',',array_fill(0,count($ids),'?'));
 $rows=query($db,'SELECT id,message_id,mime,file_name,byte_size,width,height FROM internet_message_photos WHERE message_id IN ('.$placeholders.') ORDER BY created_at,id',$ids)->fetchAll();$byMessage=[];
 foreach($rows as $row){$row['url']='api/photo.php?id='.$row['id'];$byMessage[$row['message_id']][]=$row;}
 foreach($messages as &$message)$message['photos']=$byMessage[$message['id']] ?? [];unset($message);return $messages;
}
function photoMailNote(array $photos): string {return $photos?"\n\nПрикреплено фото: ".count($photos).'. Посмотреть можно в переписке в кабинете.':'';}
