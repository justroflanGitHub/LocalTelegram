package org.localtelegram.webrtc

import android.content.Context
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraManager
import android.media.AudioManager
import android.os.Build
import android.util.Log
import android.view.Surface
import org.webrtc.*
import java.util.*

/**
 * Call state enumeration
 */
enum class CallState {
    IDLE,
    CONNECTING,
    RINGING,
    CONNECTED,
    RECONNECTING,
    ENDED
}

/**
 * Media device information
 */
data class MediaDevice(
    val id: String,
    val name: String,
    val kind: String,
    val isDefault: Boolean = false
)

/**
 * Call statistics
 */
data class CallStats(
    var bytesReceived: Long = 0,
    var bytesSent: Long = 0,
    var packetsReceived: Long = 0,
    var packetsSent: Long = 0,
    var packetsLost: Long = 0,
    var jitter: Long = 0,
    var roundTripTime: Long = 0,
    var availableOutgoingBitrate: Long = 0,
    var frameWidth: Int = 0,
    var frameHeight: Int = 0,
    var framesPerSecond: Int = 0
)

/**
 * WebRTC manager for handling peer connections on Android
 * 
 * Manages:
 * - PeerConnectionFactory creation
 * - PeerConnection creation and management
 * - Media capture (audio/video)
 * - SDP/ICE handling
 * - Screen sharing via MediaProjection
 */
class WebRtcManager(private val context: Context) {
    
    companion object {
        private const val TAG = "WebRtcManager"
        private const val VIDEO_RESOLUTION_WIDTH = 1280
        private const val VIDEO_RESOLUTION_HEIGHT = 720
        private const val VIDEO_FPS = 30
    }
    
    // WebRTC components
    private var peerConnectionFactory: PeerConnectionFactory? = null
    private var peerConnections = mutableMapOf<String, PeerConnection>()
    private var localVideoTrack: VideoTrack? = null
    private var localAudioTrack: AudioTrack? = null
    private var videoCapturer: VideoCapturer? = null
    private var surfaceTextureHelper: SurfaceTextureHelper? = null
    private var videoSource: VideoSource? = null
    private var audioSource: AudioSource? = null
    
    // ICE configuration
    private var stunServer: String? = null
    private var turnServers = mutableListOf<IceServer>()
    
    // State
    private var initialized = false
    private var callState = CallState.IDLE
    private var localAudioEnabled = false
    private var localVideoEnabled = false
    private var screenSharing = false
    
    // Device management
    private var selectedAudioInput: String? = null
    private var selectedVideoInput: String? = null
    private var selectedAudioOutput: String? = null
    
    // Callbacks
    var onCallStateChanged: ((CallState) -> Unit)? = null
    var onStatsUpdated: ((String, CallStats) -> Unit)? = null
    var onLocalVideoFrame: ((VideoFrame) -> Unit)? = null
    var onRemoteVideoFrame: ((String, VideoFrame) -> Unit)? = null
    
    // Peer connection observer for each connection
    private val peerConnectionObservers = mutableMapOf<String, PeerConnection.Observer>()
    
    // SDP observers
    private val sdpObservers = mutableMapOf<String, SdpObserver>()
    
    /**
     * Initialize the WebRTC manager
     */
    fun initialize(): Boolean {
        if (initialized) {
            return true
        }
        
        Log.d(TAG, "Initializing WebRTC manager...")
        
        try {
            // Initialize WebRTC
            val options = PeerConnectionFactory.InitializationOptions.builder(context)
                .setEnableInternalTracer(true)
                .createInitializationOptions()
            PeerConnectionFactory.initialize(options)
            
            // Create peer connection factory
            val encoderFactory = DefaultVideoEncoderFactory(
                EglBase.create().eglBaseContext,
                true,
                true
            )
            val decoderFactory = DefaultVideoDecoderFactory(EglBase.create().eglBaseContext)
            
            peerConnectionFactory = PeerConnectionFactory.builder()
                .setVideoEncoderFactory(encoderFactory)
                .setVideoDecoderFactory(decoderFactory)
                .createPeerConnectionFactory()
            
            if (peerConnectionFactory == null) {
                Log.e(TAG, "Failed to create peer connection factory")
                return false
            }
            
            initialized = true
            Log.d(TAG, "WebRTC manager initialized successfully")
            return true
            
        } catch (e: Exception) {
            Log.e(TAG, "Failed to initialize WebRTC: ${e.message}")
            return false
        }
    }
    
    /**
     * Shutdown the WebRTC manager
     */
    fun shutdown() {
        if (!initialized) {
            return
        }
        
        Log.d(TAG, "Shutting down WebRTC manager...")
        
        // Stop all media
        stopLocalAudio()
        stopLocalVideo()
        stopScreenShare()
        
        // Close all peer connections
        closeAllPeerConnections()
        
        // Release resources
        videoSource?.dispose()
        videoSource = null
        audioSource?.dispose()
        audioSource = null
        videoCapturer?.dispose()
        videoCapturer = null
        surfaceTextureHelper?.dispose()
        surfaceTextureHelper = null
        
        peerConnectionFactory?.dispose()
        peerConnectionFactory = null
        
        initialized = false
        Log.d(TAG, "WebRTC manager shut down")
    }
    
    fun isInitialized(): Boolean = initialized
    
    /**
     * Get available audio input devices
     */
    fun getAudioInputDevices(): List<MediaDevice> {
        val devices = mutableListOf<MediaDevice>()
        devices.add(MediaDevice("default", "Default Microphone", "audioinput", true))
        // Android typically has one audio input
        return devices
    }
    
    /**
     * Get available video input devices
     */
    fun getVideoInputDevices(): List<MediaDevice> {
        val devices = mutableListOf<MediaDevice>()
        
        try {
            val cameraManager = context.getSystemService(Context.CAMERA_SERVICE) as CameraManager
            val cameraIds = cameraManager.cameraIdList
            
            for ((index, id) in cameraIds.withIndex()) {
                val characteristics = cameraManager.getCameraCharacteristics(id)
                val facing = characteristics.get(CameraCharacteristics.LENS_FACING)
                val name = when (facing) {
                    CameraCharacteristics.LENS_FACING_FRONT -> "Front Camera"
                    CameraCharacteristics.LENS_FACING_BACK -> "Back Camera"
                    else -> "Camera $id"
                }
                devices.add(MediaDevice(id, name, "videoinput", index == 0))
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error enumerating cameras: ${e.message}")
        }
        
        return devices
    }
    
    /**
     * Get available audio output devices
     */
    fun getAudioOutputDevices(): List<MediaDevice> {
        val devices = mutableListOf<MediaDevice>()
        devices.add(MediaDevice("default", "Default Speaker", "audiooutput", true))
        
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            val audioManager = context.getSystemService(Context.AUDIO_SERVICE) as AudioManager
            val devicesArray = audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS)
            for (device in devicesArray) {
                devices.add(MediaDevice(
                    device.id.toString(),
                    device.productName?.toString() ?: "Audio Output",
                    "audiooutput",
                    false
                ))
            }
        }
        
        return devices
    }
    
    /**
     * Set audio input device
     */
    fun setAudioInputDevice(deviceId: String): Boolean {
        selectedAudioInput = deviceId
        Log.d(TAG, "Audio input device set to: $deviceId")
        return true
    }
    
    /**
     * Set video input device
     */
    fun setVideoInputDevice(deviceId: String): Boolean {
        selectedVideoInput = deviceId
        Log.d(TAG, "Video input device set to: $deviceId")
        
        // Restart video capturer with new device
        if (localVideoEnabled) {
            stopLocalVideo()
            startLocalVideo()
        }
        return true
    }
    
    /**
     * Set audio output device
     */
    fun setAudioOutputDevice(deviceId: String): Boolean {
        selectedAudioOutput = deviceId
        Log.d(TAG, "Audio output device set to: $deviceId")
        return true
    }
    
    /**
     * Start local audio capture
     */
    fun startLocalAudio(): Boolean {
        if (!initialized || peerConnectionFactory == null) {
            Log.e(TAG, "Cannot start audio: not initialized")
            return false
        }
        
        if (localAudioEnabled) {
            return true
        }
        
        Log.d(TAG, "Starting local audio capture...")
        
        try {
            val audioConstraints = MediaConstraints()
            audioConstraints.mandatory.add(MediaConstraints.KeyValuePair("googEchoCancellation", "true"))
            audioConstraints.mandatory.add(MediaConstraints.KeyValuePair("googNoiseSuppression", "true"))
            audioConstraints.mandatory.add(MediaConstraints.KeyValuePair("googAutoGainControl", "true"))
            
            audioSource = peerConnectionFactory?.createAudioSource(audioConstraints)
            localAudioTrack = peerConnectionFactory?.createAudioTrack("audio_label", audioSource)
            localAudioTrack?.setEnabled(true)
            
            localAudioEnabled = true
            Log.d(TAG, "Local audio capture started")
            return true
            
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start audio: ${e.message}")
            return false
        }
    }
    
    /**
     * Stop local audio capture
     */
    fun stopLocalAudio(): Boolean {
        if (!localAudioEnabled) {
            return true
        }
        
        Log.d(TAG, "Stopping local audio capture...")
        
        localAudioTrack?.setEnabled(false)
        localAudioTrack?.dispose()
        localAudioTrack = null
        audioSource?.dispose()
        audioSource = null
        
        localAudioEnabled = false
        Log.d(TAG, "Local audio capture stopped")
        return true
    }
    
    /**
     * Start local video capture
     */
    fun startLocalVideo(): Boolean {
        if (!initialized || peerConnectionFactory == null) {
            Log.e(TAG, "Cannot start video: not initialized")
            return false
        }
        
        if (localVideoEnabled) {
            return true
        }
        
        Log.d(TAG, "Starting local video capture...")
        
        try {
            // Create video capturer
            videoCapturer = createVideoCapturer()
            if (videoCapturer == null) {
                Log.e(TAG, "Failed to create video capturer")
                return false
            }
            
            // Create surface texture helper
            val eglBase = EglBase.create()
            surfaceTextureHelper = SurfaceTextureHelper.create(
                "VideoCapturerThread",
                eglBase.eglBaseContext
            )
            
            // Create video source
            videoSource = peerConnectionFactory?.createVideoSource(videoCapturer!!.isScreencast)
            videoCapturer?.initialize(surfaceTextureHelper, context, videoSource?.capturerObserver)
            videoCapturer?.startCapture(VIDEO_RESOLUTION_WIDTH, VIDEO_RESOLUTION_HEIGHT, VIDEO_FPS)
            
            // Create video track
            localVideoTrack = peerConnectionFactory?.createVideoTrack("video_label", videoSource)
            localVideoTrack?.setEnabled(true)
            
            localVideoEnabled = true
            Log.d(TAG, "Local video capture started")
            return true
            
        } catch (e: Exception) {
            Log.e(TAG, "Failed to start video: ${e.message}")
            return false
        }
    }
    
    /**
     * Stop local video capture
     */
    fun stopLocalVideo(): Boolean {
        if (!localVideoEnabled) {
            return true
        }
        
        Log.d(TAG, "Stopping local video capture...")
        
        videoCapturer?.stopCapture()
        videoCapturer?.dispose()
        videoCapturer = null
        
        localVideoTrack?.setEnabled(false)
        localVideoTrack?.dispose()
        localVideoTrack = null
        
        videoSource?.dispose()
        videoSource = null
        
        localVideoEnabled = false
        Log.d(TAG, "Local video capture stopped")
        return true
    }
    
    fun isLocalAudioEnabled(): Boolean = localAudioEnabled
    fun isLocalVideoEnabled(): Boolean = localVideoEnabled
    
    /**
     * Set callback for local video frames
     */
    fun setLocalVideoCallback(callback: (VideoFrame) -> Unit) {
        onLocalVideoFrame = callback
    }
    
    /**
     * Start screen sharing using MediaProjection
     */
    fun startScreenShare(mediaProjection: Any?, resultCode: Int, data: Any?): Boolean {
        if (!initialized) {
            return false
        }
        
        if (screenSharing) {
            return true
        }
        
        Log.d(TAG, "Starting screen share...")
        
        // Note: Full implementation requires MediaProjection API
        // This is a placeholder for the actual implementation
        // videoCapturer = createScreenCapturer(mediaProjection, resultCode, data)
        
        screenSharing = true
        return true
    }
    
    /**
     * Stop screen sharing
     */
    fun stopScreenShare() {
        if (!screenSharing) {
            return
        }
        
        Log.d(TAG, "Stopping screen share...")
        
        // Stop screen capturer and restart camera if video was enabled
        if (localVideoEnabled) {
            stopLocalVideo()
            startLocalVideo()
        }
        
        screenSharing = false
    }
    
    fun isScreenSharing(): Boolean = screenSharing
    
    /**
     * Create a peer connection for a remote peer
     */
    fun createPeerConnection(peerId: String): Boolean {
        if (!initialized || peerConnectionFactory == null) {
            Log.e(TAG, "Cannot create peer connection: not initialized")
            return false
        }
        
        if (peerConnections.containsKey(peerId)) {
            Log.w(TAG, "Peer connection already exists: $peerId")
            return false
        }
        
        Log.d(TAG, "Creating peer connection for: $peerId")
        
        try {
            // Build ICE servers list
            val iceServers = mutableListOf<IceServer>()
            
            stunServer?.let {
                iceServers.add(IceServer.builder(it).createIceServer())
            }
            
            iceServers.addAll(turnServers)
            
            // Create RTC configuration
            val rtcConfig = PeerConnection.RTCConfiguration(iceServers)
            rtcConfig.tcpCandidatePolicy = PeerConnection.TcpCandidatePolicy.DISABLED
            rtcConfig.bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE
            rtcConfig.rtcpMuxPolicy = PeerConnection.RtcpMuxPolicy.REQUIRE
            rtcConfig.continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_CONTINUALLY
            rtcConfig.sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
            
            // Create peer connection observer
            val observer = createPeerConnectionObserver(peerId)
            peerConnectionObservers[peerId] = observer
            
            // Create peer connection
            val peerConnection = peerConnectionFactory?.createPeerConnection(rtcConfig, observer)
            if (peerConnection == null) {
                Log.e(TAG, "Failed to create peer connection")
                return false
            }
            
            // Add local tracks
            localAudioTrack?.let { peerConnection.addTrack(it, listOf("stream_id")) }
            localVideoTrack?.let { peerConnection.addTrack(it, listOf("stream_id")) }
            
            peerConnections[peerId] = peerConnection
            updateCallState(CallState.CONNECTING)
            
            return true
            
        } catch (e: Exception) {
            Log.e(TAG, "Failed to create peer connection: ${e.message}")
            return false
        }
    }
    
    /**
     * Close a peer connection
     */
    fun closePeerConnection(peerId: String) {
        peerConnections[peerId]?.let { pc ->
            Log.d(TAG, "Closing peer connection: $peerId")
            pc.close()
            peerConnections.remove(peerId)
        }
        peerConnectionObservers.remove(peerId)
        sdpObservers.remove(peerId)
        
        if (peerConnections.isEmpty()) {
            updateCallState(CallState.IDLE)
        }
    }
    
    /**
     * Close all peer connections
     */
    fun closeAllPeerConnections() {
        Log.d(TAG, "Closing all peer connections...")
        
        for ((peerId, pc) in peerConnections) {
            pc.close()
        }
        peerConnections.clear()
        peerConnectionObservers.clear()
        sdpObservers.clear()
        
        updateCallState(CallState.IDLE)
    }
    
    /**
     * Create SDP offer
     */
    fun createOffer(peerId: String): Boolean {
        val pc = peerConnections[peerId] ?: run {
            Log.e(TAG, "Peer connection not found: $peerId")
            return false
        }
        
        Log.d(TAG, "Creating offer for: $peerId")
        
        val constraints = MediaConstraints()
        constraints.mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"))
        constraints.mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"))
        
        val observer = createSdpObserver(peerId, true)
        sdpObservers[peerId] = observer
        
        pc.createOffer(observer, constraints)
        return true
    }
    
    /**
     * Create SDP answer
     */
    fun createAnswer(peerId: String): Boolean {
        val pc = peerConnections[peerId] ?: run {
            Log.e(TAG, "Peer connection not found: $peerId")
            return false
        }
        
        Log.d(TAG, "Creating answer for: $peerId")
        
        val constraints = MediaConstraints()
        constraints.mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"))
        constraints.mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"))
        
        val observer = createSdpObserver(peerId, false)
        sdpObservers[peerId] = observer
        
        pc.createAnswer(observer, constraints)
        return true
    }
    
    /**
     * Set remote SDP description
     */
    fun setRemoteDescription(peerId: String, sdp: String, isOffer: Boolean): Boolean {
        val pc = peerConnections[peerId] ?: run {
            Log.e(TAG, "Peer connection not found: $peerId")
            return false
        }
        
        Log.d(TAG, "Setting remote description for: $peerId (isOffer: $isOffer)")
        
        val type = if (isOffer) SessionDescription.Type.OFFER else SessionDescription.Type.ANSWER
        val sessionDescription = SessionDescription(type, sdp)
        
        val observer = object : SdpObserver {
            override fun onCreateSuccess(p0: SessionDescription?) {}
            override fun onSetSuccess() {
                Log.d(TAG, "Remote description set successfully for: $peerId")
            }
            override fun onCreateFailure(p0: String?) {}
            override fun onSetFailure(error: String?) {
                Log.e(TAG, "Failed to set remote description: $error")
            }
        }
        
        pc.setRemoteDescription(observer, sessionDescription)
        return true
    }
    
    /**
     * Add ICE candidate
     */
    fun addIceCandidate(peerId: String, candidate: IceCandidateData): Boolean {
        val pc = peerConnections[peerId] ?: run {
            Log.e(TAG, "Peer connection not found: $peerId")
            return false
        }
        
        Log.d(TAG, "Adding ICE candidate for: $peerId")
        
        val iceCandidate = IceCandidate(
            candidate.sdpMid,
            candidate.sdpMlineIndex,
            candidate.candidate
        )
        
        pc.addIceCandidate(iceCandidate)
        return true
    }
    
    /**
     * Set callback for remote video frames
     */
    fun setOnRemoteVideoCallback(peerId: String, callback: (VideoFrame) -> Unit) {
        onRemoteVideoFrame = { id, frame ->
            if (id == peerId) {
                callback(frame)
            }
        }
    }
    
    fun getCallState(): CallState = callState
    
    fun setOnCallStateChanged(callback: (CallState) -> Unit) {
        onCallStateChanged = callback
    }
    
    /**
     * Get call statistics
     */
    fun getCallStats(peerId: String, callback: (CallStats) -> Unit) {
        val pc = peerConnections[peerId] ?: return
        
        pc.getStats { rtcStatsReport ->
            val stats = CallStats()
            
            for ((key, value) in rtcStatsReport.statsMap) {
                when (value.type) {
                    "inbound-rtp" -> {
                        // Parse inbound stats
                    }
                    "outbound-rtp" -> {
                        // Parse outbound stats
                    }
                    "candidate-pair" -> {
                        // Parse connection stats
                    }
                }
            }
            
            callback(stats)
        }
    }
    
    /**
     * Set STUN server
     */
    fun setStunServer(url: String) {
        stunServer = url
        Log.d(TAG, "STUN server set to: $url")
    }
    
    /**
     * Add TURN server
     */
    fun addTurnServer(url: String, username: String, password: String) {
        val turnServer = IceServer.builder(url)
            .setUsername(username)
            .setPassword(password)
            .createIceServer()
        turnServers.add(turnServer)
        Log.d(TAG, "TURN server added: $url")
    }
    
    /**
     * Clear all ICE servers
     */
    fun clearIceServers() {
        stunServer = null
        turnServers.clear()
        Log.d(TAG, "ICE servers cleared")
    }
    
    // Private helper methods
    
    private fun createVideoCapturer(): VideoCapturer? {
        return if (Camera2Enumerator.isSupported(context)) {
            Camera2Enumerator(context).deviceCapturers.firstOrNull()
        } else {
            Camera1Enumerator(false).deviceCapturers.firstOrNull()
        }
    }
    
    private fun createPeerConnectionObserver(peerId: String): PeerConnection.Observer {
        return object : PeerConnection.Observer {
            override fun onSignalingChange(state: PeerConnection.SignalingState?) {
                Log.d(TAG, "[$peerId] Signaling state changed: $state")
            }
            
            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                Log.d(TAG, "[$peerId] ICE connection state changed: $state")
                
                when (state) {
                    PeerConnection.IceConnectionState.CONNECTED -> {
                        updateCallState(CallState.CONNECTED)
                    }
                    PeerConnection.IceConnectionState.DISCONNECTED,
                    PeerConnection.IceConnectionState.CLOSED -> {
                        updateCallState(CallState.ENDED)
                    }
                    PeerConnection.IceConnectionState.FAILED -> {
                        updateCallState(CallState.ENDED)
                        onCallStateChanged?.invoke(CallState.ENDED)
                    }
                    else -> {}
                }
            }
            
            override fun onIceConnectionReceivingChange(receiving: Boolean) {
                Log.d(TAG, "[$peerId] ICE connection receiving changed: $receiving")
            }
            
            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {
                Log.d(TAG, "[$peerId] ICE gathering state changed: $state")
            }
            
            override fun onIceCandidate(candidate: IceCandidate?) {
                Log.d(TAG, "[$peerId] ICE candidate gathered")
                candidate?.let {
                    // Notify callback to send ICE candidate via signalling
                }
            }
            
            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>?) {
                Log.d(TAG, "[$peerId] ICE candidates removed")
            }
            
            override fun onAddStream(stream: MediaStream?) {
                Log.d(TAG, "[$peerId] Stream added")
            }
            
            override fun onRemoveStream(stream: MediaStream?) {
                Log.d(TAG, "[$peerId] Stream removed")
            }
            
            override fun onDataChannel(channel: DataChannel?) {
                Log.d(TAG, "[$peerId] Data channel received")
            }
            
            override fun onRenegotiationNeeded() {
                Log.d(TAG, "[$peerId] Renegotiation needed")
            }
            
            override fun onAddTrack(receiver: RtpReceiver?, streams: Array<out MediaStream>?) {
                Log.d(TAG, "[$peerId] Track added")
            }
        }
    }
    
    private fun createSdpObserver(peerId: String, isOffer: Boolean): SdpObserver {
        return object : SdpObserver {
            override fun onCreateSuccess(sessionDescription: SessionDescription?) {
                Log.d(TAG, "[$peerId] SDP created successfully")
                
                sessionDescription?.let { sdp ->
                    val pc = peerConnections[peerId] ?: return@let
                    
                    // Set local description
                    pc.setLocalDescription(object : SdpObserver {
                        override fun onCreateSuccess(p0: SessionDescription?) {}
                        override fun onSetSuccess() {
                            Log.d(TAG, "[$peerId] Local description set")
                            
                            // Notify callback to send SDP via signalling
                            if (isOffer) {
                                // onLocalOffer?.invoke(peerId, sdp.description)
                            } else {
                                // onLocalAnswer?.invoke(peerId, sdp.description)
                            }
                        }
                        override fun onCreateFailure(p0: String?) {}
                        override fun onSetFailure(error: String?) {
                            Log.e(TAG, "[$peerId] Failed to set local description: $error")
                        }
                    }, sdp)
                }
            }
            
            override fun onSetSuccess() {
                Log.d(TAG, "[$peerId] SDP set successfully")
            }
            
            override fun onCreateFailure(error: String?) {
                Log.e(TAG, "[$peerId] Failed to create SDP: $error")
            }
            
            override fun onSetFailure(error: String?) {
                Log.e(TAG, "[$peerId] Failed to set SDP: $error")
            }
        }
    }
    
    private fun updateCallState(newState: CallState) {
        if (callState != newState) {
            callState = newState
            onCallStateChanged?.invoke(newState)
        }
    }
}
