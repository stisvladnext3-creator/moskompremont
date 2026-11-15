package com.v2ray.ang.handler.moskom

import com.google.gson.Gson
import com.v2ray.ang.dto.entities.RulesetItem
import java.net.IDN
import java.net.URI
import java.util.Locale

/** Deterministic catalog/routing decisions; Android package visibility stays in MoskomRoutes. */
data class MoskomInstalledApp(val id:String,val name:String)
data class MoskomServiceChoice(val id:String="",val name:String="",val domains:List<String> = emptyList(),val ips:List<String> = emptyList(),val mediaIps:List<String> = emptyList(),val mediaPorts:List<String> = emptyList())
data class MoskomServiceCatalog(val version:Int=0,val services:List<MoskomServiceChoice> = emptyList())
data class MoskomChoiceFilter(val query:String="",val apps:List<MoskomInstalledApp> = emptyList(),val services:List<MoskomServiceChoice> = emptyList())
data class MoskomDnsFallback(val servers:List<String>,val direct:Boolean)
object MoskomSelective {
    fun domainInput(value:String):String = value.take(16385)
    fun customDomains(text:String):List<String> {
        require(text.length<=16384)
        val result=linkedSetOf<String>()
        for(token in text.split(Regex("[\\s,;]+")).filter{it.isNotEmpty()}) {
            var value=token
            val exact=value.startsWith("full:",ignoreCase=true)
            if(exact)value=value.substring(5) else if(value.startsWith("domain:",ignoreCase=true))value=value.substring(7)
            if(value.startsWith("http://",ignoreCase=true)||value.startsWith("https://",ignoreCase=true)) {
                val uri=try{URI(value)}catch(_:java.net.URISyntaxException){throw IllegalArgumentException()};val authority=uri.rawAuthority ?: throw IllegalArgumentException()
                require(uri.rawUserInfo==null&&!authority.contains('@'))
                if(uri.host!=null)value=uri.host else {
                    require(Regex("[^:]+(?::[0-9]{1,5})?").matches(authority))
                    if(authority.contains(':'))require(authority.substringAfter(':').toInt() in 1..65535)
                    value=authority.substringBefore(':')
                }
            }
            value=value.removePrefix("*.").removePrefix(".").trimEnd('.')
            value=IDN.toASCII(value,IDN.USE_STD3_ASCII_RULES).lowercase(Locale.ROOT)
            require(value.length in 1..253&&!Regex("[0-9.]+").matches(value)&&Regex("[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*").matches(value))
            result.add((if(exact)"full:" else "domain:")+value)
            require(result.size<=200)
        }
        return result.toList()
    }
    fun dnsFallback(mode:String,remote:List<String>,domestic:List<String>):MoskomDnsFallback {
        val direct=mode=="services";val servers=if(direct)domestic else remote
        require(servers.isNotEmpty())
        return MoskomDnsFallback(servers,direct)
    }
    fun searchQuery(value:String):String = value.take(200)
    fun filterChoices(query:String,apps:List<MoskomInstalledApp>,services:List<MoskomServiceChoice>):MoskomChoiceFilter {
        val bounded=searchQuery(query);val needle=bounded.trim()
        fun matches(vararg values:String)=values.any{it.contains(needle,ignoreCase=true)}
        return MoskomChoiceFilter(bounded,
            apps.filter{matches(it.name,it.id)},
            services.filter{matches(it.name,it.id,*it.domains.toTypedArray())})
    }
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
        val personal=normal.filter{it.id=="moskom-custom-domains"}
        return (personal+listOf(RulesetItem(id="moskom-domestic-direct",domain=domains,outboundTag="direct"))+normal.filterNot{it.id=="moskom-custom-domains"}).toMutableList()
    }
    fun catalog(json:String):List<MoskomServiceChoice> {
        val catalog=Gson().fromJson(json,MoskomServiceCatalog::class.java)
        require(catalog.version==1&&catalog.services.size in 1..200&&catalog.services.distinctBy{it.id}.size==catalog.services.size)
        return catalog.services
    }
    fun nativeAppPolicyAllowed(selective:Boolean,bypass:Boolean,allowed:Int):Boolean = !selective||(!bypass&&allowed>0)
    fun ready(mode:String,apps:Set<String>,services:Set<String>,installed:Set<String>,catalog:Set<String>,custom:List<String> = emptyList()):Boolean = when(mode) {
        "apps"->apps.any{it in installed};"services"->services.any{it in catalog}||custom.isNotEmpty();else->true
    }
    fun services(ids:Set<String>,catalog:List<MoskomServiceChoice>,custom:List<String> = emptyList()):MutableList<RulesetItem> {
        require(ids.size<=200&&ids.all{id->catalog.any{it.id==id}})
        val selected=catalog.filter{it.id in ids};val rules=mutableListOf<RulesetItem>()
        val personal=customDomains(custom.joinToString("\n"))
        if(personal.isNotEmpty())rules.add(RulesetItem(id="moskom-custom-domains",domain=personal,outboundTag="proxy"))
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
