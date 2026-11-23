<?php
declare(strict_types=1);
require __DIR__.'/../../module/private/xray-outbound.php';
function check(bool $yes,string $why): void {if(!$yes)throw new RuntimeException($why);}
function rejects(string $uri): void {try{xkeenOutbound($uri);}catch(InvalidArgumentException $e){return;}throw new RuntimeException('Expected rejection');}
$base='vless://11111111-1111-4111-8111-111111111111@router.example:4053';
$q='?authority=&encryption=none&fp=chrome&pbk='.str_repeat('A',43).'&security=reality&serviceName=&sid=108f2ebef879eb&sni=sni.example&spx=%2Ftest&support-x25519mlkem768=true&type=grpc#Test';
$o=xkeenOutbound($base.$q);
check($o['settings']['vnext'][0]['address']==='router.example','host');
check($o['settings']['vnext'][0]['port']===4053,'port');
check($o['streamSettings']['realitySettings']['spiderX']==='/test','percent decoding');
check($o['streamSettings']['realitySettings']['serverName']==='sni.example','SNI is not overwritten by endpoint host');
check($o['streamSettings']['grpcSettings']['serviceName']==='','gRPC blank');
check(!isset($o['tag']),'tag must be preserved on device');
check(xkeenOutbound($base.'?type=ws&path=%2Fa%2Bb&host=ws.example&security=tls&alpn=h2,http%2F1.1')['streamSettings']['wsSettings']['path']==='/a+b','RFC3986 plus');
check(xkeenOutbound(str_replace('router.example','[2001:db8::1]',$base))['settings']['vnext'][0]['address']==='2001:db8::1','IPv6');
check(xkeenOutbound($base.'?type=raw')['streamSettings']['network']==='tcp','raw alias');
check(xkeenOutbound($base.'?type=httpupgrade&path=%2Fupgrade')['streamSettings']['httpupgradeSettings']['path']==='/upgrade','HTTP upgrade');
check(xkeenOutbound($base.'?type=xhttp&mode=auto&extra=%7B%22noGRPCHeader%22%3Atrue%7D')['streamSettings']['xhttpSettings']['extra']->noGRPCHeader===true,'XHTTP extra');
foreach(['https://example.test/sub','vless://not-a-uuid@router.example:443',$base.'?type=grpc&type=ws',$base.'?type=h2',$base.'?security=reality&pbk=x',$base.'?encryption=unknown',$base.'?allowInsecure=1',$base.'?flow=unknown',$base.'?ech=unknown',$base.'?path=%0Acommand',$base.'?type=xhttp&extra=%5B1%5D'] as $bad)rejects($bad);
if(($argv[1] ?? '')==='fixtures'){
 $result=[];foreach(['tcp','grpc','ws','httpupgrade','xhttp'] as $type)$result[$type]=xkeenOutbound($base.'?type='.$type.'&security=tls&sni=sni.example&path=%2Ftest');
 $result['reality']=$o;echo json_encode($result,JSON_PRETTY_PRINT|JSON_UNESCAPED_SLASHES);
}else echo "PASS: VLESS UUID/host/port, IPv6, Reality/gRPC, TCP/raw, WS, HTTPUpgrade, XHTTP, percent decoding, unsupported/ambiguous settings rejected.\n";
