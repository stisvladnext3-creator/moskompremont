<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli'){http_response_code(404);exit;}
require_once __DIR__.'/monitor-http.php';
try{$db=monitorDb();monitorCheckMain($db,monitorSettings($db));}
catch(Throwable $e){fwrite(STDERR,"Проверка сервера не завершена. Проверьте конфигурацию мониторинга.\n");exit(1);}
