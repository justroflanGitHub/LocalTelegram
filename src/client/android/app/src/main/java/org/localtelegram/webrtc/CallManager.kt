package org.localtelegram.webrtc

import android.content.Context
import android.util.Log
import org.webrtc.VideoFrame
import java.util.concurrent.ConcurrentHashMap

/**
 * Call type enumeration
 */
enum class CallType {
    AUDIO,
    VIDEO,
    GROUP_AUDIO,
    GROUP_VIDEO
}

/**
 * Call information
 */
data class CallInfo(
    val callId: String,
    val roomId: String,
    val type: CallType,
    val isOutgoing: Boolean,
    val callerId: String?,
    val callerName: String?,
    val participantIds: List<String> = emptyList(),
    var startTime: Long = 0,
    var duration: Long = 0,
    var isActive: Boolean = false
)

/**
 * High-level call manager that coordinates signalling and WebRTC
 * 
 * This class provides a simplified interface for:
 * - Starting and receiving calls
 * - Managing call state
 * - Coordinating between SignallingClient and WebRtcManager
 */
class CallManager(private val context: Context) {
    
    companion object {
        private const val TAG = "CallManager"
    }
    
    private var signallingClient: SignallingClient? = null
    private var webrtcManager: WebRtcManager? = null
    
    private var signallingUrl: String? = null
    private var accessToken: String? = null
    private var currentUserId: String? = null
    private var currentDisplayName: String? = null
    
    private var currentCall: CallInfo? = null
    private val participants = ConcurrentHashMap<String, RoomParticipant>()
    
    private var initialized = false
    private var muted = false
    private var videoEnabled = false
    
    // Callbacks
    var onIncomingCall: ((CallInfo) -> Unit)? = null
    var onCallConnected: (() -> Unit)? = null
    var onCallDisconnected: (() -> Unit)? = null
    var onCallFailed: ((String) -> Unit)? = null
    var onParticipantJoined: ((RoomParticipant) -> Unit)? = null
    var onParticipantLeft: ((String) -> Unit)? = null
    var onParticipantUpdated: ((RoomParticipant) -> Unit)? = null
    var onLocalVideoFrame: ((VideoFrame) -> Unit)? = null
    var onRemoteVideoFrame: ((String, VideoFrame) -> Unit)? = null
    
    /**
     * Initialize the call manager
     */
    fun initialize(signallingUrl: String, token: String): Boolean {
        if (initialized) {
            return true
        }
        
        Log.d(TAG, "Initializing call manager...")
        
        this.signallingUrl = signallingUrl
        this.accessToken = token
        
        // Initialize WebRTC
        webrtcManager = WebRtcManager(context)
        if (webrtcManager?.initialize() != true) {
            Log.e(TAG, "Failed to initialize WebRTC")
            return false
        }
        
        // Initialize signalling client
        signallingClient = SignallingClient()
        setupSignallingCallbacks()
        
        initialized = true
        Log.d(TAG, "Call manager initialized successfully")
        return true
    }
    
    /**
     * Shutdown the call manager
     */
    fun shutdown() {
        if (!initialized) {
            return
        }
        
        Log.d(TAG, "Shutting down call manager...")
        
        // End any active call
        if (isInCall()) {
            endCall()
        }
        
        // Disconnect signalling
        disconnect()
        
        // Shutdown WebRTC
        webrtcManager?.shutdown()
        webrtcManager = null
        
        signallingClient = null
        
        initialized = false
        Log.d(TAG, "Call manager shutdown complete")
    }
    
    fun isInitialized(): Boolean = initialized
    
    /**
     * Connect to the signalling server
     */
    fun connect() {
        if (!initialized) {
            Log.e(TAG, "Cannot connect: not initialized")
            return
        }
        
        Log.d(TAG, "Connecting to signalling server...")
        signallingClient?.connect(signallingUrl ?: "", accessToken ?: "")
    }
    
    /**
     * Disconnect from the signalling server
     */
    fun disconnect() {
        signallingClient?.disconnect()
    }
    
    fun isConnected(): Boolean = signallingClient?.isConnected() ?: false
    
    /**
     * Start an audio call with a user
     */
    fun startAudioCall(userId: String, displayName: String) {
        Log.d(TAG, "Starting audio call with: $userId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        val callId = "call_${System.currentTimeMillis()}"
        val roomId = "room_$userId"
        
        currentCall = CallInfo(
            callId = callId,
            roomId = roomId,
            type = CallType.AUDIO,
            isOutgoing = true,
            callerId = currentUserId,
            callerName = currentDisplayName,
            isActive = true,
            startTime = 0
        )
        
        // Join the room
        signallingClient?.joinRoom(roomId, currentDisplayName ?: "")
        
        // Start local audio
        webrtcManager?.startLocalAudio()
        muted = false
        videoEnabled = false
    }
    
    /**
     * Start a video call with a user
     */
    fun startVideoCall(userId: String, displayName: String) {
        Log.d(TAG, "Starting video call with: $userId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        val callId = "call_${System.currentTimeMillis()}"
        val roomId = "room_$userId"
        
        currentCall = CallInfo(
            callId = callId,
            roomId = roomId,
            type = CallType.VIDEO,
            isOutgoing = true,
            callerId = currentUserId,
            callerName = currentDisplayName,
            isActive = true,
            startTime = 0
        )
        
        // Join the room
        signallingClient?.joinRoom(roomId, currentDisplayName ?: "")
        
        // Start local audio and video
        webrtcManager?.startLocalAudio()
        webrtcManager?.startLocalVideo()
        muted = false
        videoEnabled = true
    }
    
    /**
     * Accept an incoming call
     */
    fun acceptCall(callId: String) {
        Log.d(TAG, "Accepting call: $callId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        currentCall?.let { call ->
            // Join the room
            signallingClient?.joinRoom(call.roomId, currentDisplayName ?: "")
            
            // Start media based on call type
            webrtcManager?.startLocalAudio()
            if (call.type == CallType.VIDEO || call.type == CallType.GROUP_VIDEO) {
                webrtcManager?.startLocalVideo()
                videoEnabled = true
            } else {
                videoEnabled = false
            }
            
            muted = false
            currentCall = call.copy(
                isActive = true,
                startTime = System.currentTimeMillis()
            )
            
            onCallConnected?.invoke()
        }
    }
    
    /**
     * Decline an incoming call
     */
    fun declineCall(callId: String) {
        Log.d(TAG, "Declining call: $callId")
        currentCall = null
    }
    
    /**
     * End the current call
     */
    fun endCall() {
        Log.d(TAG, "Ending call...")
        
        if (currentCall?.isActive != true) {
            return
        }
        
        // Leave the room
        signallingClient?.leaveRoom()
        
        // Stop all media
        webrtcManager?.stopLocalAudio()
        webrtcManager?.stopLocalVideo()
        webrtcManager?.stopScreenShare()
        
        // Close all peer connections
        webrtcManager?.closeAllPeerConnections()
        
        // Clear state
        currentCall = null
        participants.clear()
        muted = false
        videoEnabled = false
        
        onCallDisconnected?.invoke()
    }
    
    /**
     * Start a group audio call
     */
    fun startGroupAudioCall(groupId: String) {
        Log.d(TAG, "Starting group audio call in: $groupId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        val callId = "groupcall_${System.currentTimeMillis()}"
        val roomId = "group_$groupId"
        
        currentCall = CallInfo(
            callId = callId,
            roomId = roomId,
            type = CallType.GROUP_AUDIO,
            isOutgoing = true,
            callerId = currentUserId,
            callerName = currentDisplayName,
            isActive = true
        )
        
        signallingClient?.joinRoom(roomId, currentDisplayName ?: "")
        webrtcManager?.startLocalAudio()
        muted = false
        videoEnabled = false
    }
    
    /**
     * Start a group video call
     */
    fun startGroupVideoCall(groupId: String) {
        Log.d(TAG, "Starting group video call in: $groupId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        val callId = "groupcall_${System.currentTimeMillis()}"
        val roomId = "group_$groupId"
        
        currentCall = CallInfo(
            callId = callId,
            roomId = roomId,
            type = CallType.GROUP_VIDEO,
            isOutgoing = true,
            callerId = currentUserId,
            callerName = currentDisplayName,
            isActive = true
        )
        
        signallingClient?.joinRoom(roomId, currentDisplayName ?: "")
        webrtcManager?.startLocalAudio()
        webrtcManager?.startLocalVideo()
        muted = false
        videoEnabled = true
    }
    
    /**
     * Join an existing group call
     */
    fun joinGroupCall(roomId: String) {
        Log.d(TAG, "Joining group call: $roomId")
        
        if (!initialized || !isConnected()) {
            onCallFailed?.invoke("Not connected to server")
            return
        }
        
        currentCall = CallInfo(
            callId = "joined_${System.currentTimeMillis()}",
            roomId = roomId,
            type = CallType.GROUP_VIDEO,
            isOutgoing = false,
            callerId = null,
            callerName = null,
            isActive = true
        )
        
        signallingClient?.joinRoom(roomId, currentDisplayName ?: "")
        webrtcManager?.startLocalAudio()
        webrtcManager?.startLocalVideo()
        muted = false
        videoEnabled = true
    }
    
    /**
     * Leave a group call
     */
    fun leaveGroupCall() {
        endCall()
    }
    
    /**
     * Mute local audio
     */
    fun mute() {
        if (!muted) {
            webrtcManager?.stopLocalAudio()
            signallingClient?.toggleMute(true)
            muted = true
            Log.d(TAG, "Muted")
        }
    }
    
    /**
     * Unmute local audio
     */
    fun unmute() {
        if (muted) {
            webrtcManager?.startLocalAudio()
            signallingClient?.toggleMute(false)
            muted = false
            Log.d(TAG, "Unmuted")
        }
    }
    
    /**
     * Toggle mute state
     */
    fun toggleMute() {
        if (muted) {
            unmute()
        } else {
            mute()
        }
    }
    
    fun isMuted(): Boolean = muted
    
    /**
     * Enable local video
     */
    fun enableVideo() {
        if (!videoEnabled) {
            webrtcManager?.startLocalVideo()
            signallingClient?.toggleVideo(true)
            videoEnabled = true
            Log.d(TAG, "Video enabled")
        }
    }
    
    /**
     * Disable local video
     */
    fun disableVideo() {
        if (videoEnabled) {
            webrtcManager?.stopLocalVideo()
            signallingClient?.toggleVideo(false)
            videoEnabled = false
            Log.d(TAG, "Video disabled")
        }
    }
    
    /**
     * Toggle video state
     */
    fun toggleVideo() {
        if (videoEnabled) {
            disableVideo()
        } else {
            enableVideo()
        }
    }
    
    fun isVideoEnabled(): Boolean = videoEnabled
    
    /**
     * Start screen sharing
     */
    fun startScreenShare() {
        if (webrtcManager?.isScreenSharing() != true) {
            // Note: Requires MediaProjection permission
            // webrtcManager?.startScreenShare(mediaProjection, resultCode, data)
            signallingClient?.startScreenShare()
            Log.d(TAG, "Screen sharing started")
        }
    }
    
    /**
     * Stop screen sharing
     */
    fun stopScreenShare() {
        if (webrtcManager?.isScreenSharing() == true) {
            webrtcManager?.stopScreenShare()
            signallingClient?.stopScreenShare()
            Log.d(TAG, "Screen sharing stopped")
        }
    }
    
    fun isScreenSharing(): Boolean = webrtcManager?.isScreenSharing() ?: false
    
    /**
     * Get available audio input devices
     */
    fun getAudioInputDevices(): List<MediaDevice> {
        return webrtcManager?.getAudioInputDevices() ?: emptyList()
    }
    
    /**
     * Get available video input devices
     */
    fun getVideoInputDevices(): List<MediaDevice> {
        return webrtcManager?.getVideoInputDevices() ?: emptyList()
    }
    
    /**
     * Get available audio output devices
     */
    fun getAudioOutputDevices(): List<MediaDevice> {
        return webrtcManager?.getAudioOutputDevices() ?: emptyList()
    }
    
    /**
     * Select audio input device
     */
    fun selectAudioInput(deviceId: String) {
        webrtcManager?.setAudioInputDevice(deviceId)
    }
    
    /**
     * Select video input device
     */
    fun selectVideoInput(deviceId: String) {
        webrtcManager?.setVideoInputDevice(deviceId)
    }
    
    /**
     * Select audio output device
     */
    fun selectAudioOutput(deviceId: String) {
        webrtcManager?.setAudioOutputDevice(deviceId)
    }
    
    /**
     * Get current call information
     */
    fun getCurrentCall(): CallInfo? = currentCall
    
    /**
     * Check if currently in a call
     */
    fun isInCall(): Boolean = currentCall?.isActive ?: false
    
    /**
     * Get list of call participants
     */
    fun getParticipants(): List<RoomParticipant> {
        return participants.values.toList()
    }
    
    /**
     * Configure ICE servers
     */
    fun configureIce(stunServer: String, turnServers: List<Triple<String, String, String>>) {
        webrtcManager?.clearIceServers()
        webrtcManager?.setStunServer(stunServer)
        for ((url, username, password) in turnServers) {
            webrtcManager?.addTurnServer(url, username, password)
        }
    }
    
    /**
     * Set the current user info
     */
    fun setUserInfo(userId: String, displayName: String) {
        currentUserId = userId
        currentDisplayName = displayName
    }
    
    // Private methods
    
    private fun setupSignallingCallbacks() {
        signallingClient?.onConnected = {
            Log.d(TAG, "Connected to signalling server")
        }
        
        signallingClient?.onDisconnected = {
            Log.d(TAG, "Disconnected from signalling server")
            if (isInCall()) {
                endCall()
            }
        }
        
        signallingClient?.onError = { error ->
            Log.e(TAG, "Signalling error: $error")
            onCallFailed?.invoke(error)
        }
        
        signallingClient?.onOffer = { userId, sdp ->
            handleIncomingOffer(userId, sdp)
        }
        
        signallingClient?.onAnswer = { userId, sdp ->
            handleIncomingAnswer(userId, sdp)
        }
        
        signallingClient?.onIceCandidate = { userId, candidate ->
            handleIceCandidate(userId, candidate)
        }
        
        signallingClient?.onUserJoined = { participant ->
            handleUserJoined(participant)
        }
        
        signallingClient?.onUserLeft = { userId ->
            handleUserLeft(userId)
        }
        
        signallingClient?.onRoomState = { state ->
            handleRoomState(state)
        }
    }
    
    private fun handleIncomingOffer(userId: String, sdp: String) {
        Log.d(TAG, "Received offer from: $userId")
        
        // Create peer connection if needed
        if (webrtcManager?.createPeerConnection(userId) != true) {
            Log.e(TAG, "Failed to create peer connection for: $userId")
            return
        }
        
        // Set remote description
        webrtcManager?.setRemoteDescription(userId, sdp, true)
        
        // Create and send answer
        webrtcManager?.createAnswer(userId)
        
        // Update call state
        currentCall?.let {
            if (it.startTime == 0L) {
                currentCall = it.copy(startTime = System.currentTimeMillis())
            }
        }
        
        onCallConnected?.invoke()
    }
    
    private fun handleIncomingAnswer(userId: String, sdp: String) {
        Log.d(TAG, "Received answer from: $userId")
        
        // Set remote description
        webrtcManager?.setRemoteDescription(userId, sdp, false)
        
        // Update call state
        currentCall?.let {
            if (it.startTime == 0L) {
                currentCall = it.copy(startTime = System.currentTimeMillis())
            }
        }
        
        onCallConnected?.invoke()
    }
    
    private fun handleIceCandidate(userId: String, candidate: IceCandidateData) {
        Log.d(TAG, "Received ICE candidate from: $userId")
        webrtcManager?.addIceCandidate(userId, candidate)
    }
    
    private fun handleUserJoined(participant: RoomParticipant) {
        Log.d(TAG, "User joined: ${participant.displayName}")
        
        participants[participant.userId] = participant
        
        // Create peer connection for new participant
        webrtcManager?.createPeerConnection(participant.userId)
        
        // Create and send offer
        webrtcManager?.createOffer(participant.userId)
        
        onParticipantJoined?.invoke(participant)
    }
    
    private fun handleUserLeft(userId: String) {
        Log.d(TAG, "User left: $userId")
        
        participants.remove(userId)
        webrtcManager?.closePeerConnection(userId)
        
        onParticipantLeft?.invoke(userId)
    }
    
    private fun handleRoomState(state: RoomState) {
        Log.d(TAG, "Room state updated, participants: ${state.participants.size}")
        
        // Update participants list
        for (p in state.participants) {
            val existing = participants[p.userId]
            if (existing == null) {
                // New participant
                participants[p.userId] = p
                onParticipantJoined?.invoke(p)
            } else {
                // Updated participant
                participants[p.userId] = p
                onParticipantUpdated?.invoke(p)
            }
        }
    }
}
