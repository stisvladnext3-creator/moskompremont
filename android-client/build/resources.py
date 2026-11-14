from pathlib import Path
import re,json,xml.etree.ElementTree as ET

STRINGS={
'brand':'МОСКОМПРЕМОНТ','title':'Мировой интернет','menu':'Меню приложения',
'power_on':'Включить VPN','power_off':'Выключить VPN','connecting':'Подключение…','connected':'VPN включён','disconnected':'VPN выключен',
'status':'Статус сервиса','status_ok':'Сервис работает без сбоев','status_down':'Сервис временно недоступен','status_warning':'Возможны перебои',
'login':'Войти в кабинет','cabinet':'Мой кабинет','subscription':'Подписка','server':'Сервер','routing':'Маршрутизация · Russia',
'choose_subscription':'Выберите подписку','choose_server':'Выберите сервер','group_count':'%1$s · %2$d','add':'Добавить скопированную ссылку',
'mode_all':'Всё','mode_except':'Всё, кроме РФ','mode_blocked':'Заблокированное',
'update_servers':'Обновить серверы','availability':'Проверить доступность','testing':'Проверяем…','ms':'%1$d мс',
'unavailable':'Не отвечает','available':'Доступен','unchecked':'Проверка выбранного сервера ещё не выполнялась.',
'empty':'Войдите в кабинет или скопируйте ссылку-подписку и нажмите «+».','choose_hint':'Выберите подписку, затем сервер и нажмите большую кнопку.',
'no_servers':'В этой подписке пока нет серверов. Нажмите «Обновить серверы».',
'android_mode':'Режим: VPN Android','help':'Справка','exit':'Выход','version':'МосКомпРемонт VPN %1$s · v2rayNG 2.3.15 · GPL-3.0',
'rules':'Обновить правила Russia','check_update':'Проверить обновления приложения','dns':'DNS','close':'Понятно','refresh':'Обновить',
'manage_subscriptions':'Управление подписками','manage_description':'Здесь можно удалить подписки, добавленные вручную. Подписки из личного кабинета управляются на сайте.',
'manual_subscriptions_empty':'Подписок, добавленных вручную, пока нет.','subscription_server_count':'Серверов: %1$d',
'delete_subscription':'Удалить','delete_subscription_named':'Удалить подписку «%1$s»','delete_subscription_title':'Удалить подписку?',
'delete_subscription_description':'«%1$s» и все её серверы будут удалены из приложения. Активное подключение этой подписки будет отключено. Для повторного добавления понадобится ссылка-подписка.',
'confirm_subscription_delete':'Удалить подписку','cancel_subscription_delete':'Отмена','subscription_deleted':'Подписка удалена из приложения.',
'subscription_not_removable':'Подписка уже удалена или управляется через личный кабинет на сайте.','subscription_delete_error':'Не удалось удалить подписку. Повторите попытку.',
'email':'Почта','password':'Пароль','logout':'Выйти из кабинета',
'login_description':'Используйте почту и пароль от moskompremont.ru. Активные подписки загрузятся автоматически. Пароль на устройстве не сохраняется.',
'account_subscriptions':'Подписок из кабинета: %1$d','manual_keys':'Отдельные ключи','added_subscription':'Добавленная подписка',
'dns_auto':'Автоматически · Russia','dns_cloudflare':'Cloudflare · 1.1.1.1','dns_google':'Google · 8.8.8.8','dns_quad9':'Quad9 · 9.9.9.9','dns_custom':'Свой DNS',
'dns_custom_hint':'IP-адрес или https://…/dns-query','save_dns':'Сохранить DNS',
'dns_description':'DNS применяется внутри VPN. Готовые варианты используют DNS-over-HTTPS. Для прямых соединений сохраняется DNS Russia. Активное подключение будет перезапущено.',
'rules_description':'Russia определяет, какие сайты идут через VPN. Обновление получает свежие правила и геоданные. Выбранная политика и DNS сохраняются. Новые ограничения учитываются после добавления сервисов в исходные списки Russia.',
'rules_stamp':'Версия геоданных: %1$s','update_title':'Доступно обновление %1$s','update_description':'Подписки и настройки сохранятся. После загрузки приложение проверит файл. Для установки Android может попросить разрешить обновления из этого источника.',
'update_now':'Обновить сейчас','install_update':'Установить обновление','later':'Позже','cancel':'Отменить загрузку','progress':'Загружено: %1$d%%',
'service_error':'Не удалось обновить статус. Проверьте интернет и повторите попытку.','stale':'Нет свежих данных',
'connect_error':'Не удалось запустить VPN. Попробуйте другой сервер.','operation_error':'Не удалось выполнить действие. Проверьте интернет и повторите попытку.',
'login_again':'Сессия кабинета завершена. Войдите снова.','login_success':'Вход выполнен. Выберите подписку и сервер.',
'logout_success':'Вы вышли из кабинета. Добавленные вручную ссылки сохранены.','synced':'Подписки обновлены.','partial_sync':'Не все подписки удалось обновить. Повторим синхронизацию позже.',
'imported':'Ссылка добавлена. Выберите подписку и сервер.','cancelled':'Загрузка отменена.','dns_invalid':'Укажите IP-адрес DNS или HTTPS-адрес DNS-over-HTTPS.',
'dns_saved':'DNS сохранён и применён.','rules_updated':'Правила Russia обновлены.','rules_current':'У вас уже актуальные правила Russia.',
'app_current':'У вас установлена последняя доступная версия.','update_ready':'Файл проверен. Можно устанавливать обновление.',
'install_permission':'Разрешите установку из этого источника, вернитесь в приложение и нажмите «Установить обновление».','install_error':'Не удалось открыть установщик Android.',
}

def write_resources(root,own):
 source=(own.parent/'desktop-client/overlay/v2rayN.Desktop/Views/MoskomHelpView.cs').read_text()
 topics=[(json.loads(a),json.loads(b)) for a,b in re.findall(r'Topic\(("(?:[^"\\]|\\.)*"), ("(?:[^"\\]|\\.)*")\);',source)]
 assert len(topics)==14
 topics=[(title,body.replace('системный прокси Windows','VPN Android').replace('прокси Windows','VPN Android')) for title,body in topics]
 topics[0]=('Включить VPN / Выключить VPN','Большая кнопка подключает выбранный сервер через системный VPN Android. При первом включении подтвердите разрешение на VPN. Повторное нажатие после подключения выключает VPN.')
 topics[5]=('Проверить доступность','Проверяет выбранный сервер с вашего телефона и показывает результат в мс. Для этой проверки включать VPN не требуется.')
 topics[6]=('Статус сервиса','Открывает объявление и состояние серверов с сайта. Проверка на сервере сервиса может отличаться от результата на вашем телефоне.')
 topics[10]=('Проверить обновления приложения','Проверяет новую версию. «Обновить сейчас» скачивает и проверяет APK. Затем нажмите «Установить обновление» и подтвердите системный запрос Android. «Позже» откладывает предложение на сутки. Подписки и настройки сохраняются.')
 topics[-1]=('Выход и кнопка «Назад»','«Выход» отключает VPN и закрывает приложение. Кнопка «Назад» на главном экране сворачивает приложение; VPN продолжает работать. Открыть приложение можно значком или через уведомление Android.')
 # Keep the branded Russian labels consistent. Default values are also the English fallback;
 # creating a new values-en locale would incorrectly require translating all upstream core keys.
 folders={'values','values-ru'}|{p.parent.name for p in (root/'V2rayNG/app/src/main/res').glob('values*/strings.xml')}
 for folder in sorted(folders):
  doc=ET.Element('resources')
  for key,value in STRINGS.items():ET.SubElement(doc,'string',name='moskom_'+key).text=value.replace('\n',r'\n')
  for name,items in [('titles',[x[0] for x in topics]),('bodies',[x[1] for x in topics])]:
   arr=ET.SubElement(doc,'string-array',name='moskom_help_'+name)
   for value in items:ET.SubElement(arr,'item').text=value.replace('\n',r'\n')
  ET.indent(doc)
  target=root/'V2rayNG/app/src/main/res'/folder/'moskom_strings.xml';target.parent.mkdir(exist_ok=True)
  ET.ElementTree(doc).write(target,encoding='utf-8',xml_declaration=True)
