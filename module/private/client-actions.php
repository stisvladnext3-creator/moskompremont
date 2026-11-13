<?php
// Included only after account authentication and administrator routing checks.
if($action==='notifications')answer(clientNotifications($db,$userId,$_GET,$admin));
if($action==='notification'){
 $id=filter_var($_GET['id'] ?? null,FILTER_VALIDATE_INT);if(!$id||!clientEventsReady($db))problem('Уведомление не найдено.',404);
 $item=query($db,'SELECT id,type,title,body,target,target_id,target_ref,created_at,read_at FROM internet_client_notifications WHERE id=? AND user_id=?',[$id,$userId])->fetch();if(!$item||(!$admin&&str_starts_with($item['type'],'admin_')))problem('Уведомление не найдено.',404);answer(['notification'=>$item]);
}
if($action==='notification_read')answer(readClientNotification($db,$userId,$body,$admin));
if($action==='admin_reply_templates')answer(['templates'=>replyTemplates($db)]);
if($action==='admin_template_save')answer(saveReplyTemplate($db,$body,$userId));
if($action==='admin_template_delete'){
 requireReplyTemplates($db);$id=$body['id'] ?? null;if(!is_int($id)||$id<1)problem('Выберите шаблон.');query($db,'DELETE FROM internet_reply_templates WHERE id=?',[$id]);answer(['ok'=>true]);
}
if($action==='push_settings')answer(clientPushSettings($db));
if($action==='push_device_status')answer(pushDeviceStatus($db,$userId,$body));
if($action==='push_subscribe'){limitRate($db,'push-register',20,$userId);answer(registerPushDevice($db,$userId,$body));}
if($action==='push_unsubscribe')answer(removePushDevice($db,$userId,$body));
if($action==='admin_push_settings')answer(adminPushSettings($db,$config));
if($action==='admin_push_setup')answer(setupClientPush($db,$config));
if($action==='admin_push_save'){
 pushRequireReady($db);$enabled=$body['enabled'] ?? null;if(!is_bool($enabled))problem('Укажите состояние push-уведомлений.');
 if($enabled){$settings=pushSettings($db);if(!$settings['public_key']||!$settings['private_key'])problem('Сначала создайте ключи push.');if(pushRuntimeIssues())problem(implode(' ',pushRuntimeIssues()),503);}
 query($db,'UPDATE internet_push_settings SET enabled=? WHERE id=1',[(int)$enabled]);answer(adminPushSettings($db,$config));
}
if($action==='admin_push_retry'){
 limitRate($db,'push-retry',10,$userId);pushRequireReady($db);query($db,"UPDATE internet_push_outbox SET status='retry',attempts=0,next_attempt_at=CURRENT_TIMESTAMP,lease_until=NULL WHERE status='failed'");query($db,"UPDATE internet_push_outbox SET next_attempt_at=CURRENT_TIMESTAMP WHERE status='retry'");answer(sendClientPush($db,$config,20));
}

if($action==='admin_accounts')answer(adminAccountRows($db,$_GET));
if($action==='admin_account_password'){
 $result=resetAccountPassword($db,$body,$userId);if($result['logged_out']){forgetRemember($db,$config);$_SESSION=[];session_regenerate_id(true);$_SESSION['csrf']=bin2hex(random_bytes(32));$result['csrf']=$_SESSION['csrf'];}answer($result);
}
if($action==='admin_account_delete')answer(deleteAccount($db,$body,$userId));
