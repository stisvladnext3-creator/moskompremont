package com.v2ray.ang.handler.moskom

import com.google.gson.Gson
import com.v2ray.ang.dto.entities.RulesetItem

/** Deterministic catalog/routing decisions; Android package visibility stays in MoskomRoutes. */
data class MoskomInstalledApp(val id:String,val name:String)
data class MoskomServiceChoice(val id:String="",val name:String="",val domains:List<String> = emptyList(),val ips:List<String> = emptyList(),val mediaIps:List<String> = emptyList(),val mediaPorts:List<String> = emptyList())
data class MoskomServiceCatalog(val version:Int=0,val services:List<MoskomServiceChoice> = emptyList())
object MoskomSelective {
    // These modes promise ordinary domestic connectivity. Native package bypass
    // also avoids bank/transport apps treating a routed-direct VPN as a VPN path.
    fun directDomestic(mode:String):Boolean = mode in setOf("blocked","except_ru","services")
    val domesticPackages:Set<String> = setOf(
        "com.sberbankmobile","ru.sberbankmobile","ru.sberbank.sbbol",
        "com.idamob.tinkoff.android","ru.vtb24.mobilebanking.android","ru.alfabank.mobile.android",
        "ru.ozon.fintech","ru.ozon.bank","ru.gazprombank.android.mobilebank.app","ru.rshb.dbo",
        "ru.raiffeisennews","ru.psb.psb","ru.mkb.mobile","ru.sovcomcard.halva.v1",
        "ru.pochta.mobilebank","ru.ubrr.bank","ru.homecredit.mycredit","ru.akbars.mobile",
        "com.punicapp.whoosh","ru.yandex.yandexmaps","ru.yandex.yandexnavi","ru.yandex.searchplugin",
        "ru.yandex.taxi","ru.yandex.eda","ru.yandex.market","ru.yandex.disk","ru.yandex.metro","ru.yandex.weatherplugin"
    )
    fun domesticBypass(mode:String,installed:Set<String>):Set<String> = if(directDomestic(mode)) domesticPackages.intersect(installed) else emptySet()
    fun domesticPriority(mode:String,bundle:String,rules:MutableList<RulesetItem>):MutableList<RulesetItem> {
        if(!directDomestic(mode))return rules
        require(Regex("[A-Za-z0-9_-]{1,100}").matches(bundle))
        val domains=listOf("category-bank-ru","yandex","whoosh").map{"ext:$bundle/geosite.dat:$it"}+
            listOf("yandex.ru","yandex.com","yandex.net","ya.ru","yastatic.net","yandex.st","whoosh.bike","whoosh.app",
                "sberbank.ru","sber.ru","tbank.ru","tinkoff.ru","vtb.ru","alfabank.ru","ozonbank.ru","ozon.ru").map{"domain:$it"}
        // Preset ad blocking also becomes DNS hosts=loopback in Xray, which
        // outranks route exceptions and can break bank/transport SDK requests.
        val normal=rules.filterNot{rule->rule.outboundTag=="block"&&rule.domain.orEmpty().any{it.endsWith(":category-ads-all")}}
        return (listOf(RulesetItem(id="moskom-domestic-direct",domain=domains,outboundTag="direct"))+normal).toMutableList()
    }
    fun catalog(json:String):List<MoskomServiceChoice> {
        val catalog=Gson().fromJson(json,MoskomServiceCatalog::class.java)
        require(catalog.version==1&&catalog.services.size in 1..200&&catalog.services.distinctBy{it.id}.size==catalog.services.size)
        return catalog.services
    }
    fun nativeAppPolicyAllowed(selective:Boolean,bypass:Boolean,allowed:Int):Boolean = !selective||(!bypass&&allowed>0)
    fun ready(mode:String,apps:Set<String>,services:Set<String>,installed:Set<String>,catalog:Set<String>):Boolean = when(mode) {
        "apps"->apps.any{it in installed};"services"->services.any{it in catalog};else->true
    }
    fun services(ids:Set<String>,catalog:List<MoskomServiceChoice>):MutableList<RulesetItem> {
        require(ids.size<=200&&ids.all{id->catalog.any{it.id==id}})
        val selected=catalog.filter{it.id in ids};val rules=mutableListOf<RulesetItem>()
        val domains=selected.flatMap{it.domains}.distinct();val ips=selected.flatMap{it.ips}.distinct()
        if(domains.isNotEmpty())rules.add(RulesetItem(id="moskom-service-domains",domain=domains,outboundTag="proxy"))
        if(ips.isNotEmpty())rules.add(RulesetItem(id="moskom-service-ips",ip=ips,outboundTag="proxy"))
        selected.filter{it.mediaIps.isNotEmpty()&&it.mediaPorts.isNotEmpty()}.forEach { service->
            rules.add(RulesetItem(id="moskom-media-${service.id}",ip=service.mediaIps,port=service.mediaPorts.joinToString(","){it.replace(':','-')},network="udp",outboundTag="proxy"))
        }
        rules.add(RulesetItem(id="moskom-service-direct",port="0-65535",outboundTag="direct"))
        return rules
    }
}
