#include "CallManager.h"
#include <iostream>
#include <chrono>

namespace LocalTelegram {

CallManager::CallManager()
    : signallingClient_(std::make_unique<SignallingClient>())
    , webrtcManager_(std::make_unique<WebRtcManager>()) {
}

CallManager::~CallManager() {
    shutdown();
}

bool CallManager::initialize(const std::string& signallingUrl, const std::string& accessToken) {
    if (initialized_) {
        return true;
    }
    
    std::cout << "[CallManager] Initializing..." << std::endl;
    
    signallingUrl_ = signallingUrl;
    accessToken_ = accessToken;
    
    // Initialize WebRTC
    if (!webrtcManager_->initialize()) {
        std::cerr << "[CallManager] Failed to initialize WebRTC" << std::endl;
        return false;
    }
    
    // Set up signalling callbacks
    setupSignallingCallbacks();
    
    initialized_ = true;
    std::cout << "[CallManager] Initialized successfully" << std::endl;
    return true;
}

void CallManager::shutdown() {
    if (!initialized_) {
        return;
    }
    
    std::cout << "[CallManager] Shutting down..." << std::endl;
    
    // End any active call
    if (isInCall()) {
        endCall();
    }
    
    // Disconnect signalling
    disconnect();
    
    // Shutdown WebRTC
    webrtcManager_->shutdown();
    
    initialized_ = false;
    std::cout << "[CallManager] Shutdown complete" << std::endl;
}

bool CallManager::isInitialized() const {
    return initialized_;
}

void CallManager::connect() {
    if (!initialized_) {
        std::cerr << "[CallManager] Cannot connect: not initialized" << std::endl;
        return;
    }
    
    std::cout << "[CallManager] Connecting to signalling server..." << std::endl;
    signallingClient_->connect(signallingUrl_, accessToken_);
}

void CallManager::disconnect() {
    if (signallingClient_->isConnected()) {
        signallingClient_->disconnect();
    }
}

bool CallManager::isConnected() const {
    return signallingClient_->isConnected();
}

void CallManager::startAudioCall(const std::string& userId, const std::string& displayName) {
    std::cout << "[CallManager] Starting audio call with: " << userId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    // Initialize call info
    currentCall_.callId = "call_" + std::to_string(std::chrono::system_clock::now().time_since_epoch().count());
    currentCall_.roomId = "room_" + userId;
    currentCall_.type = CallType::Audio;
    currentCall_.isOutgoing = true;
    currentCall_.callerId = currentUserId_;
    currentCall_.callerName = currentDisplayName_;
    currentCall_.isActive = true;
    currentCall_.startTime = 0;
    
    // Join the room
    signallingClient_->joinRoom(currentCall_.roomId, currentDisplayName_);
    
    // Start local audio
    webrtcManager_->startLocalAudio();
    muted_ = false;
    videoEnabled_ = false;
}

void CallManager::startVideoCall(const std::string& userId, const std::string& displayName) {
    std::cout << "[CallManager] Starting video call with: " << userId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    // Initialize call info
    currentCall_.callId = "call_" + std::to_string(std::chrono::system_clock::now().time_since_epoch().count());
    currentCall_.roomId = "room_" + userId;
    currentCall_.type = CallType::Video;
    currentCall_.isOutgoing = true;
    currentCall_.callerId = currentUserId_;
    currentCall_.callerName = currentDisplayName_;
    currentCall_.isActive = true;
    currentCall_.startTime = 0;
    
    // Join the room
    signallingClient_->joinRoom(currentCall_.roomId, currentDisplayName_);
    
    // Start local audio and video
    webrtcManager_->startLocalAudio();
    webrtcManager_->startLocalVideo();
    muted_ = false;
    videoEnabled_ = true;
}

void CallManager::acceptCall(const std::string& callId) {
    std::cout << "[CallManager] Accepting call: " << callId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    // Join the room
    signallingClient_->joinRoom(currentCall_.roomId, currentDisplayName_);
    
    // Start media based on call type
    webrtcManager_->startLocalAudio();
    if (currentCall_.type == CallType::Video || currentCall_.type == CallType::GroupVideo) {
        webrtcManager_->startLocalVideo();
        videoEnabled_ = true;
    } else {
        videoEnabled_ = false;
    }
    
    muted_ = false;
    currentCall_.isActive = true;
    currentCall_.startTime = std::chrono::system_clock::now().time_since_epoch().count();
    
    if (onCallConnected_) {
        onCallConnected_();
    }
}

void CallManager::declineCall(const std::string& callId) {
    std::cout << "[CallManager] Declining call: " << callId << std::endl;
    currentCall_ = CallInfo();
}

void CallManager::endCall() {
    std::cout << "[CallManager] Ending call..." << std::endl;
    
    if (!currentCall_.isActive) {
        return;
    }
    
    // Leave the room
    signallingClient_->leaveRoom();
    
    // Stop all media
    webrtcManager_->stopLocalAudio();
    webrtcManager_->stopLocalVideo();
    webrtcManager_->stopScreenShare();
    
    // Close all peer connections
    webrtcManager_->closeAllPeerConnections();
    
    // Clear state
    currentCall_ = CallInfo();
    participants_.clear();
    muted_ = false;
    videoEnabled_ = false;
    
    if (onCallDisconnected_) {
        onCallDisconnected_();
    }
}

void CallManager::startGroupAudioCall(const std::string& groupId) {
    std::cout << "[CallManager] Starting group audio call in: " << groupId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    currentCall_.callId = "groupcall_" + std::to_string(std::chrono::system_clock::now().time_since_epoch().count());
    currentCall_.roomId = "group_" + groupId;
    currentCall_.type = CallType::GroupAudio;
    currentCall_.isOutgoing = true;
    currentCall_.callerId = currentUserId_;
    currentCall_.callerName = currentDisplayName_;
    currentCall_.isActive = true;
    
    signallingClient_->joinRoom(currentCall_.roomId, currentDisplayName_);
    webrtcManager_->startLocalAudio();
    muted_ = false;
    videoEnabled_ = false;
}

void CallManager::startGroupVideoCall(const std::string& groupId) {
    std::cout << "[CallManager] Starting group video call in: " << groupId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    currentCall_.callId = "groupcall_" + std::to_string(std::chrono::system_clock::now().time_since_epoch().count());
    currentCall_.roomId = "group_" + groupId;
    currentCall_.type = CallType::GroupVideo;
    currentCall_.isOutgoing = true;
    currentCall_.callerId = currentUserId_;
    currentCall_.callerName = currentDisplayName_;
    currentCall_.isActive = true;
    
    signallingClient_->joinRoom(currentCall_.roomId, currentDisplayName_);
    webrtcManager_->startLocalAudio();
    webrtcManager_->startLocalVideo();
    muted_ = false;
    videoEnabled_ = true;
}

void CallManager::joinGroupCall(const std::string& roomId) {
    std::cout << "[CallManager] Joining group call: " << roomId << std::endl;
    
    if (!initialized_ || !isConnected()) {
        if (onCallFailed_) {
            onCallFailed_("Not connected to server");
        }
        return;
    }
    
    currentCall_.roomId = roomId;
    currentCall_.type = CallType::GroupVideo; // Assume video by default
    currentCall_.isOutgoing = false;
    currentCall_.isActive = true;
    
    signallingClient_->joinRoom(roomId, currentDisplayName_);
    webrtcManager_->startLocalAudio();
    webrtcManager_->startLocalVideo();
    muted_ = false;
    videoEnabled_ = true;
}

void CallManager::leaveGroupCall() {
    endCall();
}

void CallManager::mute() {
    if (!muted_) {
        webrtcManager_->stopLocalAudio();
        signallingClient_->toggleMute(true);
        muted_ = true;
        std::cout << "[CallManager] Muted" << std::endl;
    }
}

void CallManager::unmute() {
    if (muted_) {
        webrtcManager_->startLocalAudio();
        signallingClient_->toggleMute(false);
        muted_ = false;
        std::cout << "[CallManager] Unmuted" << std::endl;
    }
}

void CallManager::toggleMute() {
    if (muted_) {
        unmute();
    } else {
        mute();
    }
}

bool CallManager::isMuted() const {
    return muted_;
}

void CallManager::enableVideo() {
    if (!videoEnabled_) {
        webrtcManager_->startLocalVideo();
        signallingClient_->toggleVideo(true);
        videoEnabled_ = true;
        std::cout << "[CallManager] Video enabled" << std::endl;
    }
}

void CallManager::disableVideo() {
    if (videoEnabled_) {
        webrtcManager_->stopLocalVideo();
        signallingClient_->toggleVideo(false);
        videoEnabled_ = false;
        std::cout << "[CallManager] Video disabled" << std::endl;
    }
}

void CallManager::toggleVideo() {
    if (videoEnabled_) {
        disableVideo();
    } else {
        enableVideo();
    }
}

bool CallManager::isVideoEnabled() const {
    return videoEnabled_;
}

void CallManager::startScreenShare() {
    if (!webrtcManager_->isScreenSharing()) {
        webrtcManager_->startScreenShare();
        signallingClient_->startScreenShare();
        std::cout << "[CallManager] Screen sharing started" << std::endl;
    }
}

void CallManager::stopScreenShare() {
    if (webrtcManager_->isScreenSharing()) {
        webrtcManager_->stopScreenShare();
        signallingClient_->stopScreenShare();
        std::cout << "[CallManager] Screen sharing stopped" << std::endl;
    }
}

bool CallManager::isScreenSharing() const {
    return webrtcManager_->isScreenSharing();
}

std::vector<MediaDevice> CallManager::getAudioInputDevices() {
    return webrtcManager_->getAudioInputDevices();
}

std::vector<MediaDevice> CallManager::getVideoInputDevices() {
    return webrtcManager_->getVideoInputDevices();
}

std::vector<MediaDevice> CallManager::getAudioOutputDevices() {
    return webrtcManager_->getAudioOutputDevices();
}

void CallManager::selectAudioInput(const std::string& deviceId) {
    webrtcManager_->setAudioInputDevice(deviceId);
}

void CallManager::selectVideoInput(const std::string& deviceId) {
    webrtcManager_->setVideoInputDevice(deviceId);
}

void CallManager::selectAudioOutput(const std::string& deviceId) {
    webrtcManager_->setAudioOutputDevice(deviceId);
}

CallInfo CallManager::getCurrentCall() const {
    return currentCall_;
}

bool CallManager::isInCall() const {
    return currentCall_.isActive;
}

std::vector<RoomParticipant> CallManager::getParticipants() const {
    std::vector<RoomParticipant> result;
    for (const auto& pair : participants_) {
        result.push_back(pair.second);
    }
    return result;
}

void CallManager::configureIce(const std::string& stunServer,
                               const std::vector<std::tuple<std::string, std::string, std::string>>& turnServers) {
    webrtcManager_->clearIceServers();
    webrtcManager_->setStunServer(stunServer);
    for (const auto& turn : turnServers) {
        webrtcManager_->addTurnServer(std::get<0>(turn), std::get<1>(turn), std::get<2>(turn));
    }
}

// Callback setters
void CallManager::setOnIncomingCall(std::function<void(const CallInfo&)> callback) {
    onIncomingCall_ = std::move(callback);
}

void CallManager::setOnCallConnected(std::function<void()> callback) {
    onCallConnected_ = std::move(callback);
}

void CallManager::setOnCallDisconnected(std::function<void()> callback) {
    onCallDisconnected_ = std::move(callback);
}

void CallManager::setOnCallFailed(std::function<void(const std::string&)> callback) {
    onCallFailed_ = std::move(callback);
}

void CallManager::setOnParticipantJoined(std::function<void(const RoomParticipant&)> callback) {
    onParticipantJoined_ = std::move(callback);
}

void CallManager::setOnParticipantLeft(std::function<void(const std::string&)> callback) {
    onParticipantLeft_ = std::move(callback);
}

void CallManager::setOnParticipantUpdated(std::function<void(const RoomParticipant&)> callback) {
    onParticipantUpdated_ = std::move(callback);
}

void CallManager::setOnLocalVideoFrame(OnVideoFrameCallback callback) {
    onLocalVideoFrame_ = std::move(callback);
    webrtcManager_->setLocalVideoCallback(onLocalVideoFrame_);
}

void CallManager::setOnRemoteVideoFrame(const std::string& userId, OnVideoFrameCallback callback) {
    webrtcManager_->setOnRemoteVideoCallback(userId, std::move(callback));
}

// Private methods
void CallManager::setupSignallingCallbacks() {
    signallingClient_->setOnConnected([this]() {
        std::cout << "[CallManager] Connected to signalling server" << std::endl;
    });
    
    signallingClient_->setOnDisconnected([this]() {
        std::cout << "[CallManager] Disconnected from signalling server" << std::endl;
        if (isInCall()) {
            endCall();
        }
    });
    
    signallingClient_->setOnError([this](const std::string& error) {
        std::cerr << "[CallManager] Signalling error: " << error << std::endl;
        if (onCallFailed_) {
            onCallFailed_(error);
        }
    });
    
    signallingClient_->setOnOffer([this](const std::string& userId, const std::string& sdp) {
        handleIncomingOffer(userId, sdp);
    });
    
    signallingClient_->setOnAnswer([this](const std::string& userId, const std::string& sdp) {
        handleIncomingAnswer(userId, sdp);
    });
    
    signallingClient_->setOnIceCandidate([this](const std::string& userId, const IceCandidate& candidate) {
        handleIceCandidate(userId, candidate);
    });
    
    signallingClient_->setOnUserJoined([this](const RoomParticipant& participant) {
        handleUserJoined(participant);
    });
    
    signallingClient_->setOnUserLeft([this](const std::string& userId) {
        handleUserLeft(userId);
    });
    
    signallingClient_->setOnRoomState([this](const RoomState& state) {
        handleRoomState(state);
    });
}

void CallManager::handleIncomingOffer(const std::string& userId, const std::string& sdp) {
    std::cout << "[CallManager] Received offer from: " << userId << std::endl;
    
    // Create peer connection if needed
    if (!webrtcManager_->createPeerConnection(userId)) {
        std::cerr << "[CallManager] Failed to create peer connection for: " << userId << std::endl;
        return;
    }
    
    // Set remote description
    webrtcManager_->setRemoteDescription(userId, sdp, true);
    
    // Create and send answer
    webrtcManager_->createAnswer(userId);
    // Note: In production, the answer would be sent via callback after creation
    
    // Update call state
    if (currentCall_.startTime == 0) {
        currentCall_.startTime = std::chrono::system_clock::now().time_since_epoch().count();
    }
    
    if (onCallConnected_) {
        onCallConnected_();
    }
}

void CallManager::handleIncomingAnswer(const std::string& userId, const std::string& sdp) {
    std::cout << "[CallManager] Received answer from: " << userId << std::endl;
    
    // Set remote description
    webrtcManager_->setRemoteDescription(userId, sdp, false);
    
    // Update call state
    if (currentCall_.startTime == 0) {
        currentCall_.startTime = std::chrono::system_clock::now().time_since_epoch().count();
    }
    
    if (onCallConnected_) {
        onCallConnected_();
    }
}

void CallManager::handleIceCandidate(const std::string& userId, const IceCandidate& candidate) {
    std::cout << "[CallManager] Received ICE candidate from: " << userId << std::endl;
    webrtcManager_->addIceCandidate(userId, candidate);
}

void CallManager::handleUserJoined(const RoomParticipant& participant) {
    std::cout << "[CallManager] User joined: " << participant.displayName << std::endl;
    
    participants_[participant.userId] = participant;
    
    // Create peer connection for new participant
    webrtcManager_->createPeerConnection(participant.userId);
    
    // Create and send offer
    webrtcManager_->createOffer(participant.userId);
    
    if (onParticipantJoined_) {
        onParticipantJoined_(participant);
    }
}

void CallManager::handleUserLeft(const std::string& userId) {
    std::cout << "[CallManager] User left: " << userId << std::endl;
    
    participants_.erase(userId);
    webrtcManager_->closePeerConnection(userId);
    
    if (onParticipantLeft_) {
        onParticipantLeft_(userId);
    }
}

void CallManager::handleRoomState(const RoomState& state) {
    std::cout << "[CallManager] Room state updated, participants: " 
              << state.participants.size() << std::endl;
    
    // Update participants list
    for (const auto& p : state.participants) {
        if (participants_.find(p.userId) == participants_.end()) {
            // New participant
            participants_[p.userId] = p;
            if (onParticipantJoined_) {
                onParticipantJoined_(p);
            }
        } else {
            // Updated participant
            participants_[p.userId] = p;
            if (onParticipantUpdated_) {
                onParticipantUpdated_(p);
            }
        }
    }
}

} // namespace LocalTelegram
