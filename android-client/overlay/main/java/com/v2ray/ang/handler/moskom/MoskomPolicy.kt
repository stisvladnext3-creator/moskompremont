package com.v2ray.ang.handler.moskom

import com.google.gson.JsonArray
import com.google.gson.JsonObject
import java.net.URI
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

data class MoskomUser(val id: Long, val name: String, val email: String)
data class MoskomSubscription(val id: Long, val name: String, val url: String, val revision: String, val expired: Boolean)
data class MoskomServer(val id: String, val name: String)
data class MoskomGroup(val id: String, val name: String, val servers: List<MoskomServer>)
data class MoskomNotice(val enabled: Boolean = false, val level: String = "warning", val message: String = "")
data class MoskomNode(val name: String, val status: String, val ms: Long?, val checked: String)
data class MoskomRelease(val version: String, val code: Long, val url: String, val filename: String, val checksumUrl: String)

fun JsonObject.text(key: String, fallback: String = ""): String = get(key)?.takeUnless { it.isJsonNull }?.asString ?: fallback

object MoskomPolicy {
    const val REPOSITORY = "stisvladnext3-creator/moskompremont"
    const val SITE = "https://moskompremont.ru/"
    const val CABINET = "moskom-cabinet-"
    fun groupId(user: Long, sub: Long) = "$CABINET$user-$sub"
    fun removableSubscription(id: String, defaultId: String) = id.isNotBlank() && id != defaultId && !id.startsWith(CABINET)
    fun selectedGroup(groups: List<MoskomGroup>, groupId: String, serverId: String): MoskomGroup? = groups.firstOrNull { it.id==groupId }
        ?: groups.firstOrNull { it.servers.any { server->server.id==serverId } } ?: groups.firstOrNull()
    fun versionParts(value: String): List<Int>? {
        if (!Regex("\\d+\\.\\d+\\.\\d+").matches(value)) return null
        return value.split('.').map { it.toIntOrNull() ?: return null }
    }
    fun compareVersions(a: String, b: String): Int {
        val left = versionParts(a) ?: return -1; val right = versionParts(b) ?: return 1
        return left.indices.firstNotNullOfOrNull { i -> left[i].compareTo(right[i]).takeIf { it != 0 } } ?: 0
    }
    fun validSubscriptionUrl(value: String): Boolean = runCatching {
        val u = URI(value); u.scheme in listOf("http", "https") && !u.host.isNullOrEmpty() && u.userInfo == null && value.length <= 8192
    }.getOrDefault(false)
    fun subscriptions(json: JsonArray, now: Instant = Instant.now()): List<MoskomSubscription> {
        require(json.size() <= 1000)
        val result = json.map { item ->
            val row = item.asJsonObject; val id = row.text("id").toLong(); require(id > 0)
            val expired = row.text("is_expired") == "true" || row.text("is_expired") == "1" || time(row.text("expires_at"))?.isBefore(now)?.let { it } == true
            val url = row.text("url"); require(expired || url.isBlank() || validSubscriptionUrl(url))
            MoskomSubscription(id, row.text("name").take(200), url, row.text("content_updated_at"), expired || url.isBlank())
        }
        require(result.distinctBy { it.id }.size == result.size); return result
    }
    fun time(value: String): Instant? = runCatching { Instant.parse(value) }.getOrNull()
        ?: runCatching { LocalDateTime.parse(value, DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")).toInstant(ZoneOffset.UTC) }.getOrNull()
    fun fresh(value: String, now: Instant = Instant.now()) = time(value)?.let { it <= now.plusSeconds(60) && it >= now.minusSeconds(300) } ?: false
    fun dns(choice: String, custom: String = ""): String? = when (choice) {
        "auto" -> null
        "cloudflare" -> "https://1.1.1.1/dns-query"
        "google" -> "https://dns.google/dns-query"
        "quad9" -> "https://dns.quad9.net/dns-query"
        "custom" -> custom.trim().also { value ->
            val ipv4 = value.split('.').let { parts -> parts.size == 4 && parts.all { p -> p.isNotEmpty() && p.all(Char::isDigit) && p.toIntOrNull()?.let { it in 0..255 } == true } }
            val ipv6 = value.contains(':') && Regex("[0-9a-fA-F:]+").matches(value) && runCatching { java.net.InetAddress.getByName(value).hostAddress != null }.getOrDefault(false)
            val doh = runCatching { val u = URI(value); u.scheme == "https" && !u.host.isNullOrBlank() && u.userInfo == null && u.fragment == null }.getOrDefault(false)
            require(value.length <= 1024 && (ipv4 || ipv6 || doh))
        }
        else -> error("Unknown DNS choice")
    }
    fun release(array: JsonArray, current: String): MoskomRelease? = array.mapNotNull { element ->
        val release = element.asJsonObject
        if (release.text("draft") == "true" || release.text("prerelease") == "true") return@mapNotNull null
        val tag = release.text("tag_name"); if (!tag.startsWith("moskom-android-v")) return@mapNotNull null
        val version = tag.removePrefix("moskom-android-v"); if (versionParts(version) == null || compareVersions(version, current) <= 0) return@mapNotNull null
        val filename = "MoskomVPN-Android-$version.apk"; val prefix = "https://github.com/$REPOSITORY/releases/download/$tag/"
        val assets = release.getAsJsonArray("assets") ?: return@mapNotNull null
        val apk = assets.firstOrNull { it.asJsonObject.text("name") == filename && it.asJsonObject.text("browser_download_url") == prefix + filename } ?: return@mapNotNull null
        if (apk.asJsonObject.text("size").toLongOrNull() !in 1..512L * 1024 * 1024) return@mapNotNull null
        if (assets.none { it.asJsonObject.text("name") == "SHA256SUMS.txt" && it.asJsonObject.text("browser_download_url") == prefix + "SHA256SUMS.txt" }) return@mapNotNull null
        MoskomRelease(version, versionParts(version)!!.last().toLong(), prefix + filename, filename, prefix + "SHA256SUMS.txt")
    }.maxWithOrNull { a, b -> compareVersions(a.version, b.version) }
    fun checksum(text: String, filename: String): String = text.removePrefix("\uFEFF").lineSequence().mapNotNull { line ->
        Regex("^([a-fA-F0-9]{64}) [ *](.+)$").matchEntire(line.trimEnd('\r'))?.takeIf { it.groupValues[2] == filename }?.groupValues?.get(1)?.lowercase()
    }.firstOrNull() ?: error("Checksum absent")
}
