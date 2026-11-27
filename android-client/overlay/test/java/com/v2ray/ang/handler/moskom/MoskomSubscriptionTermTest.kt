package com.v2ray.ang.handler.moskom

import com.google.gson.JsonParser
import org.junit.jupiter.api.Assertions.*
import org.junit.jupiter.api.Test
import java.time.Instant

class MoskomSubscriptionTermTest {
    private val now=Instant.parse("2026-11-27T20:00:00Z")
    private fun term(kind:String,seconds:Long?,end:String?=null,expired:Boolean=false)=MoskomSubscriptionTerm.create(MoskomTermData(kind,end,expired),seconds,now,1000)
    @Test fun `labels match cabinet countdown at minute hour and day boundaries`() {
        for((seconds,text) in listOf(223200L to "Осталось: 2 дн. 14 ч.",7260L to "Осталось: 2 ч. 1 мин.",120L to "Осталось: 2 мин.",59L to "Осталось меньше минуты",0L to "Срок истёк"))assertEquals(text,term("fixed",seconds).text(1000))
        assertEquals("Бессрочная подписка",term("lifetime",null).text(100000))
        assertEquals("Срок не указан",term("unspecified",null).text(0));assertEquals("Срок не указан",term("fixed",null,"bad-date").text(0))
    }
    @Test fun `monotonic countdown uses server remaining time despite device clock skew`() {
        val sample=term("fixed",120,"2000-01-01 00:00:00")
        assertEquals("Осталось: 2 мин.",sample.text(1000));assertEquals("Осталось: 1 мин.",sample.text(61000));assertEquals("Срок истёк",sample.text(121000))
        assertEquals("Осталось: 2 мин.",sample.text(0))
    }
    @Test fun `UTC SQL and ISO expiry date render in Moscow`() {
        for(end in listOf("2026-11-27 21:30:00","2026-11-27T21:30:00Z"))assertEquals("Действует до: 28.11.2026, 00:30 (МСК)",term("fixed",5400,end).endText)
        assertNull(term("lifetime",null).endText)
    }
    @Test fun `cached expiry ages across restart and a renewal supplies a new deadline`() {
        val data=term("fixed",60,"2026-11-27T20:01:00Z").data
        assertEquals("Срок истёк",MoskomSubscriptionTerm.create(data,null,now.plusSeconds(120),0).text(0))
        assertEquals("Осталось: 1 дн. 0 ч.",term("fixed",86400,"2026-11-28T20:00:00Z").text(1000))
    }
    @Test fun `website term metadata survives parsing without assuming unlimited`() {
        val json=JsonParser.parseString("""[{"id":1,"url":"https://example.test/a","term_kind":"lifetime","remaining_seconds":null},{"id":2,"url":"https://example.test/b","term_kind":"fixed","expires_at":"2027-01-01 00:00:00","remaining_seconds":90000},{"id":3,"url":"https://example.test/c"}]""").asJsonArray
        val rows=MoskomPolicy.subscriptions(json,now,1000)
        assertEquals("Бессрочная подписка",rows[0].term!!.text(1000));assertEquals("Осталось: 1 дн. 1 ч.",rows[1].term!!.text(1000));assertEquals("Срок не указан",rows[2].term!!.text(1000))
    }
}
