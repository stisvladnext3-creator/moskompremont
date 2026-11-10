<?php
declare(strict_types=1);
require_once dirname(__DIR__).'/private/monitor-http.php';
try {if($_SERVER['REQUEST_METHOD']!=='GET')problem('Требуется GET.',405);monitorAnswer(monitorPublic(monitorDb()));}
catch(AccountError $e){monitorAnswer(['error'=>$e->getMessage()],$e->httpStatus);}
catch(Throwable $e){error_log('Server status: '.$e->getMessage());monitorAnswer(['error'=>'Мониторинг временно недоступен.'],503);}
