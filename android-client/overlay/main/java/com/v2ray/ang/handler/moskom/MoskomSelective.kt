package com.v2ray.ang.handler.moskom

import com.google.gson.Gson
import com.v2ray.ang.dto.entities.RulesetItem

/** Deterministic catalog/routing decisions; Android package visibility stays in MoskomRoutes. */
data class MoskomInstalledApp(val id:String,val name:String)
data class MoskomServiceChoice(val id:String="",val name:String="",val domains:List<String> = emptyList(),val ips:List<String> = emptyList(),val mediaIps:List<String> = emptyList(),val mediaPorts:List<String> = emptyList())
data class MoskomServiceCatalog(val version:Int=0,val services:List<MoskomServiceChoice> = emptyList())
object MoskomSelective {
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
