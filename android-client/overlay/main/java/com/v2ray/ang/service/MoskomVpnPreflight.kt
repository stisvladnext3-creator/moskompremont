package com.v2ray.ang.service

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import com.v2ray.ang.AppConfig
import com.v2ray.ang.BuildConfig
import com.v2ray.ang.handler.MmkvManager
import com.v2ray.ang.util.LogUtil
import java.io.File
import kotlinx.coroutines.*

/** Owns only cancellable app-package preflight for this VpnService instance.
 * Native setup/teardown stays in CoreVpnService's established serialized callback path.
 * Preflight creates no tunnel/native resources: cancellation discards its IO result
 * before the queued main-thread continuation can start the VPN after service destruction.
 */
class MoskomVpnPreflight(context:Context) {
    private val app=context.applicationContext
    private val owner=SupervisorJob()
    private val scope=CoroutineScope(owner+Dispatchers.Main.immediate)
    private var work:Job?=null
    @Volatile private var closed=false
    fun begin(intent:Intent?,start:()->Unit,reject:()->Unit) {
        if(closed||work?.isActive==true)return
        work=scope.launch {
            try {
                val ready=withContext(Dispatchers.IO) {
                    // Test the Android-owned stop-during-setup path without delaying release builds.
                    val pause=if(BuildConfig.DEBUG)intent?.getLongExtra("moskom_fixture_setup_delay",0)?.coerceIn(0,2000)?:0 else 0
                    if(pause>0){val marker=File(app.filesDir,"moskom-vpn-preflight-ready");marker.writeText("ready");try{delay(pause)}finally{marker.delete()}}
                    val packages=MmkvManager.decodeSettingsStringSet(AppConfig.PREF_PER_APP_PROXY_SET).orEmpty().filter{it!=BuildConfig.APPLICATION_ID}
                    MmkvManager.decodeSettingsString("moskom_route_mode")=="apps"&&MmkvManager.decodeSettingsBool(AppConfig.PREF_PER_APP_PROXY)&&!MmkvManager.decodeSettingsBool(AppConfig.PREF_BYPASS_APPS)&&packages.any(::installed)
                }
                ensureActive()
                if(!closed){if(ready)start()else reject()}
            }catch(cancelled:CancellationException){throw cancelled}
            catch(error:Exception){LogUtil.e(AppConfig.TAG,"VPN selective-app package preflight failed",IllegalStateException(error.javaClass.simpleName));if(!closed)reject()}
        }
    }
    // Android 15 Builder.verifyApp ignores a null IPackageManager application result.
    // Explicit lookup prevents accepting only removed packages; emulator regression covers it.
    // API 33 typed flags keep the supported API 24 fallback; remove fallback when minSdk >=33.
    @Suppress("DEPRECATION")
    private fun installed(id:String):Boolean = try {
        if(Build.VERSION.SDK_INT>=33)app.packageManager.getApplicationInfo(id,PackageManager.ApplicationInfoFlags.of(0))
        else app.packageManager.getApplicationInfo(id,0)
        true
    }catch(_:PackageManager.NameNotFoundException){false}
    fun close(){closed=true;work?.cancel();owner.cancel()}
}
