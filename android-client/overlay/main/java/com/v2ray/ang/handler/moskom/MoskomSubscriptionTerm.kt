package com.v2ray.ang.handler.moskom

import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import kotlin.math.ceil

/** Only these public term fields are cached, never a subscription URL or secret. */
data class MoskomTermData(val kind:String="unspecified",val expiresAt:String?=null,val expired:Boolean=false)

class MoskomSubscriptionTerm private constructor(val data:MoskomTermData,private val seconds:Long?,private val observed:Long) {
    companion object {
        fun create(data:MoskomTermData,remaining:Long?,now:Instant,elapsedMillis:Long):MoskomSubscriptionTerm {
            val seconds=if(data.kind=="fixed") remaining?.coerceAtLeast(0)
                ?: data.expiresAt?.let(MoskomPolicy::time)?.let{ceil((it.toEpochMilli()-now.toEpochMilli())/1000.0).toLong().coerceAtLeast(0)} else null
            return MoskomSubscriptionTerm(data,seconds,elapsedMillis)
        }
    }
    fun remaining(elapsedMillis:Long):Long?=seconds?.let{(it-(elapsedMillis-observed).coerceAtLeast(0)/1000).coerceAtLeast(0)}
    fun state(elapsedMillis:Long):String=when {
        data.expired||remaining(elapsedMillis)==0L->"expired"
        data.kind=="lifetime"->"lifetime"
        data.kind=="fixed"&&seconds!=null->"fixed"
        else->"unspecified"
    }
    fun text(elapsedMillis:Long):String {
        when(state(elapsedMillis)){"expired"->return "Срок истёк";"lifetime"->return "Бессрочная подписка";"unspecified"->return "Срок не указан"}
        val value=remaining(elapsedMillis)?:return "Срок не указан"
        val days=value/86400;val hours=value%86400/3600;val minutes=value%3600/60
        return when {days>0->"Осталось: $days дн. $hours ч.";hours>0->"Осталось: $hours ч. $minutes мин.";minutes>0->"Осталось: $minutes мин.";else->"Осталось меньше минуты"}
    }
    val endText:String? get()=if(data.kind=="fixed")data.expiresAt?.let(MoskomPolicy::time)?.let {
        "Действует до: "+DateTimeFormatter.ofPattern("dd.MM.yyyy, HH:mm").withZone(ZoneOffset.ofHours(3)).format(it)+" (МСК)"
    }else null
}
