<?php
declare(strict_types=1);

// A device may request this representation. Ordinary router-agent clients keep
// receiving the original URI, and no server connection is made to its host.
function xkeenOutbound(string $uri): array {
 if(strlen($uri)>8192||preg_match('/[\x00-\x20\x7f]/',$uri))throw new InvalidArgumentException('Нужен одиночный ключ VLESS.');
 $u=parse_url($uri);
 if(!$u||strtolower($u['scheme'] ?? '')!=='vless'||isset($u['pass'])||!isset($u['user'],$u['host'],$u['port'])||!in_array($u['path'] ?? '',['','/'],true))throw new InvalidArgumentException('Для XKeen нужен ключ vless:// с адресом и портом.');
 $id=rawurldecode($u['user']);$host=trim($u['host'],'[]');$port=$u['port'];
 if(!preg_match('/\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z/iD',$id)||!xkeenHost($host)||$port<1||$port>65535)throw new InvalidArgumentException('Некорректный адрес или UUID ключа VLESS.');
 $q=[];
 foreach(explode('&',$u['query'] ?? '') as $pair){
  if($pair==='')continue;
  [$name,$value]=array_pad(explode('=',$pair,2),2,'');$name=rawurldecode($name);$value=rawurldecode($value);
  if(isset($q[$name])||preg_match('/[\x00-\x1f\x7f]/',$name.$value))throw new InvalidArgumentException('Некорректные параметры VLESS.');
  $q[$name]=$value;
 }
 $known=['encryption','type','security','flow','sni','pbk','sid','spx','fp','alpn','allowInsecure','serviceName','mode','authority','host','path','headerType','extra','support-x25519mlkem768'];
 foreach($q as $name=>$value)if($value!==''&&!in_array($name,$known,true))throw new InvalidArgumentException('Ключ содержит параметр, требующий ручной настройки XKeen.');
 if(($q['encryption'] ?? 'none')!=='none'||!in_array($q['allowInsecure'] ?? '0',['','0','false'],true))throw new InvalidArgumentException('Эта настройка шифрования требует ручной настройки.');
 $user=['id'=>$id,'encryption'=>'none'];
 if(($q['flow'] ?? '')!==''){
  if(!in_array($q['flow'],['xtls-rprx-vision','xtls-rprx-vision-udp443'],true))throw new InvalidArgumentException('Неизвестный flow VLESS.');
  $user['flow']=$q['flow'];
 }
 $type=$q['type'] ?? 'tcp';if($type==='raw')$type='tcp';
 $stream=['network'=>$type,'security'=>$q['security'] ?? 'none'];
 switch($type){
  case 'tcp':
   if(!in_array($q['headerType'] ?? 'none',['','none'],true))throw new InvalidArgumentException('TCP HTTP-заголовок требует ручной настройки.');
   break;
  case 'grpc':
   $stream['grpcSettings']=['serviceName'=>$q['serviceName'] ?? '','multiMode'=>($q['mode'] ?? '')==='multi'];
   if(($q['authority'] ?? '')!=='')$stream['grpcSettings']['authority']=$q['authority'];
   break;
  case 'ws':
   $stream['wsSettings']=['path'=>$q['path'] ?? '/'];
   if(($q['host'] ?? '')!=='')$stream['wsSettings']['headers']=['Host'=>$q['host']];
   break;
  case 'httpupgrade':
   $stream['httpupgradeSettings']=['path'=>$q['path'] ?? '/','host'=>$q['host'] ?? ''];break;
  case 'xhttp':
   $mode=$q['mode'] ?? 'auto';
   if(!in_array($mode,['auto','packet-up','stream-up','stream-one'],true))throw new InvalidArgumentException('Неизвестный режим XHTTP.');
   $stream['xhttpSettings']=['path'=>$q['path'] ?? '/','host'=>$q['host'] ?? '','mode'=>$mode];
   if(($q['extra'] ?? '')!==''){
    $extra=json_decode($q['extra'],false,16);
    if(!$extra instanceof stdClass)throw new InvalidArgumentException('Некорректные дополнительные настройки XHTTP.');
    $stream['xhttpSettings']['extra']=$extra;
   }
   break;
  default:throw new InvalidArgumentException('Этот транспорт VLESS пока требует ручной настройки XKeen.');
 }
 $security=$stream['security'];
 if(!in_array($security,['none','tls','reality'],true))throw new InvalidArgumentException('Неизвестная защита VLESS.');
 if($security!=='none'){
  $sni=$q['sni'] ?? $host;if(!xkeenHost($sni))throw new InvalidArgumentException('Некорректный SNI.');
  $tls=['serverName'=>$sni];
  if(($q['fp'] ?? '')!=='')$tls['fingerprint']=$q['fp'];
  if($security==='reality'){
   if(!preg_match('/\A[A-Za-z0-9_-]{43}\z/D',$q['pbk'] ?? '')||!preg_match('/\A(?:[a-f0-9]{2}){0,8}\z/iD',$q['sid'] ?? ''))throw new InvalidArgumentException('Некорректные параметры Reality.');
   $tls+=['fingerprint'=>$q['fp'] ?? 'chrome','publicKey'=>$q['pbk'],'shortId'=>$q['sid'] ?? '','spiderX'=>$q['spx'] ?? '/'];
  }elseif(($q['alpn'] ?? '')!=='')$tls['alpn']=explode(',',$q['alpn']);
  $stream[$security.'Settings']=$tls;
 }
 return ['protocol'=>'vless','settings'=>['vnext'=>[['address'=>$host,'port'=>$port,'users'=>[$user]]]],'streamSettings'=>$stream];
}
function xkeenHost(string $host): bool {
 return (bool)filter_var($host,FILTER_VALIDATE_IP)||(strlen($host)<=253&&(bool)preg_match('/\A(?=.{1,253}\z)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\.?\z/iD',$host));
}
