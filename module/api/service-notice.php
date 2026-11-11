<?php
declare(strict_types=1);
require_once dirname(__DIR__).'/private/monitor-http.php';
require_once dirname(__DIR__).'/private/service-notice.php';
try {
 if($_SERVER['REQUEST_METHOD']!=='GET'){header('Allow: GET');problem('Требуется GET.',405);}
 monitorAnswer(serviceNoticePublic(monitorDb()));
} catch(AccountError $e){monitorAnswer(['error'=>$e->getMessage()],$e->httpStatus);}
catch(Throwable $e){error_log('Service notice: request failed');monitorAnswer(['error'=>'Объявление временно недоступно.'],503);}
