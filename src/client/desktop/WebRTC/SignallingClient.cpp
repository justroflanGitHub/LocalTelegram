#include "SignallingClient.h"
#include <iostream>
#include <sstream>

namespace LocalTelegram {

// SignallingMessage implementation
SignallingMessage SignallingMessage::fromJson(const nlohmann::json& json) {
    SignallingMessage msg;
    
    std::string typeStr = json.value("type", "");
    if (typeStr == "joinRoom") msg.type = SignallingMessageType::JoinRoom;
    else if (typeStr == "leaveRoom") msg.type = SignallingMessageType::LeaveRoom;
    else if (typeStr == "offer") msg.type = SignallingMessageType::Offer;
    else if (typeStr == "answer") msg.type = SignallingMessageType::Answer;
    else if (typeStr == "iceCandidate") msg.type = SignallingMessageType::IceCandidate;
    else if (typeStr == "userJoined") msg.type = SignallingMessageType::UserJoined;
    else if (typeStr == "userLeft") msg.type = SignallingMessageType::UserLeft;
    else if (typeStr == "roomState") msg.type = SignallingMessageType::RoomState;
    else if (typeStr == "error") msg.type = SignallingMessageType::Error;
    else msg.type = SignallingMessageType::Unknown;
    
    msg.roomId = json.value("roomId", "");
    msg.userId = json.value("userId", "");
    msg.targetUserId = json.value("targetUserId", "");
    msg.data = json.value("data", nlohmann::json::object());
    msg.error = json.value("error", "");
    
    return msg;
}

nlohmann::json SignallingMessage::toJson() const {
    nlohmann::json json;
    
    std::string typeStr;
    switch (type) {
        case SignallingMessageType::JoinRoom: typeStr = "joinRoom"; break;
        case SignallingMessageType::LeaveRoom: typeStr = "leaveRoom"; break;
        case SignallingMessageType::Offer: typeStr = "offer"; break;
        case SignallingMessageType::Answer: typeStr = "answer"; break;
        case SignallingMessageType::IceCandidate: typeStr = "iceCandidate"; break;
        case SignallingMessageType::UserJoined: typeStr = "userJoined"; break;
        case SignallingMessageType::UserLeft: typeStr = "userLeft"; break;
        case SignallingMessageType::RoomState: typeStr = "roomState"; break;
        case SignallingMessageType::Error: typeStr = "error"; break;
        default: typeStr = "unknown";
    }
    
    json["type"] = typeStr;
    if (!roomId.empty()) json["roomId"] = roomId;
    if (!userId.empty()) json["userId"] = userId;
    if (!targetUserId.empty()) json["targetUserId"] = targetUserId;
    if (!data.is_null()) json["data"] = data;
    if (!error.empty()) json["error"] = error;
    
    return json;
}

// SignallingClient implementation
SignallingClient::SignallingClient() 
    : websocket_(std::make_unique<ix::WebSocket>()) {
}

SignallingClient::~SignallingClient() {
    disconnect();
}

void SignallingClient::connect(const std::string& serverUrl, const std::string& accessToken) {
    accessToken_ = accessToken;
    
    // Build WebSocket URL with access token
    std::string wsUrl = serverUrl;
    if (wsUrl.find("://") == std::string::npos) {
        wsUrl = "wss://" + wsUrl;
    }
    
    // Append access token as query parameter
    wsUrl += "?access_token=" + accessToken;
    
    websocket_->setUrl(wsUrl);
    
    // Set up callbacks
    websocket_->setOnMessageCallback([this](const ix::WebSocketMessagePtr& msg) {
        if (msg->type == ix::WebSocketMessageType::Open) {
            connected_ = true;
            std::cout << "[Signalling] Connected to server" << std::endl;
            if (onConnected_) {
                onConnected_();
            }
        } else if (msg->type == ix::WebSocketMessageType::Close) {
            connected_ = false;
            std::cout << "[Signalling] Disconnected from server: " 
                      << msg->closeInfo.reason << std::endl;
            if (onDisconnected_) {
                onDisconnected_();
            }
        } else if (msg->type == ix::WebSocketMessageType::Error) {
            std::cerr << "[Signalling] Error: " << msg->errorInfo.reason << std::endl;
            if (onError_) {
                onError_(msg->errorInfo.reason);
            }
        } else if (msg->type == ix::WebSocketMessageType::Message) {
            handleMessage(msg->str);
        }
    });
    
    // Start connection
    websocket_->start();
}

void SignallingClient::disconnect() {
    if (websocket_) {
        websocket_->stop();
        connected_ = false;
    }
    currentRoomId_.clear();
}

bool SignallingClient::isConnected() const {
    return connected_;
}

void SignallingClient::joinRoom(const std::string& roomId, const std::string& displayName) {
    currentRoomId_ = roomId;
    
    SignallingMessage msg;
    msg.type = SignallingMessageType::JoinRoom;
    msg.roomId = roomId;
    msg.data["displayName"] = displayName;
    
    sendMessage(msg);
}

void SignallingClient::leaveRoom() {
    if (currentRoomId_.empty()) return;
    
    SignallingMessage msg;
    msg.type = SignallingMessageType::LeaveRoom;
    msg.roomId = currentRoomId_;
    
    sendMessage(msg);
    currentRoomId_.clear();
}

void SignallingClient::sendOffer(const std::string& sdp) {
    SignallingMessage msg;
    msg.type = SignallingMessageType::Offer;
    msg.roomId = currentRoomId_;
    msg.data["sdp"] = sdp;
    
    sendMessage(msg);
}

void SignallingClient::sendAnswer(const std::string& targetUserId, const std::string& sdp) {
    SignallingMessage msg;
    msg.type = SignallingMessageType::Answer;
    msg.roomId = currentRoomId_;
    msg.targetUserId = targetUserId;
    msg.data["sdp"] = sdp;
    
    sendMessage(msg);
}

void SignallingClient::sendIceCandidate(const std::string& targetUserId, const IceCandidate& candidate) {
    SignallingMessage msg;
    msg.type = SignallingMessageType::IceCandidate;
    msg.roomId = currentRoomId_;
    msg.targetUserId = targetUserId;
    msg.data["candidate"] = candidate.candidate;
    msg.data["sdpMid"] = candidate.sdpMid;
    msg.data["sdpMlineIndex"] = candidate.sdpMlineIndex;
    
    sendMessage(msg);
}

void SignallingClient::updateMediaState(bool hasAudio, bool hasVideo) {
    SignallingMessage msg;
    msg.type = SignallingMessageType::UserJoined; // Reuse for state update
    msg.roomId = currentRoomId_;
    msg.data["action"] = "updateMediaState";
    msg.data["hasAudio"] = hasAudio;
    msg.data["hasVideo"] = hasVideo;
    
    sendMessage(msg);
}

void SignallingClient::toggleMute(bool muted) {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.data["action"] = "toggleMute";
    msg.data["muted"] = muted;
    
    sendMessage(msg);
}

void SignallingClient::toggleVideo(bool enabled) {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.data["action"] = "toggleVideo";
    msg.data["enabled"] = enabled;
    
    sendMessage(msg);
}

void SignallingClient::startScreenShare() {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.data["action"] = "startScreenShare";
    
    sendMessage(msg);
}

void SignallingClient::stopScreenShare() {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.data["action"] = "stopScreenShare";
    
    sendMessage(msg);
}

void SignallingClient::muteParticipant(const std::string& userId) {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.targetUserId = userId;
    msg.data["action"] = "muteParticipant";
    
    sendMessage(msg);
}

void SignallingClient::kickParticipant(const std::string& userId) {
    SignallingMessage msg;
    msg.roomId = currentRoomId_;
    msg.targetUserId = userId;
    msg.data["action"] = "kickParticipant";
    
    sendMessage(msg);
}

// Callback setters
void SignallingClient::setOnConnected(OnConnectedCallback callback) {
    onConnected_ = std::move(callback);
}

void SignallingClient::setOnDisconnected(OnDisconnectedCallback callback) {
    onDisconnected_ = std::move(callback);
}

void SignallingClient::setOnError(OnErrorCallback callback) {
    onError_ = std::move(callback);
}

void SignallingClient::setOnUserJoined(OnUserJoinedCallback callback) {
    onUserJoined_ = std::move(callback);
}

void SignallingClient::setOnUserLeft(OnUserLeftCallback callback) {
    onUserLeft_ = std::move(callback);
}

void SignallingClient::setOnRoomState(OnRoomStateCallback callback) {
    onRoomState_ = std::move(callback);
}

void SignallingClient::setOnOffer(OnOfferCallback callback) {
    onOffer_ = std::move(callback);
}

void SignallingClient::setOnAnswer(OnAnswerCallback callback) {
    onAnswer_ = std::move(callback);
}

void SignallingClient::setOnIceCandidate(OnIceCandidateCallback callback) {
    onIceCandidate_ = std::move(callback);
}

// Private methods
void SignallingClient::handleMessage(const std::string& message) {
    try {
        nlohmann::json json = nlohmann::json::parse(message);
        SignallingMessage msg = SignallingMessage::fromJson(json);
        
        switch (msg.type) {
            case SignallingMessageType::RoomState:
                processRoomState(msg.data);
                break;
            case SignallingMessageType::UserJoined:
                processUserJoined(msg.data);
                break;
            case SignallingMessageType::UserLeft:
                processUserLeft(msg.data);
                break;
            case SignallingMessageType::Offer:
                processOffer(msg.data);
                break;
            case SignallingMessageType::Answer:
                processAnswer(msg.data);
                break;
            case SignallingMessageType::IceCandidate:
                processIceCandidate(msg.data);
                break;
            case SignallingMessageType::Error:
                if (onError_) {
                    onError_(msg.error);
                }
                break;
            default:
                std::cerr << "[Signalling] Unknown message type received" << std::endl;
        }
    } catch (const std::exception& e) {
        std::cerr << "[Signalling] Error parsing message: " << e.what() << std::endl;
    }
}

void SignallingClient::sendMessage(const SignallingMessage& message) {
    if (!connected_) {
        std::cerr << "[Signalling] Cannot send message: not connected" << std::endl;
        return;
    }
    
    nlohmann::json json = message.toJson();
    std::string jsonStr = json.dump();
    
    websocket_->send(jsonStr);
}

void SignallingClient::processRoomState(const nlohmann::json& data) {
    RoomState state;
    state.roomId = data.value("roomId", "");
    state.recordingEnabled = data.value("recordingEnabled", false);
    state.recordingStatus = data.value("recordingStatus", "");
    
    if (data.contains("participants") && data["participants"].is_array()) {
        for (const auto& p : data["participants"]) {
            RoomParticipant participant;
            participant.userId = p.value("userId", "");
            participant.displayName = p.value("displayName", "");
            participant.hasAudio = p.value("hasAudio", false);
            participant.hasVideo = p.value("hasVideo", false);
            participant.isMuted = p.value("isMuted", false);
            participant.isVideoEnabled = p.value("isVideoEnabled", true);
            participant.isScreenSharing = p.value("isScreenSharing", false);
            participant.isModerator = p.value("isModerator", false);
            state.participants.push_back(participant);
        }
    }
    
    if (onRoomState_) {
        onRoomState_(state);
    }
}

void SignallingClient::processUserJoined(const nlohmann::json& data) {
    RoomParticipant participant;
    participant.userId = data.value("userId", "");
    participant.displayName = data.value("displayName", "");
    participant.hasAudio = data.value("hasAudio", false);
    participant.hasVideo = data.value("hasVideo", false);
    participant.isMuted = data.value("isMuted", false);
    participant.isVideoEnabled = data.value("isVideoEnabled", true);
    participant.isScreenSharing = data.value("isScreenSharing", false);
    participant.isModerator = data.value("isModerator", false);
    
    if (onUserJoined_) {
        onUserJoined_(participant);
    }
}

void SignallingClient::processUserLeft(const nlohmann::json& data) {
    std::string userId = data.value("userId", "");
    
    if (onUserLeft_) {
        onUserLeft_(userId);
    }
}

void SignallingClient::processOffer(const nlohmann::json& data) {
    std::string userId = data.value("userId", "");
    std::string sdp = data.value("sdp", "");
    
    if (onOffer_) {
        onOffer_(userId, sdp);
    }
}

void SignallingClient::processAnswer(const nlohmann::json& data) {
    std::string userId = data.value("userId", "");
    std::string sdp = data.value("sdp", "");
    
    if (onAnswer_) {
        onAnswer_(userId, sdp);
    }
}

void SignallingClient::processIceCandidate(const nlohmann::json& data) {
    std::string userId = data.value("userId", "");
    
    IceCandidate candidate;
    candidate.candidate = data.value("candidate", "");
    candidate.sdpMid = data.value("sdpMid", "");
    candidate.sdpMlineIndex = data.value("sdpMlineIndex", 0);
    
    if (onIceCandidate_) {
        onIceCandidate_(userId, candidate);
    }
}

} // namespace LocalTelegram
