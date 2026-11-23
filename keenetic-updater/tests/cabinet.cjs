const { chromium } = require('playwright');
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const base=process.env.MKR_TEST_SITE || 'http://localhost:4201';
const output=process.env.MKR_TEST_ARTIFACTS || '/workspace/artifacts/keenetic-20261123';fs.mkdirSync(output,{recursive:true});
(async()=>{
 const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});const errors=[];
 async function login(email,password,width){const context=await browser.newContext({viewport:{width,height:900}}),p=await context.newPage();p.on('pageerror',e=>errors.push(e.message));p.on('dialog',d=>d.accept());await p.goto(base+'/account.html');await p.locator('#login-form [name=email]').fill(email);await p.locator('#login-form [name=password]').fill(password);await p.locator('#login-form button[type=submit]').click();await p.locator('#cabinet-section').waitFor({state:'visible'});return {p,context};}
 try{
  const {p,context}=await login('keenetic@example.test','fixture-only-password',390);
  await p.locator('#keenetic-client-tab').waitFor({state:'visible'});await p.locator('#keenetic-client-tab').click();
  await p.locator('#keenetic-device-source option:not([value=""])').waitFor({state:'attached'});
  const choices=await p.locator('#keenetic-device-source').innerText();assert(choices.includes('Дом Keenetic')&&!choices.includes('Другой протокол')&&!choices.includes('Чужой'));
  await p.locator('#keenetic-device-form [name=label]').fill('Keenetic дома');await p.locator('#keenetic-device-source').selectOption({index:1});await p.locator('#keenetic-device-submit').click();
  await p.locator('#keenetic-device-code').waitFor({state:'visible'});
  const command=await p.locator('#keenetic-device-code textarea').nth(0).inputValue(),token=await p.locator('#keenetic-device-code textarea').nth(1).inputValue();
  assert(command.includes('/downloads/keenetic/install.sh')&&command.includes('/api/router-agent.php')&&!command.includes('mkr1.'));assert(/^mkr1\.[a-f0-9]{24}\.[a-f0-9]{64}$/.test(token));
  assert.equal(await p.evaluate(()=>Object.keys(localStorage).length),0);assert(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await p.screenshot({path:path.join(output,'keenetic-mobile.png'),fullPage:true});
  await p.locator('[data-client-tab=subscriptions]').click();assert.equal(await p.locator('#keenetic-device-code textarea').count(),0);
  await p.locator('#keenetic-client-tab').click();await p.locator('#keenetic-device-form [name=label]').fill('Slow response');await p.locator('#keenetic-device-source').selectOption({index:1});
  let started,release;const start=new Promise(r=>started=r),gate=new Promise(r=>release=r);
  await p.route('**/api/account.php?action=router_device_create',async route=>{const response=await route.fetch();started();await gate;await route.fulfill({response});});
  await p.locator('#keenetic-device-submit').click();await start;await p.locator('[data-client-tab=subscriptions]').click();release();
  await p.locator('#keenetic-device-submit').waitFor({state:'attached'});await p.waitForResponse(r=>r.url().includes('action=router_devices'));await p.waitForTimeout(100);
  assert.equal(await p.locator('#keenetic-device-code textarea').count(),0);await p.unroute('**/api/account.php?action=router_device_create');
  await p.locator('#keenetic-client-tab').click();await p.locator('#keenetic-devices-list .subscription-card').first().getByRole('button',{name:'Отключить код'}).click();await p.getByText('Код устройства отключён.',{exact:true}).waitFor();
  await p.locator('#logout-button').click();await p.locator('#auth-section').waitFor({state:'visible'});assert.equal(await p.locator('#keenetic-device-code textarea').count(),0);assert.equal(await p.locator('#keenetic-devices-list').innerText(),'');await context.close();
  const a=await login('admin@example.test','fixture-only-password',1280);await a.p.locator('[data-admin-tab=openwrt]').click();await a.p.locator('#admin-router-devices-list .subscription-card').first().waitFor();
  assert((await a.p.locator('#admin-router-devices-list').innerText()).includes('Keenetic дома'));assert(!(await a.p.locator('#admin-router-devices-list').innerText()).includes(token));
  assert(await a.p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await a.p.screenshot({path:path.join(output,'keenetic-admin.png'),fullPage:true});assert.deepEqual(errors,[]);
  console.log('PASS cabinet: own VLESS key picker, installation command + one-time code, mobile/desktop layout, no browser storage, hiding on navigation/logout, slow response after navigation, administrator device list and revoke.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
