#pragma once

#include <string>
#include <memory>
#include <functional>
#include <vector>
#include <map>

#include "SignallingClient.h"
#include "WebRtcManager.h"

namespace LocalTelegram {

/**
 * @brief Call type enumeration
 */
enum class CallType {
    Audio,
    Video,
    GroupAudio,
    GroupVideo
};

/**
 * @brief Call information
 */
struct CallInfo {
    std::string callId;
    std::string roomId;
    CallType type;
    bool isOutgoing;
    std::string callerId;
    std::string callerName;
    std::vector<std::string> participantIds;
    int64_t startTime = 0;
    int64_t duration = 0;
    bool isActive = false;
};

/**
 * @brief High-level call manager that coordinates signalling and WebRTC
 * 
 * This class provides a simplified interface for:
 * - Starting and receiving calls
 * - Managing call state
 * - Coordinating between SignallingClient and WebRtcManager
 */
class CallManager {
public:
    CallManager();
    ~CallManager();
    
    // Initialization
    bool initialize(const std::string& signallingUrl, const std::string& accessToken);
    void shutdown();
    bool isInitialized() const;
    
    // Connection
    void connect();
    void disconnect();
    bool isConnected() const;
    
    // 1-on-1 calls
    void startAudioCall(const std::string& userId, const std::string& displayName);
    void startVideoCall(const std::string& userId, const std::string& displayName);
    void acceptCall(const std::string& callId);
    void declineCall(const std::string& callId);
    void endCall();
    
    // Group calls
    void startGroupAudioCall(const std::string& groupId);
    void startGroupVideoCall(const std::string& groupId);
    void joinGroupCall(const std::string& roomId);
    void leaveGroupCall();
    
    // Call controls
    void mute();
    void unmute();
    void toggleMute();
    bool isMuted() const;
    
    void enableVideo();
    void disableVideo();
    void toggleVideo();
    bool isVideoEnabled() const;
    
    void startScreenShare();
    void stopScreenShare();
    bool isScreenSharing() const;
    
    // Device selection
    std::vector<MediaDevice> getAudioInputDevices();
    std::vector<MediaDevice> getVideoInputDevices();
    std::vector<MediaDevice> getAudioOutputDevices();
    void selectAudioInput(const std::string& deviceId);
    void selectVideoInput(const std::string& deviceId);
    void selectAudioOutput(const std::string& deviceId);
    
    // Current call info
    CallInfo getCurrentCall() const;
    bool isInCall() const;
    std::vector<RoomParticipant> getParticipants() const;
    
    // ICE configuration
    void configureIce(const std::string& stunServer, 
                      const std::vector<std::tuple<std::string, std::string, std::string>>& turnServers);
    
    // Callbacks
    void setOnIncomingCall(std::function<void(const CallInfo&)> callback);
    void setOnCallConnected(std::function<void()> callback);
    void setOnCallDisconnected(std::function<void()> callback);
    void setOnCallFailed(std::function<void(const std::string& error)> callback);
    void setOnParticipantJoined(std::function<void(const RoomParticipant&)> callback);
    void setOnParticipantLeft(std::function<void(const std::string& userId)> callback);
    void setOnParticipantUpdated(std::function<void(const RoomParticipant&)> callback);
    void setOnLocalVideoFrame(OnVideoFrameCallback callback);
    void setOnRemoteVideoFrame(const std::string& userId, OnVideoFrameCallback callback);
    
private:
    void setupSignallingCallbacks();
    void handleIncomingOffer(const std::string& userId, const std::string& sdp);
    void handleIncomingAnswer(const std::string& userId, const std::string& sdp);
    void handleIceCandidate(const std::string& userId, const IceCandidate& candidate);
    void handleUserJoined(const RoomParticipant& participant);
    void handleUserLeft(const std::string& userId);
    void handleRoomState(const RoomState& state);
    
    std::unique_ptr<SignallingClient> signallingClient_;
    std::unique_ptr<WebRtcManager> webrtcManager_;
    
    std::string signallingUrl_;
    std::string accessToken_;
    std::string currentUserId_;
    std::string currentDisplayName_;
    
    CallInfo currentCall_;
    std::map<std::string, RoomParticipant> participants_;
    
    bool initialized_ = false;
    bool muted_ = false;
    bool videoEnabled_ = false;
    
    // Callbacks
    std::function<void(const CallInfo&)> onIncomingCall_;
    std::function<void()> onCallConnected_;
    std::function<void()> onCallDisconnected_;
    std::function<void(const std::string&)> onCallFailed_;
    std::function<void(const RoomParticipant&)> onParticipantJoined_;
    std::function<void(const std::string&)> onParticipantLeft_;
    std::function<void(const RoomParticipant&)> onParticipantUpdated_;
    OnVideoFrameCallback onLocalVideoFrame_;
};

} // namespace LocalTelegram
