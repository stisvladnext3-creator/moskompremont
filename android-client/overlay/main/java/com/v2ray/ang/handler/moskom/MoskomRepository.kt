package com.v2ray.ang.handler.moskom

import android.content.Context
import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.v2ray.ang.AppConfig
import com.v2ray.ang.BuildConfig
import com.v2ray.ang.R
import com.v2ray.ang.dto.entities.SubscriptionItem
import com.v2ray.ang.handler.AngConfigManager
import com.v2ray.ang.handler.MmkvManager
import com.v2ray.ang.ui.compose.ThemeManager
import java.io.File
import java.io.IOException
import java.util.UUID

/** Service account orchestration. Actual profile records remain owned by MmkvManager. Call on IO. */
class MoskomRepository(private val context: Context) {
    val http = MoskomHttp(context)
    val routes = MoskomRoutes(context,http)
    var changed = false
        private set
    private val gson = Gson()
    private val userKey = "moskom_user"
    private val groupKey = "moskom_group"
    var user: MoskomUser? = runCatching { gson.fromJson(MmkvManager.decodeSettingsString(userKey),MoskomUser::class.java) }.getOrNull()
        private set

    fun initialize() {
        ThemeManager.setThemeMode("2")
        ThemeManager.setDynamicColorEnabled(false)
        MmkvManager.encodeSettings(AppConfig.PREF_GROUP_ALL_DISPLAY,false)
        MmkvManager.encodeSettings(AppConfig.PREF_LOGLEVEL,"warning")
        val default = MmkvManager.decodeSubscription(AppConfig.DEFAULT_SUBSCRIPTION_ID) ?: SubscriptionItem()
        default.remarks = context.getString(R.string.moskom_manual_keys)
        MmkvManager.encodeSubscription(AppConfig.DEFAULT_SUBSCRIPTION_ID, default)
        routes.initialize()
        ensureSelection()
    }
    fun groups(): List<MoskomGroup> = MmkvManager.decodeSubscriptions().mapNotNull { sub ->
        val servers=MmkvManager.decodeServerList(sub.guid).mapNotNull { id -> MmkvManager.decodeServerConfig(id)?.let { MoskomServer(id,it.remarks) } }
        if (sub.guid == AppConfig.DEFAULT_SUBSCRIPTION_ID && servers.isEmpty()) null else MoskomGroup(sub.guid,sub.subscription.remarks,servers)
    }.sortedBy { if(it.id.startsWith(MoskomPolicy.CABINET))0 else 1 }
    fun selectedGroup(): String = MmkvManager.decodeSettingsString(groupKey).orEmpty()
    fun selectedServer(): String = MmkvManager.getSelectServer().orEmpty()
    fun ensureSelection(): MoskomGroup? {
        val groups=groups();val selected=selectedGroup()
        val group=MoskomPolicy.selectedGroup(groups,selected,selectedServer())
        MmkvManager.encodeSettings(groupKey,group?.id.orEmpty())
        if(group?.servers?.any { it.id==selectedServer() }!=true) MmkvManager.setSelectServer(group?.servers?.firstOrNull()?.id.orEmpty())
        return group
    }
    fun chooseGroup(id: String) { require(groups().any { it.id==id });MmkvManager.encodeSettings(groupKey,id);ensureSelection() }
    fun chooseServer(id: String) { require(ensureSelection()?.servers?.any { it.id==id }==true);MmkvManager.setSelectServer(id) }
    fun canDeleteSubscription(id: String) = MoskomPolicy.removableSubscription(id,AppConfig.DEFAULT_SUBSCRIPTION_ID) && MmkvManager.decodeSubscription(id)!=null
    fun deleteManualSubscription(id: String) {
        require(canDeleteSubscription(id))
        MmkvManager.removeSubscriptionWithRollback(id)
        ensureSelection()
    }

    fun login(email: String,password: String) {
        require(email.trim().contains('@') && password.isNotEmpty())
        http.me();val reply=http.api("login",JsonObject().apply { addProperty("email",email.trim());addProperty("password",password);addProperty("remember",true) })
        val row=reply.getAsJsonObject("user")?:throw MoskomAuthException();setUser(row);http.save()
    }
    fun accountSnapshot(): List<MoskomSubscription>? {
        val session=http.me();val row=session.get("user")
        if(row==null || row.isJsonNull){user=null;http.clear();require(MmkvManager.encodeSettings(userKey,""));return null}
        setUser(row.asJsonObject);http.save()
        val response=http.api("subscriptions")
        return MoskomPolicy.subscriptions(response.getAsJsonArray("subscriptions")?:throw IOException("Invalid subscription snapshot"))
    }
    private fun setUser(row: JsonObject) {
        val next=MoskomUser(row.text("id").toLong(),(row.text("first_name")+" "+row.text("last_name")).trim(),row.text("email"))
        require(next.id>0);user=next;require(MmkvManager.encodeSettings(userKey,gson.toJson(next)))
    }
    fun selectedWillBeRemoved(snapshot: List<MoskomSubscription>?): Boolean {
        val selected=selectedGroup();if(!selected.startsWith(MoskomPolicy.CABINET))return false
        val user=user?:return true
        return snapshot==null || snapshot.none { !it.expired && MoskomPolicy.groupId(user.id,it.id)==selected }
    }
    fun applySnapshot(snapshot: List<MoskomSubscription>?,force: Boolean=false,preferCabinet: Boolean=false): Int {
        changed=false
        val user=user
        val active=if(user!=null)snapshot.orEmpty().filterNot { it.expired } else emptyList()
        val desired=active.map { MoskomPolicy.groupId(user!!.id,it.id) }.toSet()
        MmkvManager.decodeSubscriptions().filter { it.guid.startsWith(MoskomPolicy.CABINET) && it.guid !in desired }.forEach { MmkvManager.removeSubscription(it.guid);changed=true }
        var failures=0
        for(sub in active) {
            val id=MoskomPolicy.groupId(user!!.id,sub.id);val old=MmkvManager.decodeSubscription(id)
            val revision=MmkvManager.decodeSettingsString("moskom_rev_$id")
            if(force || old?.url!=sub.url || old?.remarks!=sub.name || revision!=sub.revision || MmkvManager.decodeServerList(id).isEmpty()) {
                try {
                    val content=http.subscription(sub.url)
                    replace(id,sub.name,sub.url,content)
                    changed=true
                    require(MmkvManager.encodeSettings("moskom_rev_$id",sub.revision))
                } catch (cancelled: java.util.concurrent.CancellationException) { throw cancelled }
                catch (_: Exception) { failures++ }
            }
        }
        if(preferCabinet) groups().firstOrNull { it.id in desired }?.let { MmkvManager.encodeSettings(groupKey,it.id) }
        ensureSelection();return failures
    }
    private fun replace(id: String,name: String,url: String,content: String) {
        require(content.isNotBlank() && content.length<=2*1024*1024)
        val previous=MmkvManager.decodeSubscription(id)
        // The upstream profile importer commits a group atomically; restore metadata if it rejects input.
        try {
            MmkvManager.encodeSubscription(id,SubscriptionItem(remarks=name,url=url,autoUpdate=false))
            val result=AngConfigManager.importBatchConfig(content,id,false)
            if(result.first<=0)throw IOException("No supported profiles")
        } catch(error: Exception) {
            if(previous==null)MmkvManager.removeSubscription(id) else MmkvManager.encodeSubscription(id,previous)
            throw error
        }
    }
    fun importClipboard(text: String) {
        val value=text.trim();require(value.isNotEmpty()&&value.length<=2*1024*1024)
        if(MoskomPolicy.validSubscriptionUrl(value)) {
            val existing=MmkvManager.decodeSubscriptions().firstOrNull { it.subscription.url==value && !it.guid.startsWith(MoskomPolicy.CABINET) }
            val id=existing?.guid?:"moskom-manual-${UUID.randomUUID()}"
            val name=existing?.subscription?.remarks ?: context.getString(R.string.moskom_added_subscription)
            replace(id,name,value,http.subscription(value));MmkvManager.encodeSettings(groupKey,id)
        } else {
            val result=AngConfigManager.importBatchConfig(value,AppConfig.DEFAULT_SUBSCRIPTION_ID,true)
            require(result.first>0);MmkvManager.encodeSettings(groupKey,AppConfig.DEFAULT_SUBSCRIPTION_ID)
        }
        ensureSelection()
    }
    fun updateManual(): Int {
        var failures=0
        MmkvManager.decodeSubscriptions().filter { !it.guid.startsWith(MoskomPolicy.CABINET) && it.subscription.url.isNotBlank() }.forEach { sub ->
            try {replace(sub.guid,sub.subscription.remarks,sub.subscription.url,http.subscription(sub.subscription.url))}catch(_:Exception){failures++}
        };ensureSelection();return failures
    }
    fun logout() {
        runCatching { http.api("logout",JsonObject()) }
        MmkvManager.decodeSubscriptions().filter { it.guid.startsWith(MoskomPolicy.CABINET) }.forEach { MmkvManager.removeSubscription(it.guid) }
        http.clear();user=null;MmkvManager.encodeSettings(userKey,"");ensureSelection()
    }
    fun forgetAccount() {
        MmkvManager.decodeSubscriptions().filter { it.guid.startsWith(MoskomPolicy.CABINET) }.forEach { MmkvManager.removeSubscription(it.guid) }
        http.clear();user=null;MmkvManager.encodeSettings(userKey,"");ensureSelection()
    }
    fun service(): Pair<MoskomNotice,List<MoskomNode>> {
        val notice=http.siteJson("api/service-notice.php");val monitor=http.siteJson("api/server-status.php")
        val nodes=monitor.getAsJsonArray("nodes")?.map { it.asJsonObject }.orEmpty().map { row ->
            MoskomNode(row.text("name"),row.text("status"),row.text("latency_ms").toLongOrNull(),row.text("checked_at"))
        }
        return MoskomNotice(notice.text("enabled")=="true",notice.text("level","warning"),notice.text("message")) to nodes
    }
    fun appUpdate(): MoskomRelease? {
        val array=JsonParser.parseString(String(http.githubBytes("https://api.github.com/repos/${MoskomPolicy.REPOSITORY}/releases?per_page=30",2*1024*1024))).asJsonArray
        return MoskomPolicy.release(array,BuildConfig.VERSION_NAME)
    }
    fun downloadUpdate(release: MoskomRelease,progress:(Int)->Unit): File {
        val text=String(http.githubBytes(release.checksumUrl,65536));val hash=MoskomPolicy.checksum(text,release.filename)
        val file=File(context.cacheDir,"moskomupdates/${release.filename}");http.download(release.url,file,512L*1024*1024,hash,progress)
        require(java.util.zip.ZipFile(file).use { it.getEntry("AndroidManifest.xml")!=null && it.getEntry("classes.dex")!=null });return file
    }
    fun defer(version: String) {MmkvManager.encodeSettings("moskom_defer_version",version);MmkvManager.encodeSettings("moskom_defer_until",System.currentTimeMillis()+86400000)}
    fun deferred(release: MoskomRelease) = MmkvManager.decodeSettingsString("moskom_defer_version")==release.version && MmkvManager.decodeSettingsLong("moskom_defer_until",0)>System.currentTimeMillis()
    fun close()=http.close()
}
