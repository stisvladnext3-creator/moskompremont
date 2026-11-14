package com.v2ray.ang.handler.moskom

import com.google.gson.JsonParser
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test
import java.time.Instant

class MoskomPolicyTest {
    @Test fun `subscriptions keep ID URL revision and reject duplicate or foreign protocols`() {
        val body="""[{"id":1,"name":"VPN-A","url":"https://example.test/sub/a","content_updated_at":"1"},{"id":2,"name":"VPN-B","is_expired":true}]"""
        val rows=MoskomPolicy.subscriptions(JsonParser.parseString(body).asJsonArray)
        assertEquals(2,rows.size);assertFalse(rows[0].expired);assertTrue(rows[1].expired)
        assertThrows(IllegalArgumentException::class.java){MoskomPolicy.subscriptions(JsonParser.parseString("""[{"id":1,"url":"https://example.test"},{"id":1,"url":"https://example.test"}]""").asJsonArray)}
        assertFalse(MoskomPolicy.validSubscriptionUrl("file:///private"));assertFalse(MoskomPolicy.validSubscriptionUrl("https://user:pass@example.test/sub"))
    }
    @Test fun `expired UTC timestamp and blank URL are unavailable`() {
        val json=JsonParser.parseString("""[{"id":1,"url":"https://example.test","expires_at":"2026-01-01 00:00:00"},{"id":2,"url":""}]""").asJsonArray
        assertTrue(MoskomPolicy.subscriptions(json,Instant.parse("2026-02-01T00:00:00Z")).all{it.expired})
        assertFalse(MoskomPolicy.fresh("2020-01-01 00:00:00"));assertFalse(MoskomPolicy.fresh("garbage"))
    }
    @Test fun `group selection never falls through an explicitly chosen empty group`() {
        val a=MoskomGroup("A","VPN-A",listOf(MoskomServer("a1","A1")))
        val b=MoskomGroup("B","VPN-B",listOf(MoskomServer("b1","B1"),MoskomServer("b2","B2")))
        val empty=MoskomGroup("E","Empty",emptyList());val groups=listOf(a,b,empty)
        assertEquals(empty,MoskomPolicy.selectedGroup(groups,"E","a1"));assertEquals(b,MoskomPolicy.selectedGroup(groups,"missing","b2"))
        assertEquals(a,MoskomPolicy.selectedGroup(groups,"missing","missing"));assertNull(MoskomPolicy.selectedGroup(emptyList(),"A","a1"))
        assertEquals(b,MoskomPolicy.selectedGroup(groups,"B","a1"));assertEquals("moskom-cabinet-41-802",MoskomPolicy.groupId(41,802))
    }
    @Test fun `DNS choices and custom validation`() {
        assertNull(MoskomPolicy.dns("auto"));assertEquals("https://1.1.1.1/dns-query",MoskomPolicy.dns("cloudflare"));assertEquals("https://dns.google/dns-query",MoskomPolicy.dns("google"));assertEquals("https://dns.quad9.net/dns-query",MoskomPolicy.dns("quad9"))
        assertEquals("8.8.4.4",MoskomPolicy.dns("custom","8.8.4.4"));assertEquals("::1",MoskomPolicy.dns("custom","::1"))
        for(value in listOf("bad","999.1.1.1","http://bad.test/dns-query","https://user:pass@resolver.test/dns-query"))assertThrows(IllegalArgumentException::class.java){MoskomPolicy.dns("custom",value)}
    }
    @Test fun `update versions sort numerically and ignore website releases and foreign installers`() {
        fun release(version:String,wrong:Boolean=false):String {
            val tag="moskom-android-v$version";val prefix="https://github.com/${if(wrong)"foreign/repo" else MoskomPolicy.REPOSITORY}/releases/download/$tag/"
            return """{"tag_name":"$tag","draft":false,"assets":[{"name":"MoskomVPN-Android-$version.apk","browser_download_url":"${prefix}MoskomVPN-Android-$version.apk","size":100},{"name":"SHA256SUMS.txt","browser_download_url":"${prefix}SHA256SUMS.txt"}]}"""
        }
        val array=JsonParser.parseString("[${release("0.1.9")},${release("0.1.10")},${release("0.1.99",true)},{\"tag_name\":\"moskom-vpn-v0.3.99\"}]").asJsonArray
        assertEquals("0.1.10",MoskomPolicy.release(array,"0.1.8")?.version);assertNull(MoskomPolicy.release(array,"0.1.10"))
        assertNull(MoskomPolicy.versionParts("0.1.x"));assertNull(MoskomPolicy.versionParts("0.1.1.2"))
    }
    @Test fun `checksum requires exact APK filename and supports BOM CRLF`() {
        val hash="a".repeat(64);assertEquals(hash,MoskomPolicy.checksum("\uFEFF$hash  app.apk\r\n","app.apk"))
        assertThrows(IllegalStateException::class.java){MoskomPolicy.checksum("$hash  other.apk","app.apk")}
    }
}
