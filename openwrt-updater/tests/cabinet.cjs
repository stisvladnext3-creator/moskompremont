const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path'), fs = require('node:fs');
const artifacts = process.env.MKR_TEST_ARTIFACTS || '/workspace/artifacts'; fs.mkdirSync(artifacts,{recursive:true});
(async () => {
 const browser = await chromium.launch({executablePath: process.env.CHROMIUM_PATH || '/usr/bin/chromium',headless:true,args:['--no-sandbox']}); const errors=[];
 async function login(email,password,width){const context=await browser.newContext({viewport:{width,height:1000}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());await page.goto('http://localhost:4201/account.html');await page.locator('#login-form [name=email]').fill(email);await page.locator('#login-form [name=password]').fill(password);await page.locator('#login-form button[type=submit]').click();await page.locator('#cabinet-section').waitFor({state:'visible'});return {page,context};}
 try{
  const c=await login('alice@example.test','new-fixture-password',390),p=c.page;
  await p.locator('[data-client-tab=openwrt]').click();await p.locator('#router-device-source option:not([value=""])').first().waitFor({state:'attached'});
  assert(!(await p.locator('#router-device-source').innerText()).includes('BOB'));
  await p.locator('#router-device-form [name=label]').fill('Дом · OpenWrt');await p.locator('#router-device-source').selectOption({index:1});
  let response=p.waitForResponse(r=>r.url().includes('action=router_device_create'));await p.locator('#router-device-form button').click();assert.equal((await response).status(),201);
  await p.locator('#router-device-code').waitFor({state:'visible'});const token=await p.locator('#router-device-code textarea').nth(1).inputValue();assert(/^mkr1\./.test(token));
  assert.equal(await p.evaluate(()=>Object.keys(localStorage).length),0);
  assert(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await p.screenshot({path:path.join(artifacts,'openwrt-cabinet-mobile.png'),fullPage:true});
  await p.locator('[data-client-tab=subscriptions]').click();assert.equal(await p.locator('#router-device-code textarea').count(),0);
  await p.locator('[data-client-tab=openwrt]').click();await p.locator('#router-devices-list .subscription-card').first().getByRole('button',{name:'Отключить код'}).click();await p.getByText('Код устройства отключён.',{exact:true}).waitFor();
  await p.locator('#logout-button').click();await p.locator('#auth-section').waitFor({state:'visible'});assert.equal(await p.locator('#router-devices-list').innerText(),'');
  const a=await login('admin@example.test','fixture-only-password',1280),ap=a.page;await ap.locator('[data-admin-tab=openwrt]').click();await ap.locator('#admin-device-user-query').fill('alice@example.test');await ap.locator('#admin-device-user-search button').click();await ap.locator('#admin-device-user option[value="2"]').waitFor({state:'attached'});await ap.locator('#admin-device-user').selectOption('2');await ap.locator('#admin-router-device-source option:not([value=""])').first().waitFor({state:'attached'});
  await ap.locator('#admin-router-device-form [name=label]').fill('Роутер клиента');await ap.locator('#admin-router-device-source').selectOption({index:1});await ap.locator('#admin-router-device-form button').click();await ap.locator('#admin-router-device-code').waitFor({state:'visible'});assert(!(await ap.locator('#admin-router-devices-list').innerText()).includes(token));assert(await ap.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
  await ap.screenshot({path:path.join(artifacts,'openwrt-admin-desktop.png'),fullPage:true});assert.deepEqual(errors,[]);console.log('PASS cabinet: client/admin pairing, own-source choices, one-time token display, revoke, logout clearing, mobile 390px and desktop 1280px.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
