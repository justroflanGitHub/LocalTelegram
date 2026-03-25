package org.localtelegram.webrtc

import android.util.Log
import okhttp3.*
import okhttp3.internal.ws.RealWebSocket
import okio.ByteString
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * Signalling message types for WebRTC negotiation
 */
enum class SignallingMessageType {
    JOIN_ROOM,
    LEAVE_ROOM,
    OFFER,
    ANSWER,
    ICE_CANDIDATE,
    USER_JOINED,
    USER_LEFT,
    ROOM_STATE,
    ERROR,
    UNKNOWN
}

/**
 * Represents a participant in a conference room
 */
data class RoomParticipant(
    val userId: String,
    val displayName: String,
    val hasAudio: Boolean = false,
    val hasVideo: Boolean = false,
    val isMuted: Boolean = false,
    val isVideoEnabled: Boolean = true,
    val isScreenSharing: Boolean = false,
    val isModerator: Boolean = false
)

/**
 * Represents the current state of a conference room
 */
data class RoomState(
    val roomId: String,
    val participants: List<RoomParticipant>,
    val recordingEnabled: Boolean = false,
    val recordingStatus: String = ""
)

/**
 * ICE candidate information
 */
data class IceCandidateData(
    val candidate: String,
    val sdpMid: String,
    val sdpMlineIndex: Int
)

/**
 * Signalling message structure
 */
data class SignallingMessage(
    val type: SignallingMessageType,
    val roomId: String? = null,
    val userId: String? = null,
    val targetUserId: String? = null,
    val data: JSONObject? = null,
    val error: String? = null
) {
    companion object {
        fun fromJson(json: JSONObject): SignallingMessage {
            val typeStr = json.optString("type", "")
            val type = when (typeStr) {
                "joinRoom" -> SignallingMessageType.JOIN_ROOM
                "leaveRoom" -> SignallingMessageType.LEAVE_ROOM
                "offer" -> SignallingMessageType.OFFER
                "answer" -> SignallingMessageType.ANSWER
                "iceCandidate" -> SignallingMessageType.ICE_CANDIDATE
                "userJoined" -> SignallingMessageType.USER_JOINED
                "userLeft" -> SignallingMessageType.USER_LEFT
                "roomState" -> SignallingMessageType.ROOM_STATE
                "error" -> SignallingMessageType.ERROR
                else -> SignallingMessageType.UNKNOWN
            }
            
            return SignallingMessage(
                type = type,
                roomId = json.optString("roomId"),
                userId = json.optString("userId"),
                targetUserId = json.optString("targetUserId"),
                data = json.optJSONObject("data"),
                error = json.optString("error")
            )
        }
    }
    
    fun toJson(): JSONObject {
        val json = JSONObject()
        val typeStr = when (type) {
            SignallingMessageType.JOIN_ROOM -> "joinRoom"
            SignallingMessageType.LEAVE_ROOM -> "leaveRoom"
            SignallingMessageType.OFFER -> "offer"
            SignallingMessageType.ANSWER -> "answer"
            SignallingMessageType.ICE_CANDIDATE -> "iceCandidate"
            SignallingMessageType.USER_JOINED -> "userJoined"
            SignallingMessageType.USER_LEFT -> "userLeft"
            SignallingMessageType.ROOM_STATE -> "roomState"
            SignallingMessageType.ERROR -> "error"
            else -> "unknown"
        }
        
        json.put("type", typeStr)
        roomId?.let { json.put("roomId", it) }
        userId?.let { json.put("userId", it) }
        targetUserId?.let { json.put("targetUserId", it) }
        data?.let { json.put("data", it) }
        error?.let { json.put("error", it) }
        
        return json
    }
}

/**
 * WebSocket-based signalling client for WebRTC negotiation
 * 
 * Connects to the ConferenceService SignallingHub for:
 * - Room join/leave operations
 * - SDP offer/answer exchange
 * - ICE candidate exchange
 * - Room state updates
 */
class SignallingClient {
    
    companion object {
        private const val TAG = "SignallingClient"
    }
    
    private var webSocket: WebSocket? = null
    private var currentRoomId: String? = null
    private var currentUserId: String? = null
    private var accessToken: String? = null
    private var connected = false
    
    private val client = OkHttpClient.Builder()
        .pingInterval(30, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()
    
    // Callbacks
    var onConnected: (() -> Unit)? = null
    var onDisconnected: (() -> Unit)? = null
    var onError: ((String) -> Unit)? = null
    var onUserJoined: ((RoomParticipant) -> Unit)? = null
    var onUserLeft: ((String) -> Unit)? = null
    var onRoomState: ((RoomState) -> Unit)? = null
    var onOffer: ((String, String) -> Unit)? = null
    var onAnswer: ((String, String) -> Unit)? = null
    var onIceCandidate: ((String, IceCandidateData) -> Unit)? = null
    
    /**
     * Connect to the signalling server
     */
    fun connect(serverUrl: String, token: String) {
        accessToken = token
        
        var wsUrl = serverUrl
        if (!wsUrl.contains("://")) {
            wsUrl = "wss://$wsUrl"
        }
        wsUrl = "$wsUrl?access_token=$token"
        
        Log.d(TAG, "Connecting to signalling server: $wsUrl")
        
        val request = Request.Builder()
            .url(wsUrl)
            .build()
        
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                Log.d(TAG, "Connected to signalling server")
                connected = true
                onConnected?.invoke()
            }
            
            override fun onMessage(webSocket: WebSocket, text: String) {
                handleMessage(text)
            }
            
            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                Log.d(TAG, "WebSocket closing: $code - $reason")
                webSocket.close(1000, null)
            }
            
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                Log.d(TAG, "WebSocket closed: $code - $reason")
                connected = false
                onDisconnected?.invoke()
            }
            
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                Log.e(TAG, "WebSocket failure: ${t.message}")
                connected = false
                onError?.invoke(t.message ?: "Unknown error")
            }
        })
    }
    
    /**
     * Disconnect from the signalling server
     */
    fun disconnect() {
        webSocket?.close(1000, "Client disconnecting")
        webSocket = null
        connected = false
        currentRoomId = null
    }
    
    /**
     * Check if connected to the signalling server
     */
    fun isConnected(): Boolean = connected
    
    /**
     * Join a conference room
     */
    fun joinRoom(roomId: String, displayName: String) {
        currentRoomId = roomId
        
        val msg = SignallingMessage(
            type = SignallingMessageType.JOIN_ROOM,
            roomId = roomId,
            data = JSONObject().put("displayName", displayName)
        )
        sendMessage(msg)
    }
    
    /**
     * Leave the current room
     */
    fun leaveRoom() {
        currentRoomId?.let { roomId ->
            val msg = SignallingMessage(
                type = SignallingMessageType.LEAVE_ROOM,
                roomId = roomId
            )
            sendMessage(msg)
        }
        currentRoomId = null
    }
    
    /**
     * Send SDP offer
     */
    fun sendOffer(sdp: String) {
        val msg = SignallingMessage(
            type = SignallingMessageType.OFFER,
            roomId = currentRoomId,
            data = JSONObject().put("sdp", sdp)
        )
        sendMessage(msg)
    }
    
    /**
     * Send SDP answer
     */
    fun sendAnswer(targetUserId: String, sdp: String) {
        val msg = SignallingMessage(
            type = SignallingMessageType.ANSWER,
            roomId = currentRoomId,
            targetUserId = targetUserId,
            data = JSONObject().put("sdp", sdp)
        )
        sendMessage(msg)
    }
    
    /**
     * Send ICE candidate
     */
    fun sendIceCandidate(targetUserId: String, candidate: IceCandidateData) {
        val data = JSONObject()
        data.put("candidate", candidate.candidate)
        data.put("sdpMid", candidate.sdpMid)
        data.put("sdpMlineIndex", candidate.sdpMlineIndex)
        
        val msg = SignallingMessage(
            type = SignallingMessageType.ICE_CANDIDATE,
            roomId = currentRoomId,
            targetUserId = targetUserId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Update media state
     */
    fun updateMediaState(hasAudio: Boolean, hasVideo: Boolean) {
        val data = JSONObject()
        data.put("action", "updateMediaState")
        data.put("hasAudio", hasAudio)
        data.put("hasVideo", hasVideo)
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Toggle mute state
     */
    fun toggleMute(muted: Boolean) {
        val data = JSONObject()
        data.put("action", "toggleMute")
        data.put("muted", muted)
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Toggle video state
     */
    fun toggleVideo(enabled: Boolean) {
        val data = JSONObject()
        data.put("action", "toggleVideo")
        data.put("enabled", enabled)
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Start screen sharing
     */
    fun startScreenShare() {
        val data = JSONObject()
        data.put("action", "startScreenShare")
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Stop screen sharing
     */
    fun stopScreenShare() {
        val data = JSONObject()
        data.put("action", "stopScreenShare")
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Mute a participant (moderator only)
     */
    fun muteParticipant(userId: String) {
        val data = JSONObject()
        data.put("action", "muteParticipant")
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            targetUserId = userId,
            data = data
        )
        sendMessage(msg)
    }
    
    /**
     * Kick a participant (moderator only)
     */
    fun kickParticipant(userId: String) {
        val data = JSONObject()
        data.put("action", "kickParticipant")
        
        val msg = SignallingMessage(
            roomId = currentRoomId,
            targetUserId = userId,
            data = data
        )
        sendMessage(msg)
    }
    
    private fun sendMessage(message: SignallingMessage) {
        if (!connected) {
            Log.w(TAG, "Cannot send message: not connected")
            return
        }
        
        val json = message.toJson().toString()
        Log.d(TAG, "Sending message: $json")
        webSocket?.send(json)
    }
    
    private fun handleMessage(text: String) {
        try {
            val json = JSONObject(text)
            val msg = SignallingMessage.fromJson(json)
            
            when (msg.type) {
                SignallingMessageType.ROOM_STATE -> processRoomState(msg.data)
                SignallingMessageType.USER_JOINED -> processUserJoined(msg.data)
                SignallingMessageType.USER_LEFT -> processUserLeft(msg.data)
                SignallingMessageType.OFFER -> processOffer(msg.data)
                SignallingMessageType.ANSWER -> processAnswer(msg.data)
                SignallingMessageType.ICE_CANDIDATE -> processIceCandidate(msg.data)
                SignallingMessageType.ERROR -> {
                    msg.error?.let { onError?.invoke(it) }
                }
                else -> Log.w(TAG, "Unknown message type: ${msg.type}")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error parsing message: ${e.message}")
        }
    }
    
    private fun processRoomState(data: JSONObject?) {
        data?.let {
            val roomId = it.optString("roomId")
            val recordingEnabled = it.optBoolean("recordingEnabled", false)
            val recordingStatus = it.optString("recordingStatus", "")
            
            val participants = mutableListOf<RoomParticipant>()
            val participantsArray = it.optJSONArray("participants")
            participantsArray?.let { array ->
                for (i in 0 until array.length()) {
                    val p = array.getJSONObject(i)
                    participants.add(RoomParticipant(
                        userId = p.optString("userId"),
                        displayName = p.optString("displayName"),
                        hasAudio = p.optBoolean("hasAudio", false),
                        hasVideo = p.optBoolean("hasVideo", false),
                        isMuted = p.optBoolean("isMuted", false),
                        isVideoEnabled = p.optBoolean("isVideoEnabled", true),
                        isScreenSharing = p.optBoolean("isScreenSharing", false),
                        isModerator = p.optBoolean("isModerator", false)
                    ))
                }
            }
            
            onRoomState?.invoke(RoomState(
                roomId = roomId,
                participants = participants,
                recordingEnabled = recordingEnabled,
                recordingStatus = recordingStatus
            ))
        }
    }
    
    private fun processUserJoined(data: JSONObject?) {
        data?.let {
            val participant = RoomParticipant(
                userId = it.optString("userId"),
                displayName = it.optString("displayName"),
                hasAudio = it.optBoolean("hasAudio", false),
                hasVideo = it.optBoolean("hasVideo", false),
                isMuted = it.optBoolean("isMuted", false),
                isVideoEnabled = it.optBoolean("isVideoEnabled", true),
                isScreenSharing = it.optBoolean("isScreenSharing", false),
                isModerator = it.optBoolean("isModerator", false)
            )
            onUserJoined?.invoke(participant)
        }
    }
    
    private fun processUserLeft(data: JSONObject?) {
        data?.let {
            val userId = it.optString("userId")
            onUserLeft?.invoke(userId)
        }
    }
    
    private fun processOffer(data: JSONObject?) {
        data?.let {
            val userId = it.optString("userId")
            val sdp = it.optString("sdp")
            onOffer?.invoke(userId, sdp)
        }
    }
    
    private fun processAnswer(data: JSONObject?) {
        data?.let {
            val userId = it.optString("userId")
            val sdp = it.optString("sdp")
            onAnswer?.invoke(userId, sdp)
        }
    }
    
    private fun processIceCandidate(data: JSONObject?) {
        data?.let {
            val userId = it.optString("userId")
            val candidate = IceCandidateData(
                candidate = it.optString("candidate"),
                sdpMid = it.optString("sdpMid"),
                sdpMlineIndex = it.optInt("sdpMlineIndex", 0)
            )
            onIceCandidate?.invoke(userId, candidate)
        }
    }
}
