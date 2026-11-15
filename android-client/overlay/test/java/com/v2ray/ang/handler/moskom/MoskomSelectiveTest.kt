package com.v2ray.ang.handler.moskom
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test

class MoskomSelectiveTest {
    @Test fun `unselected service DNS uses direct fallback and app VPN DNS stays remote`() {
        val remote=listOf("https://1.1.1.1/dns-query");val direct=listOf("77.88.8.8")
        assertEquals(MoskomDnsFallback(direct,true),MoskomSelective.dnsFallback("services",remote,direct))
        for(mode in listOf("apps","all","except_ru","blocked"))assertEquals(MoskomDnsFallback(remote,false),MoskomSelective.dnsFallback(mode,remote,direct))
        assertThrows(IllegalArgumentException::class.java){MoskomSelective.dnsFallback("services",remote,emptyList())}
    }
    private fun catalog()=javaClass.getResourceAsStream("/service-catalog.json")!!.bufferedReader().use{MoskomSelective.catalog(it.readText())}
    @Test fun `choice search handles names packages domains case whitespace and empty results`() {
        val apps=listOf(MoskomInstalledApp("com.example.calc","Калькулятор"),MoskomInstalledApp("com.example.browser","Browser"))
        val all=MoskomSelective.filterChoices("",apps,catalog())
        assertEquals(apps.map{it.id}.toSet(),all.apps.map{it.id}.toSet());assertEquals(27,all.services.size)
        assertEquals(setOf("com.example.calc"),MoskomSelective.filterChoices(" КАЛЬК ",apps,catalog()).apps.map{it.id}.toSet())
        assertEquals(setOf("com.example.browser"),MoskomSelective.filterChoices("EXAMPLE.BROW",apps,catalog()).apps.map{it.id}.toSet())
        assertEquals(setOf("google-meet"),MoskomSelective.filterChoices("  mEEt  ",apps,catalog()).services.map{it.id}.toSet())
        assertEquals(setOf("youtube"),MoskomSelective.filterChoices("youtu.be",apps,catalog()).services.map{it.id}.toSet())
        val absent=MoskomSelective.filterChoices("no-such-fixture-choice",apps,catalog());assertTrue(absent.apps.isEmpty());assertTrue(absent.services.isEmpty())
        assertEquals(200,MoskomSelective.searchQuery("x".repeat(500)).length)
        assertEquals(setOf("google-meet"),MoskomSelective.filterChoices("meet",emptyList(),catalog()).services.map{it.id}.toSet())
        assertTrue(MoskomSelective.filterChoices("",emptyList(),emptyList()).apps.isEmpty())
    }
    @Test fun `catalog includes all 27 requested services and call media`() {
        val rows=catalog();assertEquals(27,rows.size)
        assertEquals(setOf("youtube","chatgpt","telegram","whatsapp","flibusta","gemini","kimi","deepseek","perplexity","claude","cursor","instagram","speedtest","discord","viber","snapchat","netflix","spotify","x","facebook","tiktok","playstation","signal","notion","miro","facetime","google-meet"),rows.map{it.id}.toSet())
        assertTrue(rows.first{it.id=="facetime"}.mediaIps.isNotEmpty());assertTrue(rows.first{it.id=="google-meet"}.mediaPorts.contains("3478"))
    }
    @Test fun `service mode routes selected services and defaults other traffic direct`() {
        val rules=MoskomSelective.services(setOf("youtube","google-meet"),catalog())
        assertEquals("direct",rules.last().outboundTag);assertEquals("0-65535",rules.last().port)
        val domains=rules.flatMap{it.domain.orEmpty()};assertTrue(domains.contains("domain:youtube.com"));assertTrue(domains.contains("full:meet.google.com"));assertFalse(domains.contains("domain:chatgpt.com"))
        val media=rules.first{it.id=="moskom-media-google-meet"};assertEquals("udp",media.network);assertEquals("19302-19309,3478",media.port)
        assertEquals(listOf("direct"),MoskomSelective.services(emptySet(),catalog()).map{it.outboundTag})
        assertThrows(IllegalArgumentException::class.java){MoskomSelective.services(setOf("foreign-id"),catalog())}
    }
    @Test fun `readiness blocks empty and uninstalled application choices`() {
        val valid=setOf("youtube");val installed=setOf("browser")
        assertTrue(MoskomSelective.ready("all",emptySet(),emptySet(),emptySet(),valid))
        assertFalse(MoskomSelective.ready("apps",emptySet(),emptySet(),installed,valid))
        assertFalse(MoskomSelective.ready("apps",setOf("uninstalled"),emptySet(),installed,valid))
        assertTrue(MoskomSelective.ready("apps",setOf("browser"),emptySet(),installed,valid))
        assertFalse(MoskomSelective.ready("services",emptySet(),setOf("missing"),installed,valid))
        assertTrue(MoskomSelective.ready("services",emptySet(),valid,installed,valid))
        assertThrows(IllegalArgumentException::class.java){MoskomSelective.catalog("{\"version\":2,\"services\":[]}")}
    }
    @Test fun `native empty or invalid package list never captures every app`() {
        assertFalse(MoskomSelective.nativeAppPolicyAllowed(true,false,0))
        assertFalse(MoskomSelective.nativeAppPolicyAllowed(true,true,1))
        assertTrue(MoskomSelective.nativeAppPolicyAllowed(true,false,1))
        assertTrue(MoskomSelective.nativeAppPolicyAllowed(false,false,0))
        assertTrue(MoskomSelective.nativeAppPolicyAllowed(false,true,0))
    }

    @Test fun `domestic apps bypass only direct modes and preserve explicit app selection`() {
        val installed=setOf("com.punicapp.whoosh","ru.yandex.yandexmaps","ru.yandex.searchplugin","com.sberbankmobile","com.idamob.tinkoff.android","foreign.app")
        for(mode in listOf("blocked","except_ru","services"))assertEquals(installed-"foreign.app",MoskomSelective.domesticBypass(mode,installed))
        for(mode in listOf("all","apps","invalid"))assertTrue(MoskomSelective.domesticBypass(mode,installed).isEmpty())
        assertTrue(MoskomSelective.domesticBypass("blocked",emptySet()).isEmpty())
        assertFalse(MoskomSelective.domesticPackages.contains("com.yandex.browser"))
    }

    @Test fun `domestic route and DNS domain priority precedes preset ads and proxy rules`() {
        val old=MoskomSelective.services(setOf("youtube"),catalog())
        val bundle="moskom-bundle-11111111-1111-4111-8111-111111111111"
        for(mode in listOf("blocked","except_ru","services")) {
            val rules=MoskomSelective.domesticPriority(mode,bundle,old)
            assertEquals("direct",rules.first().outboundTag)
            for(tag in listOf("category-bank-ru","yandex","whoosh"))assertTrue(rules.first().domain!!.contains("ext:$bundle/geosite.dat:$tag"))
            assertTrue(rules.first().domain!!.contains("domain:ya.ru"));assertEquals(old,rules.drop(1))
            assertEquals("direct",rules.last().outboundTag)
        }
        assertEquals(old,MoskomSelective.domesticPriority("all",bundle,old))
        val ads=com.v2ray.ang.dto.entities.RulesetItem(id="ads",domain=listOf("ext:$bundle/geosite.dat:category-ads-all"),outboundTag="block")
        val custom=com.v2ray.ang.dto.entities.RulesetItem(id="custom",domain=listOf("domain:personal.example"),outboundTag="block")
        val filtered=MoskomSelective.domesticPriority("blocked",bundle,mutableListOf(ads,custom))
        assertFalse(filtered.any{it.id=="ads"});assertTrue(filtered.any{it.id=="custom"})
        assertThrows(IllegalArgumentException::class.java){MoskomSelective.domesticPriority("blocked","../foreign",old)}
    }

}
