"""Apply the branded Android overlay to the pinned, unmodified v2rayNG source."""
from pathlib import Path
import sys, shutil, re
from resources import write_resources

root=Path(sys.argv[1]).resolve(); own=Path(__file__).resolve().parents[1]
version=sys.argv[2]; code=int(sys.argv[3])
assert re.fullmatch(r'\d+\.\d+\.\d+',version)
def edit(name,old,new):
 p=root/name;s=p.read_text(encoding='utf-8-sig')
 if s.count(old)!=1:raise SystemExit(f'Upstream anchor mismatch in {name}: {old[:90]}')
 p.write_text(s.replace(old,new),encoding='utf-8')
for f in (own/'overlay').rglob('*'):
 if f.is_file():
  to=root/'V2rayNG/app/src'/f.relative_to(own/'overlay');to.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,to)
gradle='V2rayNG/app/build.gradle.kts'
edit(gradle,'applicationId = "com.v2ray.ang"','applicationId = "ru.moskompremont.vpn"')
edit(gradle,'applicationIdSuffix = ".fdroid"','// Direct APK distribution uses the service application ID.')
edit(gradle,'versionCode = 755',f'versionCode = {code}')
edit(gradle,'versionName = "2.3.15"',f'versionName = "{version}"')
catalog=root/'V2rayNG/gradle/libs.versions.toml'
catalog.write_text(catalog.read_text().replace('[libraries]','uiautomator = "2.3.0"\n[libraries]\nandroidx-uiautomator = { module = "androidx.test.uiautomator:uiautomator", version.ref = "uiautomator" }\nandroidx-compose-ui-test-junit4 = { module = "androidx.compose.ui:ui-test-junit4" }'))
edit(gradle,'    androidTestImplementation(libs.androidx.espresso.core)','    androidTestImplementation(libs.androidx.espresso.core)\n    androidTestImplementation(libs.androidx.compose.ui.test.junit4)\n    androidTestImplementation(libs.androidx.uiautomator)')
# The service UI follows the Russian Windows client; retain English fallback resources.
g=root/gradle;s=g.read_text();s=re.sub(r'localeFilters \+= listOf\(.*?\n        \)', 'localeFilters += listOf("en", "ru")',s,flags=re.S);g.write_text(s)
res=root/'V2rayNG/app/src/main/res';(res/'drawable').mkdir(exist_ok=True)
shutil.copyfile(own.parent/'desktop-client/branding/app-logo.png',res/'drawable/moskom_logo.png')
manifest='V2rayNG/app/src/main/AndroidManifest.xml'
edit(manifest,'android:banner="@mipmap/ic_banner"\n        android:icon="@mipmap/ic_launcher"','android:banner="@mipmap/ic_banner"\n        android:icon="@drawable/moskom_logo"')
edit(manifest,'android:icon="@mipmap/ic_launcher"','android:icon="@drawable/moskom_status"')
# Android system notifications and quick settings require a monochrome small icon.
# Use the shell's power symbol there; the launcher keeps the original full-color site logo.
for relative,count in [('helper/NotificationHelper.kt',1),('handler/NotificationManager.kt',2),('service/QSTileService.kt',1)]:
 p=root/'V2rayNG/app/src/main/java/com/v2ray/ang'/relative;s=p.read_text();assert s.count('R.drawable.ic_stat_name')==count,relative
 p.write_text(s.replace('R.drawable.ic_stat_name','R.drawable.moskom_status'))
# Do not export account/session data through Android backup.
edit(manifest,'android:allowBackup="true"','android:allowBackup="false"')
for p in res.glob('values*/strings.xml'):
 s=p.read_text();s=re.sub(r'(<string name="app_name"[^>]*>).*?(</string>)',r'\1МосКомпРемонт VPN\2',s);p.write_text(s)
write_resources(root,own)
# Keep subscription deletion in the existing storage owner. Snapshot the records
# under its cross-process index lock so a failed multi-record write can roll back.
edit('V2rayNG/app/src/main/java/com/v2ray/ang/handler/MmkvManager.kt','    fun removeSubscription(subid: String) {','''    internal fun removeSubscriptionWithRollback(subid: String) {
        require(subid.isNotBlank() && subid != DEFAULT_SUBSCRIPTION_ID)
        withProfileIndexLock {
            require(decodeSubscription(subid) != null)
            val servers = decodeServerList(subid).toList()
            val referenced = decodeServersReferencedByOtherGroups(subid)
            val removable = servers.filterNot { it in referenced }
            val backup = mutableListOf<Triple<MMKV, String, String?>>()
            fun capture(storage: MMKV, key: String) { backup.add(Triple(storage, key, storage.decodeString(key))) }
            capture(subStorage, subid)
            capture(mainStorage, KEY_SUB_IDS)
            capture(mainStorage, serverListKey(subid))
            capture(mainStorage, KEY_SELECTED_SERVER)
            removable.forEach { guid ->
                capture(profileFullStorage, guid); capture(serverRawStorage, guid); capture(serverAffStorage, guid)
            }
            try {
                requireStorageWrite(mainStorage.encode(KEY_SUB_IDS, JsonUtil.toJson(decodeSubsList().filterNot { it == subid })), "Failed to remove subscription index")
                requireStorageWrite(persistServerList(emptyList(), subid), "Failed to remove subscription servers")
                if (getSelectServer() in removable) mainStorage.remove(KEY_SELECTED_SERVER)
                subStorage.remove(subid)
                require(!subStorage.containsKey(subid))
                removeProfilePayloads(removable)
                require(removable.none { profileFullStorage.containsKey(it) || serverRawStorage.containsKey(it) || serverAffStorage.containsKey(it) })
            } catch (error: Exception) {
                backup.forEach { (storage, key, value) ->
                    try {
                        if (value == null) storage.remove(key)
                        else requireStorageWrite(storage.encode(key, value), "Failed to restore subscription data")
                    } catch (restore: Exception) { error.addSuppressed(restore) }
                }
                throw error
            }
        }
    }

    fun removeSubscription(subid: String) {''')
main='V2rayNG/app/src/main/java/com/v2ray/ang/ui/main/'
edit(main+'MainActivity.kt','import androidx.lifecycle.lifecycleScope','import androidx.lifecycle.lifecycleScope\nimport androidx.lifecycle.repeatOnLifecycle')
edit(main+'MainContract.kt','sealed interface MainAction {','''sealed interface MainAction {
    data class Moskom(val value: MoskomAction) : MainAction
    data object MoskomAvailability : MainAction''')
edit(main+'MainViewModel.kt','    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()', '''    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()
    private var moskomHolder: MoskomMainController? = null
    fun moskom(): MoskomMainController = moskomHolder ?: MoskomMainController(
        getApplication<Application>(), viewModelScope, uiState
    ) { groupId ->
        setupGroupTab(forceRefresh = true).join()
        subscriptionIdChanged(groupId)
        refreshSelectedGuid()
    }.also { moskomHolder = it }''')
edit(main+'MainViewModel.kt','            MainAction.Initialize -> initialize()', '''            is MainAction.Moskom -> moskom().onAction(action.value)
            MainAction.MoskomAvailability -> testAllRealPing(selectedOnly = true)
            MainAction.Initialize -> initialize()''')
edit(main+'MainViewModel.kt','    fun testAllRealPing(onlyTcp: Boolean = false) {','    fun testAllRealPing(onlyTcp: Boolean = false, selectedOnly: Boolean = false) {')
edit(main+'MainViewModel.kt','        val servers = currentServers()\n        if (servers.isEmpty())', '        val servers = currentServers().let { list -> if (selectedOnly) list.filter { it.guid == dataSource.getSelectServer() } else list }\n        if (servers.isEmpty())')
edit(main+'MainViewModel.kt','serverGuids = if (keywordFilter.isNotEmpty()) serverGuids else emptyList(),','serverGuids = if (selectedOnly || keywordFilter.isNotEmpty()) serverGuids else emptyList(),')
edit(main+'MainViewModel.kt','    private fun updateRunningState(running: Boolean, clearTestingText: Boolean = true) {','    private fun updateRunningState(running: Boolean, clearTestingText: Boolean = true) {\n        moskomHolder?.onNativeState(running)')
edit(main+'MainViewModel.kt','    override fun onCleared() {','    override fun onCleared() {\n        moskomHolder?.close()')
edit(main+'MainActivity.kt','        MainScreen(','        MoskomScreen(')
edit(main+'MainActivity.kt','mainViewModel.onAction(MainAction.ImportBatchConfig(text))','mainViewModel.onAction(MainAction.Moskom(MoskomAction.Import(text)))')
edit(main+'MainActivity.kt','            if (it.resultCode == RESULT_OK) startV2Ray()','            if (it.resultCode == RESULT_OK) startV2Ray() else mainViewModel.onAction(MainAction.Moskom(MoskomAction.PermissionCancelled))')
edit(main+'MainActivity.kt','''        } else {
            requestServiceStart()
        }
    }

    private fun requestServiceStart()''','''        } else {
            mainViewModel.onAction(MainAction.Moskom(MoskomAction.Connecting))
            requestServiceStart()
        }
    }

    private fun requestServiceStart()''')
edit(main+'MainActivity.kt','        if (mainViewModel.uiState.value.selectedGuid.isNullOrEmpty()) {','        if (MmkvManager.getSelectServer().isNullOrEmpty()) {\n            mainViewModel.onAction(MainAction.Moskom(MoskomAction.PermissionCancelled))')
edit(main+'MainActivity.kt','''        super.onCreate(savedInstanceState)
        mainViewModel.onAction(MainAction.Initialize)''','''        if (com.v2ray.ang.BuildConfig.DEBUG) {
            com.v2ray.ang.handler.moskom.MoskomHttp.fixtureSite = intent.getStringExtra("moskom_fixture")
        }
        super.onCreate(savedInstanceState)
        lifecycleScope.launch {
            repeatOnLifecycle(androidx.lifecycle.Lifecycle.State.STARTED) {
                mainViewModel.moskom().events.collect { event ->
                    when(event) {
                        MoskomEvent.Start -> requestServiceStart()
                        MoskomEvent.Exit -> finishAndRemoveTask()
                        is MoskomEvent.Install -> installMoskomUpdate(event.path)
                    }
                }
            }
        }
        mainViewModel.onAction(MainAction.Initialize)''')
edit(main+'MainActivity.kt','    private fun shareToClipboard(guid: String): Boolean =','''    private fun installMoskomUpdate(path: String) {
        lifecycleScope.launch {
            try {
                // Android 8+ requires per-source permission before its system APK installer opens.
                val allowed = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    withContext(Dispatchers.IO) { packageManager.canRequestPackageInstalls() }
                } else true
                if (!allowed) {
                    toast(R.string.moskom_install_permission)
                    startActivity(Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, android.net.Uri.parse("package:" + packageName)))
                    return@launch
                }
                val uri = withContext(Dispatchers.IO) {
                    val file = java.io.File(path).canonicalFile
                    require(file.path.startsWith(java.io.File(cacheDir, "moskomupdates").canonicalPath + "/"))
                    androidx.core.content.FileProvider.getUriForFile(this@MainActivity, packageName + ".cache", file)
                }
                startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
            } catch (error: Exception) {
                LogUtil.e(AppConfig.TAG, "Moskom APK install intent failed", IllegalStateException(error.javaClass.simpleName))
                toast(R.string.moskom_install_error)
            }
        }
    }

    private fun shareToClipboard(guid: String): Boolean =''')
edit(manifest,'    <uses-permission android:name="android.permission.INTERNET" />','    <uses-permission android:name="android.permission.INTERNET" />\n    <uses-permission android:name="android.permission.REQUEST_INSTALL_PACKAGES" />')
shortcuts=res/'xml/shortcuts.xml';shortcuts.write_text(shortcuts.read_text().replace('android:targetPackage="com.v2ray.ang"','android:targetPackage="ru.moskompremont.vpn"'))
print(f'Prepared MosKomRemont Android {version}')
