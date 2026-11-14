package com.v2ray.ang.ui.main
import com.v2ray.ang.R
internal fun moskomServiceLabel(id:String):Int? = when(id) {
    "youtube" -> R.string.moskom_service_youtube
    "chatgpt" -> R.string.moskom_service_chatgpt
    "telegram" -> R.string.moskom_service_telegram
    "whatsapp" -> R.string.moskom_service_whatsapp
    "flibusta" -> R.string.moskom_service_flibusta
    "gemini" -> R.string.moskom_service_gemini
    "kimi" -> R.string.moskom_service_kimi
    "deepseek" -> R.string.moskom_service_deepseek
    "perplexity" -> R.string.moskom_service_perplexity
    "claude" -> R.string.moskom_service_claude
    "cursor" -> R.string.moskom_service_cursor
    "instagram" -> R.string.moskom_service_instagram
    "speedtest" -> R.string.moskom_service_speedtest
    "discord" -> R.string.moskom_service_discord
    "viber" -> R.string.moskom_service_viber
    "snapchat" -> R.string.moskom_service_snapchat
    "netflix" -> R.string.moskom_service_netflix
    "spotify" -> R.string.moskom_service_spotify
    "x" -> R.string.moskom_service_x
    "facebook" -> R.string.moskom_service_facebook
    "tiktok" -> R.string.moskom_service_tiktok
    "playstation" -> R.string.moskom_service_playstation
    "signal" -> R.string.moskom_service_signal
    "notion" -> R.string.moskom_service_notion
    "miro" -> R.string.moskom_service_miro
    "facetime" -> R.string.moskom_service_facetime
    "google-meet" -> R.string.moskom_service_google_meet
    else -> null
}
