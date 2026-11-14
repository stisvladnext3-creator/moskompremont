package com.v2ray.ang.ui.main

import android.content.Intent
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createEmptyComposeRule
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.semantics.SemanticsActions
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.v2ray.ang.AppConfig
import com.v2ray.ang.R
import com.v2ray.ang.handler.MmkvManager
import com.v2ray.ang.handler.moskom.MoskomPolicy
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.net.URL

/** Exercises the real Compose shell, encrypted account session and native daemon on Android. */
class MoskomIntegrationTest {
    @get:Rule val compose = createEmptyComposeRule()
    private lateinit var scenario: ActivityScenario<MainActivity>
    private lateinit var model: MainViewModel
    private val context get()=InstrumentationRegistry.getInstrumentation().targetContext
    private val device get()=UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
    private fun waitReady(){compose.waitForIdle();compose.waitUntil(120_000){model.moskom().state.value.let{it.loaded&&!it.busy&&!it.connecting}};compose.waitForIdle()}
    private fun dispatch(action:MoskomAction){compose.runOnIdle{model.onAction(MainAction.Moskom(action))};waitReady()}
    private fun phase(value:Int){URL("http://10.0.2.2:18080/fixture/phase?value=$value").openStream().close()}
    private fun click(tag:String){compose.onNodeWithTag(tag).performScrollTo().performClick()}
    private fun text(id:Int)=context.getString(id)
    private fun closeSheet(){compose.onNodeWithText(text(R.string.moskom_close)).performScrollTo().performClick()}
    private fun screenshot(name:String){
        compose.waitForIdle()
        val file=File(context.cacheDir,"$name.png");device.takeScreenshot(file)
        val connection=URL("http://10.0.2.2:18080/fixture/screenshot?name=$name.png").openConnection() as java.net.HttpURLConnection
        connection.requestMethod="POST";connection.doOutput=true
        connection.outputStream.use{out->file.inputStream().use{it.copyTo(out)}}
        connection.inputStream.close();connection.disconnect();file.delete()
    }
    private fun approveSystemDialog(){
        val allow=device.wait(Until.findObject(By.res("android:id/button1")),15_000)
        assertNotNull("Android VPN consent must be visible",allow)
        allow!!.click()
        assertTrue("Android VPN consent must close after approval",device.wait(Until.gone(By.pkg("com.android.vpndialogs")),15_000))
    }
    @Test fun accountGroupsRoutingDnsNativeLifecycleAndRevocation() {
        phase(0)
        device.executeShellCommand("pm grant ru.moskompremont.vpn android.permission.POST_NOTIFICATIONS")
        scenario=ActivityScenario.launch(Intent(context,MainActivity::class.java).putExtra("moskom_fixture","http://10.0.2.2:18080/").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        scenario.onActivity{model=ViewModelProvider(it)[MainViewModel::class.java]}
        compose.mainClock.advanceTimeByFrame()
        waitReady()
        compose.waitUntil(30_000){compose.onAllNodesWithTag("moskom_power").fetchSemanticsNodes(atLeastOneRootRequired=false).isNotEmpty()}
        scenario.onActivity{assertFalse("Dark shell must use readable light system bar icons",androidx.core.view.WindowCompat.getInsetsController(it.window,it.window.decorView).isAppearanceLightStatusBars)}
        assertTrue(model.moskom().state.value.groups.isEmpty())
        compose.onNodeWithTag("moskom_power").assertIsNotEnabled()
        screenshot("01-empty")
        // Touch UI login with the IME open, including the last form action.
        click("moskom_account")
        compose.onNodeWithTag("moskom_email").performScrollTo().performTextInput("client@example.test")
        compose.onNodeWithTag("moskom_password").performScrollTo().performTextInput("fixture-pass")
        compose.onNodeWithTag("moskom_login_submit").performScrollTo().performClick()
        waitReady();compose.waitUntil(30_000){model.moskom().state.value.user!=null}
        closeSheet()
        val groups=model.moskom().state.value.groups
        assertEquals(setOf("VPN-A","VPN-B"),groups.map{it.name}.toSet())
        assertEquals(listOf("A-1","A-2"),groups.first{it.name=="VPN-A"}.servers.map{it.name})
        screenshot("02-account")
        click("moskom_subscription");compose.onNodeWithText("VPN-B · 1").performClick();waitReady()
        assertEquals("moskom-cabinet-41-802",model.moskom().state.value.group)
        click("moskom_server");compose.onNodeWithText("A-1").assertDoesNotExist();compose.onAllNodesWithText("B-1").onLast().performClick();waitReady()
        click("moskom_routing");compose.onNodeWithText(text(R.string.moskom_mode_except)).performClick();waitReady()
        assertEquals("except_ru",model.moskom().state.value.mode)
        // Menu, DNS dialog, persistence and recreation by domain ID.
        click("moskom_menu");compose.onNodeWithText(text(R.string.moskom_dns)).performClick()
        compose.onNodeWithText(text(R.string.moskom_dns_auto)).performClick();compose.onNodeWithText(text(R.string.moskom_dns_google)).performClick()
        compose.onNodeWithText(text(R.string.moskom_save_dns)).performScrollTo().performClick();waitReady();closeSheet()
        assertEquals("google",model.moskom().state.value.dns)
        dispatch(MoskomAction.Rules)
        assertEquals(0,model.moskom().state.value.error)
        assertEquals("google",model.moskom().state.value.dns)
        assertEquals("except_ru",model.moskom().state.value.mode)
        val selected=model.moskom().state.value.server
        scenario.recreate();scenario.onActivity{model=ViewModelProvider(it)[MainViewModel::class.java]};waitReady()
        assertEquals(selected,model.moskom().state.value.server);assertEquals("google",model.moskom().state.value.dns)
        assertEquals("moskom-cabinet-41-802",model.moskom().state.value.group)
        // Native real-ping works while VPN is off and must traverse the host Xray inbound.
        runBlocking(Dispatchers.IO){MmkvManager.encodeSettings(AppConfig.PREF_DELAY_TEST_URL,"http://127.0.0.1:18080/ping")}
        click("moskom_availability")
        compose.waitUntil(60_000){model.serversForGroup(model.moskom().state.value.group).value.firstOrNull{it.guid==selected}?.testDelayMillis?.let{it>0}==true}
        compose.waitUntil(30_000){!model.uiState.value.isTesting}
        waitReady()
        screenshot("03-real-availability")
        // Real Android permission flow and daemon IPC running/stopped state, repeated.
        compose.onNodeWithTag("moskom_power").assertIsEnabled()
        click("moskom_power");approveSystemDialog()
        compose.waitUntil(60_000){model.uiState.value.isRunning};screenshot("04-connected")
        click("moskom_power");compose.waitUntil(30_000){!model.uiState.value.isRunning}
        click("moskom_power");compose.waitUntil(60_000){model.uiState.value.isRunning}
        // Revocation stops the selected B group before it is removed; A's changed URL is imported.
        phase(1);dispatch(MoskomAction.Sync)
        assertFalse(model.uiState.value.isRunning)
        assertEquals(listOf("VPN-A"),model.moskom().state.value.groups.map{it.name})
        assertEquals(listOf("A-NEW-1","A-NEW-2"),model.moskom().state.value.groups.single().servers.map{it.name})
        // A transient download failure preserves the previous working group.
        phase(3);dispatch(MoskomAction.Sync)
        assertEquals(listOf("A-NEW-1","A-NEW-2"),model.moskom().state.value.groups.first{it.name=="VPN-A"}.servers.map{it.name})
        assertEquals(R.string.moskom_partial_sync,model.moskom().state.value.message)
        dispatch(MoskomAction.Import("vless://33333333-3333-4333-8333-333333333333@10.0.2.2:18081?encryption=none&security=none&type=tcp#Manual-key"))
        assertTrue(model.moskom().state.value.groups.any{it.id==AppConfig.DEFAULT_SUBSCRIPTION_ID})
        dispatch(MoskomAction.Logout)
        assertNull(model.moskom().state.value.user)
        assertTrue(model.moskom().state.value.groups.none{it.id.startsWith(MoskomPolicy.CABINET)})
        assertEquals("Manual-key",model.moskom().state.value.groups.single().servers.single().name)
        // Help stays inside the branded activity and includes the owner's routing descriptions.
        click("moskom_help")
        compose.onNodeWithText("Режим «Всё» — полная маршрутизация",substring=true).assertExists();screenshot("05-help");closeSheet()
        // Standard focus activation must match touch for keyboard and D-pad users.
        for(key in listOf(Key.Enter,Key.Spacebar,Key.DirectionCenter)) {
            // Real hardware navigation leaves Android touch mode before a button requests focus.
            device.pressDPadDown();compose.waitForIdle()
            compose.onNodeWithTag("moskom_help").performScrollTo().performSemanticsAction(SemanticsActions.RequestFocus){it()}
            compose.onNodeWithTag("moskom_help").assertIsFocused().performKeyInput{pressKey(key)}
            compose.onNodeWithText("Режим «Всё» — полная маршрутизация",substring=true).assertExists();closeSheet()
        }
        compose.onNodeWithTag("moskom_menu").assertContentDescriptionEquals(text(R.string.moskom_menu))
        compose.onNodeWithTag("moskom_add").assertContentDescriptionEquals(text(R.string.moskom_add))
        screenshot("06-manual-after-logout")
        scenario.close()
    }
}
