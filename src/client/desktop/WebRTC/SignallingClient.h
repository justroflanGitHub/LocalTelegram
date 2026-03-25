#pragma once

#include <string>
#include <functional>
#include <memory>
#include <nlohmann/json.hpp>
#include <ixwebsocket/IXWebSocket.h>

namespace LocalTelegram {

/**
 * @brief Signalling message types for WebRTC negotiation
 */
enum class SignallingMessageType {
    JoinRoom,
    LeaveRoom,
    Offer,
    Answer,
    IceCandidate,
    UserJoined,
    UserLeft,
    RoomState,
    Error,
    Unknown
};

/**
 * @brief Represents a participant in a conference room
 */
struct RoomParticipant {
    std::string userId;
    std::string displayName;
    bool hasAudio;
    bool hasVideo;
    bool isMuted;
    bool isVideoEnabled;
    bool isScreenSharing;
    bool isModerator;
};

/**
 * @brief Represents the current state of a conference room
 */
struct RoomState {
    std::string roomId;
    std::vector<RoomParticipant> participants;
    bool recordingEnabled;
    std::string recordingStatus;
};

/**
 * @brief ICE candidate information
 */
struct IceCandidate {
    std::string candidate;
    std::string sdpMid;
    int sdpMlineIndex;
};

/**
 * @brief Signalling message structure
 */
struct SignallingMessage {
    SignallingMessageType type;
    std::string roomId;
    std::string userId;
    std::string targetUserId;
    nlohmann::json data;
    std::string error;
    
    static SignallingMessage fromJson(const nlohmann::json& json);
    nlohmann::json toJson() const;
};

/**
 * @brief Callback types for signalling events
 */
using OnConnectedCallback = std::function<void()>;
using OnDisconnectedCallback = std::function<void()>;
using OnErrorCallback = std::function<void(const std::string& error)>;
using OnUserJoinedCallback = std::function<void(const RoomParticipant& participant)>;
using OnUserLeftCallback = std::function<void(const std::string& userId)>;
using OnRoomStateCallback = std::function<void(const RoomState& state)>;
using OnOfferCallback = std::function<void(const std::string& userId, const std::string& sdp)>;
using OnAnswerCallback = std::function<void(const std::string& userId, const std::string& sdp)>;
using OnIceCandidateCallback = std::function<void(const std::string& userId, const IceCandidate& candidate)>;

/**
 * @brief WebSocket-based signalling client for WebRTC negotiation
 * 
 * Connects to the ConferenceService SignallingHub for:
 * - Room join/leave operations
 * - SDP offer/answer exchange
 * - ICE candidate exchange
 * - Room state updates
 */
class SignallingClient {
public:
    SignallingClient();
    ~SignallingClient();
    
    // Connection management
    void connect(const std::string& serverUrl, const std::string& accessToken);
    void disconnect();
    bool isConnected() const;
    
    // Room operations
    void joinRoom(const std::string& roomId, const std::string& displayName);
    void leaveRoom();
    
    // WebRTC negotiation
    void sendOffer(const std::string& sdp);
    void sendAnswer(const std::string& targetUserId, const std::string& sdp);
    void sendIceCandidate(const std::string& targetUserId, const IceCandidate& candidate);
    
    // Media state updates
    void updateMediaState(bool hasAudio, bool hasVideo);
    void toggleMute(bool muted);
    void toggleVideo(bool enabled);
    void startScreenShare();
    void stopScreenShare();
    
    // Moderation (for moderators)
    void muteParticipant(const std::string& userId);
    void kickParticipant(const std::string& userId);
    
    // Callbacks
    void setOnConnected(OnConnectedCallback callback);
    void setOnDisconnected(OnDisconnectedCallback callback);
    void setOnError(OnErrorCallback callback);
    void setOnUserJoined(OnUserJoinedCallback callback);
    void setOnUserLeft(OnUserLeftCallback callback);
    void setOnRoomState(OnRoomStateCallback callback);
    void setOnOffer(OnOfferCallback callback);
    void setOnAnswer(OnAnswerCallback callback);
    void setOnIceCandidate(OnIceCandidateCallback callback);
    
private:
    void handleMessage(const std::string& message);
    void sendMessage(const SignallingMessage& message);
    void processRoomState(const nlohmann::json& data);
    void processUserJoined(const nlohmann::json& data);
    void processUserLeft(const nlohmann::json& data);
    void processOffer(const nlohmann::json& data);
    void processAnswer(const nlohmann::json& data);
    void processIceCandidate(const nlohmann::json& data);
    
    std::unique_ptr<ix::WebSocket> websocket_;
    std::string currentRoomId_;
    std::string currentUserId_;
    std::string accessToken_;
    bool connected_ = false;
    
    // Callbacks
    OnConnectedCallback onConnected_;
    OnDisconnectedCallback onDisconnected_;
    OnErrorCallback onError_;
    OnUserJoinedCallback onUserJoined_;
    OnUserLeftCallback onUserLeft_;
    OnRoomStateCallback onRoomState_;
    OnOfferCallback onOffer_;
    OnAnswerCallback onAnswer_;
    OnIceCandidateCallback onIceCandidate_;
};

} // namespace LocalTelegram
