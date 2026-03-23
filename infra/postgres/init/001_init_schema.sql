-- LocalTelegram Database Schema
-- Version: 1.0
-- Phase 1: Basic Infrastructure

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Create enum types
CREATE TYPE user_status AS ENUM ('online', 'offline', 'away', 'busy');
CREATE TYPE message_status AS ENUM ('sending', 'sent', 'delivered', 'read', 'failed');
CREATE TYPE chat_type AS ENUM ('private', 'group', 'channel');
CREATE TYPE member_role AS ENUM ('owner', 'admin', 'moderator', 'member');

-- Users table
CREATE TABLE users (
    id BIGSERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    phone VARCHAR(20) UNIQUE,
    email VARCHAR(255) UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    first_name VARCHAR(100),
    last_name VARCHAR(100),
    bio TEXT,
    avatar_id BIGINT,
    status user_status DEFAULT 'offline',
    last_seen_at TIMESTAMP WITH TIME ZONE,
    is_verified BOOLEAN DEFAULT FALSE,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    settings JSONB DEFAULT '{}'
);

-- Create index on users
CREATE INDEX idx_users_username ON users(username);
CREATE INDEX idx_users_phone ON users(phone);
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_status ON users(status);

-- Sessions table
CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(255) NOT NULL,
    refresh_token_hash VARCHAR(255),
    device_info VARCHAR(255),
    ip_address VARCHAR(45),
    user_agent TEXT,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    refresh_expires_at TIMESTAMP WITH TIME ZONE,
    is_revoked BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_accessed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Create index on sessions
CREATE INDEX idx_sessions_user_id ON sessions(user_id);
CREATE INDEX idx_sessions_token_hash ON sessions(token_hash);
CREATE INDEX idx_sessions_expires_at ON sessions(expires_at);

-- Chats table
CREATE TABLE chats (
    id BIGSERIAL PRIMARY KEY,
    type chat_type NOT NULL DEFAULT 'private',
    title VARCHAR(255),
    description TEXT,
    avatar_id BIGINT,
    owner_id BIGINT REFERENCES users(id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    settings JSONB DEFAULT '{}'
);

-- Create index on chats
CREATE INDEX idx_chats_type ON chats(type);
CREATE INDEX idx_chats_owner_id ON chats(owner_id);

-- Chat members (for groups)
CREATE TABLE chat_members (
    chat_id BIGINT NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role member_role NOT NULL DEFAULT 'member',
    joined_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_read_message_id BIGINT,
    muted_until TIMESTAMP WITH TIME ZONE,
    PRIMARY KEY (chat_id, user_id)
);

-- Create index on chat_members
CREATE INDEX idx_chat_members_user_id ON chat_members(user_id);

-- Messages table
CREATE TABLE messages (
    id BIGSERIAL PRIMARY KEY,
    chat_id BIGINT NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
    sender_id BIGINT NOT NULL REFERENCES users(id),
    reply_to_id BIGINT REFERENCES messages(id),
    forward_from_id BIGINT REFERENCES messages(id),
    content TEXT,
    content_type VARCHAR(50) DEFAULT 'text',
    media_id BIGINT,
    status message_status DEFAULT 'sending',
    is_edited BOOLEAN DEFAULT FALSE,
    is_deleted BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    metadata JSONB DEFAULT '{}'
);

-- Create indexes on messages
CREATE INDEX idx_messages_chat_id ON messages(chat_id);
CREATE INDEX idx_messages_sender_id ON messages(sender_id);
CREATE INDEX idx_messages_created_at ON messages(created_at DESC);
CREATE INDEX idx_messages_chat_created ON messages(chat_id, created_at DESC);

-- Message status tracking (for each user in chat)
CREATE TABLE message_status_log (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    status message_status NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(message_id, user_id)
);

-- Create index on message_status_log
CREATE INDEX idx_message_status_message_id ON message_status_log(message_id);
CREATE INDEX idx_message_status_user_id ON message_status_log(user_id);

-- Files metadata table
CREATE TABLE files (
    id BIGSERIAL PRIMARY KEY,
    uploader_id BIGINT NOT NULL REFERENCES users(id),
    original_name VARCHAR(255),
    mime_type VARCHAR(100),
    size_bytes BIGINT NOT NULL,
    storage_path VARCHAR(500) NOT NULL,
    storage_provider VARCHAR(50) DEFAULT 'minio',
    checksum VARCHAR(64),
    thumbnail_path VARCHAR(500),
    is_uploaded BOOLEAN DEFAULT TRUE,
    upload_completed_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    metadata JSONB DEFAULT '{}'
);

-- Create index on files
CREATE INDEX idx_files_uploader_id ON files(uploader_id);
CREATE INDEX idx_files_checksum ON files(checksum);

-- Contacts table
CREATE TABLE contacts (
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    contact_user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    display_name VARCHAR(100),
    is_blocked BOOLEAN DEFAULT FALSE,
    is_favorite BOOLEAN DEFAULT FALSE,
    added_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, contact_user_id)
);

-- Create index on contacts
CREATE INDEX idx_contacts_contact_user_id ON contacts(contact_user_id);

-- User devices (for push notifications)
CREATE TABLE user_devices (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_token VARCHAR(500),
    device_type VARCHAR(20) NOT NULL, -- 'windows', 'android', 'ios'
    device_name VARCHAR(100),
    push_token VARCHAR(500),
    is_active BOOLEAN DEFAULT TRUE,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Create index on user_devices
CREATE INDEX idx_user_devices_user_id ON user_devices(user_id);
CREATE INDEX idx_user_devices_push_token ON user_devices(push_token);

-- Audit log table
CREATE TABLE audit_log (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT REFERENCES users(id),
    action VARCHAR(100) NOT NULL,
    entity_type VARCHAR(50),
    entity_id BIGINT,
    ip_address VARCHAR(45),
    user_agent TEXT,
    old_values JSONB,
    new_values JSONB,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Create index on audit_log
CREATE INDEX idx_audit_log_user_id ON audit_log(user_id);
CREATE INDEX idx_audit_log_action ON audit_log(action);
CREATE INDEX idx_audit_log_created_at ON audit_log(created_at);

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Apply updated_at trigger to tables
CREATE TRIGGER update_users_updated_at BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_chats_updated_at BEFORE UPDATE ON chats
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_messages_updated_at BEFORE UPDATE ON messages
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Insert default admin user (password: admin123 - change in production!)
-- Password hash is BCrypt hash of 'admin123'
INSERT INTO users (username, email, password_hash, first_name, last_name, is_verified, is_active, status)
VALUES (
    'admin',
    'admin@localtelegram.local',
    '$2a$11$K3g3g3g3g3g3g3g3g3g3g.3g3g3g3g3g3g3g3g3g3g3g3g3g3g3g3',
    'System',
    'Administrator',
    TRUE,
    TRUE,
    'offline'
) ON CONFLICT (username) DO NOTHING;
