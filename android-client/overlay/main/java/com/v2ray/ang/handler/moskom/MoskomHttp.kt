package com.v2ray.ang.handler.moskom

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.v2ray.ang.BuildConfig
import com.v2ray.ang.handler.MmkvManager
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.io.IOException
import java.security.KeyStore
import java.security.MessageDigest
import java.util.concurrent.TimeUnit
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class MoskomAuthException : IOException()

/** Owns only the service account session; profile and preference storage stays in MMKV. */
class MoskomHttp(private val context: Context) {
    companion object { @Volatile var fixtureSite: String? = null }
    val site: String = fixtureSite?.takeIf { BuildConfig.DEBUG && (it.startsWith("http://10.0.2.2:") || it.startsWith("http://127.0.0.1:")) } ?: MoskomPolicy.SITE
    private val cookies = mutableListOf<Cookie>()
    private val client = OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).callTimeout(25, TimeUnit.SECONDS).cookieJar(object : CookieJar {
        override fun saveFromResponse(url: HttpUrl, values: List<Cookie>) {
            synchronized(cookies) { values.forEach { next -> cookies.removeAll { it.name == next.name && it.domain == next.domain && it.path == next.path }; cookies.add(next) } }
        }
        override fun loadForRequest(url: HttpUrl): List<Cookie> = synchronized(cookies) { cookies.filter { it.matches(url) && it.expiresAt > System.currentTimeMillis() } }
    }).build()
    private val publicClient = OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).connectTimeout(15, TimeUnit.SECONDS).callTimeout(10, TimeUnit.MINUTES).build()
    private var csrf = ""
    @Volatile private var closed = false
    init { restore() }
    private fun bytes(request: Request, limit: Long, public: Boolean = false): ByteArray = (if (public) publicClient else client).newCall(request).execute().use { response ->
        if (response.code == 401) throw MoskomAuthException()
        val data = response.body.byteStream().use { input ->
            val result = java.io.ByteArrayOutputStream(); val buffer = ByteArray(16384)
            while (true) { val n = input.read(buffer); if (n < 0) break; if (result.size() + n > limit) throw IOException("Response too large"); result.write(buffer, 0, n) }
            result.toByteArray()
        }
        if (!response.isSuccessful) throw IOException("HTTP ${response.code}")
        data
    }
    fun api(action: String, body: JsonObject? = null): JsonObject {
        check(!closed)
        if (body != null && csrf.isBlank()) me()
        fun request(): Request = Request.Builder().url(site + "api/account.php?action=" + action).apply {
            if (body != null) { header("Origin", site.trimEnd('/')); header("X-CSRF-Token", csrf); post(body.toString().toRequestBody("application/json; charset=utf-8".toMediaType())) }
        }.build()
        val response = client.newCall(request()).execute()
        val data = response.use { r ->
            val text = String(r.body.byteStream().use { it.readBytesBounded(2 * 1024 * 1024) }, Charsets.UTF_8)
            val json = JsonParser.parseString(text).asJsonObject
            if (r.code == 401) throw MoskomAuthException()
            if (r.code == 403 && json.text("code") == "csrf_refresh" && body != null) {
                csrf = json.text("csrf"); return@use null
            }
            if (!r.isSuccessful) throw IOException("HTTP ${r.code}")
            json
        } ?: JsonParser.parseString(String(bytes(request(), 2L * 1024 * 1024))).asJsonObject
        if (data.has("csrf")) csrf = data.text("csrf")
        return data
    }
    fun me(): JsonObject = api("me")
    fun siteJson(path: String): JsonObject = JsonParser.parseString(String(bytes(Request.Builder().url(site + path).build(),2L*1024*1024, true))).asJsonObject
    fun save() {
        val plain = synchronized(cookies) { cookies.joinToString("\n") }.toByteArray()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.ENCRYPT_MODE, key())
        val sealed = cipher.iv + cipher.doFinal(plain)
        require(MmkvManager.encodeSettings("moskom_session", Base64.encodeToString(sealed, Base64.NO_WRAP)))
    }
    fun clear() { synchronized(cookies) { cookies.clear() }; csrf = ""; MmkvManager.encodeSettings("moskom_session", "") }
    private fun restore() {
        val encoded = MmkvManager.decodeSettingsString("moskom_session").orEmpty(); if (encoded.isBlank()) return
        runCatching {
            val data = Base64.decode(encoded, Base64.NO_WRAP); require(data.size > 28)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding"); cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, data.copyOfRange(0,12)))
            val root = site.toHttpUrl(); val values = String(cipher.doFinal(data.copyOfRange(12,data.size))).lineSequence().mapNotNull { Cookie.parse(root,it) }.toList()
            synchronized(cookies) { cookies.addAll(values) }
        }.onFailure { clear() }
    }
    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }; val alias = "moskom-account-session-v1"
        (store.getKey(alias,null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder(alias,KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun subscription(url: String): String {
        check(!closed)
        require(MoskomPolicy.validSubscriptionUrl(url))
        var next = url.toHttpUrl()
        repeat(6) {
            publicClient.newCall(Request.Builder().url(next).build()).execute().use { r ->
                if (r.isRedirect) { next = next.resolve(r.header("Location").orEmpty()) ?: throw IOException("Invalid redirect") }
                else {
                    if (!r.isSuccessful) throw IOException("Subscription HTTP ${r.code}")
                    val data = r.body.byteStream().use { stream -> stream.readBytesBounded(2 * 1024 * 1024) }
                    return String(data,Charsets.UTF_8)
                }
            }
        }
        throw IOException("Too many redirects")
    }
    fun json(url: String): JsonObject = JsonParser.parseString(String(githubBytes(url,2 * 1024 * 1024))).asJsonObject
    fun githubBytes(url: String, limit: Int): ByteArray = openGithub(url).use { r -> r.body.byteStream().use { it.readBytesBounded(limit) } }
    private fun openGithub(url: String): okhttp3.Response {
        check(!closed)
        var next = url.toHttpUrl()
        repeat(6) {
            require(next.scheme == "https" && next.username.isEmpty() && next.password.isEmpty() && next.port == 443 && next.host in setOf("api.github.com","github.com","raw.githubusercontent.com","release-assets.githubusercontent.com","objects.githubusercontent.com"))
            val response = publicClient.newCall(Request.Builder().url(next).header("User-Agent","MoskomVPN-Android/${BuildConfig.VERSION_NAME}").build()).execute()
            if (response.isRedirect) { val location=response.header("Location");response.close();next=next.resolve(location.orEmpty())?:throw IOException("Invalid redirect") }
            else { if (!response.isSuccessful) { val code=response.code;response.close();throw IOException("Update HTTP $code") };return response }
        }
        throw IOException("Too many redirects")
    }
    fun download(url: String, target: File, limit: Long, sha: String?, progress: (Int) -> Unit = {}) {
        target.parentFile?.mkdirs();val part=File(target.path+".part")
        try {
            openGithub(url).use { response ->
                val hash=MessageDigest.getInstance("SHA-256");var size=0L;val total=response.body.contentLength()
                part.outputStream().use { output -> response.body.byteStream().use { input -> val buffer=ByteArray(65536)
                    while(true){val n=input.read(buffer);if(n<0)break;size+=n;if(size>limit)throw IOException("File too large");hash.update(buffer,0,n);output.write(buffer,0,n);if(total>0)progress((size*100/total).coerceAtMost(100).toInt())}
                } }
                require(sha==null || hash.digest().joinToString(""){"%02x".format(it)}==sha.lowercase())
                if(total>=0)require(size==total)
                if(!part.renameTo(target))throw IOException("Unable to save file")
            }
        } finally { part.delete() }
    }
    fun cancel() { client.dispatcher.cancelAll(); publicClient.dispatcher.cancelAll() }
    fun close() { closed=true;cancel() }
    private fun java.io.InputStream.readBytesBounded(limit: Int): ByteArray {
        val output=java.io.ByteArrayOutputStream();val buffer=ByteArray(16384)
        while(true){val n=read(buffer);if(n<0)break;if(output.size()+n>limit)throw IOException("Response too large");output.write(buffer,0,n)}
        return output.toByteArray()
    }
}
