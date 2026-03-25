#pragma once

#include <string>
#include <vector>
#include <memory>
#include <functional>
#include <map>

// Forward declarations for WebRTC types
namespace rtc {
    class Configuration;
    class PeerConnection;
    class DataChannel;
    class Track;
    class Rtcp;
}

namespace webrtc {
    class PeerConnectionFactoryInterface;
    class PeerConnectionInterface;
    class MediaStreamInterface;
    class VideoTrackInterface;
    class AudioTrackInterface;
    class VideoTrackSourceInterface;
    class AudioSourceInterface;
    class DataChannelInterface;
    class SessionDescriptionInterface;
    class IceCandidateInterface;
}

namespace LocalTelegram {

class SignallingClient;
struct IceCandidate;
struct RoomParticipant;

/**
 * @brief Video frame callback type
 */
using OnVideoFrameCallback = std::function<void(const uint8_t* data, int width, int height, int stride)>;

/**
 * @brief Audio data callback type
 */
using OnAudioDataCallback = std::function<void(const int16_t* data, int sampleRate, int channels, int frames)>;

/**
 * @brief Call state enumeration
 */
enum class CallState {
    Idle,
    Connecting,
    Ringing,
    Connected,
    Reconnecting,
    Ended
};

/**
 * @brief Media device information
 */
struct MediaDevice {
    std::string id;
    std::string name;
    std::string kind; // "audioinput", "audiooutput", "videoinput"
    bool isDefault;
};

/**
 * @brief Call statistics
 */
struct CallStats {
    uint32_t bytesReceived = 0;
    uint32_t bytesSent = 0;
    uint32_t packetsReceived = 0;
    uint32_t packetsSent = 0;
    uint32_t packetsLost = 0;
    uint32_t jitter = 0;
    uint32_t roundTripTime = 0;
    uint32_t availableOutgoingBitrate = 0;
    uint32_t frameWidth = 0;
    uint32_t frameHeight = 0;
    uint32_t framesPerSecond = 0;
};

/**
 * @brief WebRTC manager for handling peer connections
 * 
 * Manages:
 * - PeerConnectionFactory creation
 * - PeerConnection creation and management
 * - Media capture (audio/video)
 * - SDP/ICE handling
 * - Screen sharing
 */
class WebRtcManager {
public:
    WebRtcManager();
    ~WebRtcManager();
    
    // Initialization
    bool initialize();
    void shutdown();
    bool isInitialized() const;
    
    // Device enumeration
    std::vector<MediaDevice> getAudioInputDevices();
    std::vector<MediaDevice> getVideoInputDevices();
    std::vector<MediaDevice> getAudioOutputDevices();
    
    // Device selection
    bool setAudioInputDevice(const std::string& deviceId);
    bool setVideoInputDevice(const std::string& deviceId);
    bool setAudioOutputDevice(const std::string& deviceId);
    
    // Local media
    bool startLocalAudio();
    bool stopLocalAudio();
    bool startLocalVideo();
    bool stopLocalVideo();
    bool isLocalAudioEnabled() const;
    bool isLocalVideoEnabled() const;
    
    // Local video preview
    void setLocalVideoCallback(OnVideoFrameCallback callback);
    
    // Screen sharing
    bool startScreenShare(const std::string& windowId = "");
    bool startScreenShareMonitor(int monitorIndex = 0);
    void stopScreenShare();
    bool isScreenSharing() const;
    std::vector<std::string> getAvailableWindows();
    std::vector<std::string> getAvailableMonitors();
    
    // Peer connection management
    bool createPeerConnection(const std::string& peerId);
    void closePeerConnection(const std::string& peerId);
    void closeAllPeerConnections();
    
    // SDP handling
    bool createOffer(const std::string& peerId);
    bool createAnswer(const std::string& peerId);
    bool setRemoteDescription(const std::string& peerId, const std::string& sdp, bool isOffer);
    bool addIceCandidate(const std::string& peerId, const IceCandidate& candidate);
    
    // Remote track callbacks
    void setOnRemoteVideoCallback(const std::string& peerId, OnVideoFrameCallback callback);
    void setOnRemoteAudioCallback(const std::string& peerId, OnAudioDataCallback callback);
    
    // Call state
    CallState getCallState() const;
    void setOnCallStateChanged(std::function<void(CallState)> callback);
    
    // Statistics
    CallStats getCallStats(const std::string& peerId);
    void setOnStatsUpdated(std::function<void(const std::string&, const CallStats&)> callback);
    
    // ICE configuration
    void setStunServer(const std::string& url);
    void addTurnServer(const std::string& url, const std::string& username, const std::string& password);
    void clearIceServers();
    
private:
    class Impl;
    std::unique_ptr<Impl> impl_;
    
    // Prevent copying
    WebRtcManager(const WebRtcManager&) = delete;
    WebRtcManager& operator=(const WebRtcManager&) = delete;
};

} // namespace LocalTelegram
