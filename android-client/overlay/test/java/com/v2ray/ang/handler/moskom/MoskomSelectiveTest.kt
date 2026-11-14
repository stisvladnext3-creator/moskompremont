package com.v2ray.ang.handler.moskom
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test

class MoskomSelectiveTest {
    private fun catalog()=javaClass.getResourceAsStream("/service-catalog.json")!!.bufferedReader().use{MoskomSelective.catalog(it.readText())}
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
}
