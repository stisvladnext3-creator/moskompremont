package com.v2ray.ang.handler.moskom

import android.content.Context
import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.v2ray.ang.AppConfig
import com.v2ray.ang.dto.entities.RulesetItem
import com.v2ray.ang.handler.MmkvManager
import com.v2ray.ang.util.Utils
import java.io.File
import java.util.UUID

/** Immutable bundles keep native geo files and rules consistent across updates and rollback. */
class MoskomRoutes(private val context: Context,private val http: MoskomHttp) {
    private val root get()=File(Utils.userAssetPath(context))
    private val gson=Gson()
    var active: File = root
        private set
    fun mode()=MmkvManager.decodeSettingsString("moskom_route_mode","all").orEmpty()
    fun dns()=MmkvManager.decodeSettingsString("moskom_dns","auto").orEmpty()
    fun customDns()=MmkvManager.decodeSettingsString("moskom_custom_dns").orEmpty()
    fun stamp()=MmkvManager.decodeSettingsString("moskom_rules_stamp").orEmpty()
    private fun read(file: File)=JsonParser.parseString(file.readText()).asJsonObject
    fun initialize() {
        val name=MmkvManager.decodeSettingsString("moskom_route_bundle").orEmpty()
        val bundled=JsonParser.parseString(context.assets.open("moskom/russia/manifest.json").bufferedReader().use { it.readText() }).asJsonObject
        val dir=File(root,name)
        if(Regex("moskom-bundle-[a-f0-9-]{36}").matches(name)&&File(dir,"manifest.json").isFile&&File(dir,"geosite.dat").isFile&&File(dir,"geoip.dat").isFile&&read(File(dir,"manifest.json")).text("geoRelease")>=bundled.text("geoRelease"))active=dir
        else {
            val stage=File(root,"moskom-stage-${UUID.randomUUID()}");stage.mkdirs()
            for(file in listOf("route-0.json","route-1.json","route-2.json","template.json","simple_dns.json","manifest.json","geoip.dat","geosite.dat"))
                context.assets.open(if(file.endsWith(".dat"))file else "moskom/russia/$file").use { input -> File(stage,file).outputStream().use { input.copyTo(it) } }
            activate(stage)
        }
        apply(mode(),dns(),customDns())
    }
    private fun rules(bundle: File,mode: String): MutableList<RulesetItem> {
        val index=when(mode){"all"->0;"except_ru"->1;"blocked"->2;else->error("Unknown routing mode")}
        val source=JsonParser.parseString(File(bundle,"route-$index.json").readText()).asJsonArray
        require(source.size()>0)
        return source.map { element ->
            val item=gson.fromJson(element,RulesetItem::class.java)
            fun geo(value:String)=when {
                value.startsWith("geosite:")->"ext:${bundle.name}/geosite.dat:${value.removePrefix("geosite:")}"
                value.startsWith("geoip:")->"ext:${bundle.name}/geoip.dat:${value.removePrefix("geoip:")}"
                else->value
            }
            item.id=UUID.randomUUID().toString();item.domain=item.domain?.map(::geo);item.ip=item.ip?.map(::geo);item
        }.toMutableList()
    }
    fun apply(mode: String, dns: String, custom: String="") {
        val next=rules(active,mode);val simple=read(File(active,"simple_dns.json"))
        fun setting(key:String,fallback:String):String=simple.entrySet().firstOrNull { it.key.equals(key,true) }?.value?.asString?.takeIf { it.isNotBlank() }?:fallback
        val remote=MoskomPolicy.dns(dns,custom)?:setting("remoteDNS","https://1.1.1.1/dns-query")
        val direct=setting("directDNS","77.88.8.8")
        val index=when(mode){"all"->0;"except_ru"->1;"blocked"->2;else->error("Unknown routing mode")}
        val strategy=read(File(active,"template.json")).getAsJsonArray("routingItems")[index].asJsonObject.text("domainStrategy","AsIs")
        require(strategy in listOf("AsIs","IPOnDemand","IPIfNonMatch"))
        val keys=listOf("moskom_route_mode","moskom_dns","moskom_custom_dns",AppConfig.PREF_REMOTE_DNS,AppConfig.PREF_DOMESTIC_DNS,AppConfig.PREF_ROUTING_DOMAIN_STRATEGY)
        val old=keys.associateWith { MmkvManager.decodeSettingsString(it) };val oldRules=MmkvManager.decodeRoutingRulesets()
        val oldLocalDns=MmkvManager.decodeSettingsBool(AppConfig.PREF_LOCAL_DNS_ENABLED,false)
        try {
            MmkvManager.encodeRoutingRulesets(next)
            val values=listOf(mode,dns,custom,remote,direct,strategy)
            keys.zip(values).forEach { (key,value)->require(MmkvManager.encodeSettings(key,value)) }
            require(MmkvManager.encodeSettings(AppConfig.PREF_LOCAL_DNS_ENABLED,true))
        } catch(error:Exception) {MmkvManager.encodeRoutingRulesets(oldRules);old.forEach { (k,v)->MmkvManager.encodeSettings(k,v) };MmkvManager.encodeSettings(AppConfig.PREF_LOCAL_DNS_ENABLED,oldLocalDns);throw error}
    }
    private fun activate(stage: File) {
        for(name in listOf("geoip.dat","geosite.dat","route-0.json","route-1.json","route-2.json","manifest.json","simple_dns.json"))require(File(stage,name).isFile)
        val target=File(root,"moskom-bundle-${UUID.randomUUID()}");require(stage.renameTo(target))
        val previous=active;val oldName=MmkvManager.decodeSettingsString("moskom_route_bundle")
        val oldRules=MmkvManager.decodeRoutingRulesets()
        val oldSettings=listOf("moskom_route_mode","moskom_dns","moskom_custom_dns",AppConfig.PREF_REMOTE_DNS,AppConfig.PREF_DOMESTIC_DNS,AppConfig.PREF_ROUTING_DOMAIN_STRATEGY,"moskom_rules_stamp").associateWith { MmkvManager.decodeSettingsString(it) }
        val oldLocalDns=MmkvManager.decodeSettingsBool(AppConfig.PREF_LOCAL_DNS_ENABLED,false)
        try {active=target;apply(mode(),dns(),customDns());require(MmkvManager.encodeSettings("moskom_route_bundle",target.name));MmkvManager.encodeSettings("moskom_rules_stamp",read(File(target,"manifest.json")).text("geoRelease"))}
        catch(error:Exception){active=previous;MmkvManager.encodeSettings("moskom_route_bundle",oldName);MmkvManager.encodeRoutingRulesets(oldRules);oldSettings.forEach{(k,v)->MmkvManager.encodeSettings(k,v)};MmkvManager.encodeSettings(AppConfig.PREF_LOCAL_DNS_ENABLED,oldLocalDns);target.deleteRecursively();throw error}
    }
    fun update(progress:(Int)->Unit): Boolean {
        val route=http.json("https://api.github.com/repos/runetfreedom/russia-v2ray-custom-routing-list/commits/main").text("sha")
        val geo=http.json("https://api.github.com/repos/runetfreedom/russia-v2ray-rules-dat/releases/latest");val release=geo.text("tag_name")
        require(Regex("[a-f0-9]{40}").matches(route)&&Regex("[0-9]{12}").matches(release))
        val current=read(File(active,"manifest.json"));if(current.text("routingCommit")==route&&current.text("geoRelease")==release)return false
        val stage=File(root,"moskom-stage-${UUID.randomUUID()}");stage.mkdirs()
        try {
            val base="https://raw.githubusercontent.com/runetfreedom/russia-v2ray-custom-routing-list/$route/v2rayN/"
            fun text(remote:String,local:String){require(Regex("[A-Za-z0-9_.-]{1,100}").matches(remote)&&!remote.contains(".."));File(stage,local).writeBytes(http.githubBytes(base+remote,4*1024*1024))}
            text("template.json","template.json");val template=read(File(stage,"template.json"));val items=template.getAsJsonArray("routingItems");require(items.size()==3)
            items.forEachIndexed { i,item->text(item.asJsonObject.text("url").substringAfterLast('/'),"route-$i.json") };text("simple_dns.json","simple_dns.json")
            for(name in listOf("geoip.dat","geosite.dat")) {
                val asset=geo.getAsJsonArray("assets").map { it.asJsonObject }.first { it.text("name")==name }
                val expected="https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/download/$release/$name";require(asset.text("browser_download_url")==expected)
                val digest=asset.text("digest");require(Regex("sha256:[a-f0-9]{64}").matches(digest));http.download(expected,File(stage,name),160L*1024*1024,digest.removePrefix("sha256:"),progress)
            }
            File(stage,"manifest.json").writeText(JsonObject().apply {addProperty("routingCommit",route);addProperty("geoRelease",release)}.toString())
            activate(stage);return true
        } finally { stage.deleteRecursively() }
    }
}
