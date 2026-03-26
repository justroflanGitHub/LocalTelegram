# LocalTelegram Database Schema Documentation

> Version: 1.0 | Date: March 2026

---

## Table of Contents

1. [Overview](#overview)
2. [PostgreSQL Schema](#postgresql-schema)
3. [MongoDB Collections](#mongodb-collections)
4. [Redis Data Structures](#redis-data-structures)
5. [Indexes](#indexes)
6. [Data Migration](#data-migration)

---

## Overview

LocalTelegram uses a polyglot persistence approach:
- **PostgreSQL**: Relational data (users, messages, groups)
- **MongoDB**: Document data (message metadata, logs)
- **Redis**: Caching and session data
- **MinIO**: File storage

---

## PostgreSQL Schema

### Users & Authentication

#### `users` Table

```sql
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_number VARCHAR(20) UNIQUE,
    username VARCHAR(50) UNIQUE,
    email VARCHAR(255) UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    first_name VARCHAR(100),
    last_name VARCHAR(100),
    bio TEXT,
    avatar_url VARCHAR(500),
    is_active BOOLEAN DEFAULT true,
    is_verified BOOLEAN DEFAULT false,
    is_bot BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    last_seen_at TIMESTAMP WITH TIME ZONE,
    deleted_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_users_phone ON users(phone_number);
CREATE INDEX idx_users_username ON users(username);
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_created_at ON users(created_at);
```

#### `sessions` Table

```sql
CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id UUID NOT NULL,
    refresh_token_hash VARCHAR(255) NOT NULL,
    ip_address INET,
    user_agent TEXT,
    platform VARCHAR(50),
    device_name VARCHAR(100),
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    last_activity_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_sessions_user ON sessions(user_id);
CREATE INDEX idx_sessions_device ON sessions(device_id);
CREATE INDEX idx_sessions_token ON sessions(refresh_token_hash);
CREATE INDEX idx_sessions_expires ON sessions(expires_at);
```

#### `devices` Table

```sql
CREATE TABLE devices (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_fingerprint VARCHAR(255) NOT NULL,
    platform VARCHAR(50) NOT NULL,
    device_name VARCHAR(100),
    os_version VARCHAR(50),
    app_version VARCHAR(50),
    push_token TEXT,
    is_trusted BOOLEAN DEFAULT false,
    last_used_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_devices_user ON devices(user_id);
CREATE INDEX idx_devices_fingerprint ON devices(device_fingerprint);
```

#### `two_factor_secrets` Table

```sql
CREATE TABLE two_factor_secrets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    secret_key VARCHAR(255) NOT NULL,
    backup_codes TEXT[],
    method VARCHAR(20) NOT NULL, -- 'totp', 'sms'
    is_enabled BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(user_id)
);
```

### Messages

#### `conversations` Table

```sql
CREATE TYPE conversation_type AS ENUM ('private', 'group', 'channel');
CREATE TYPE conversation_visibility AS ENUM ('normal', 'archived', 'hidden');

CREATE TABLE conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type conversation_type NOT NULL DEFAULT 'private',
    title VARCHAR(255),
    avatar_url VARCHAR(500),
    created_by UUID REFERENCES users(id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    deleted_at TIMESTAMP WITH TIME ZONE,
    
    -- For groups
    description TEXT,
    is_public BOOLEAN DEFAULT false,
    invite_link VARCHAR(100) UNIQUE,
    
    -- For channels
    subscriber_count INTEGER DEFAULT 0
);

CREATE INDEX idx_conversations_type ON conversations(type);
CREATE INDEX idx_conversations_created_by ON conversations(created_by);
```

#### `conversation_members` Table

```sql
CREATE TYPE member_role AS ENUM ('member', 'admin', 'owner');
CREATE TYPE member_status AS ENUM ('active', 'left', 'kicked', 'banned');

CREATE TABLE conversation_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role member_role NOT NULL DEFAULT 'member',
    status member_status NOT NULL DEFAULT 'active',
    joined_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    left_at TIMESTAMP WITH TIME ZONE,
    last_read_at TIMESTAMP WITH TIME ZONE,
    unread_count INTEGER DEFAULT 0,
    is_muted BOOLEAN DEFAULT false,
    mute_until TIMESTAMP WITH TIME ZONE,
    is_pinned BOOLEAN DEFAULT false,
    
    UNIQUE(conversation_id, user_id)
);

CREATE INDEX idx_conversation_members_conversation ON conversation_members(conversation_id);
CREATE INDEX idx_conversation_members_user ON conversation_members(user_id);
CREATE INDEX idx_conversation_members_status ON conversation_members(status);
```

#### `messages` Table

```sql
CREATE TYPE message_status AS ENUM ('sending', 'sent', 'delivered', 'read', 'failed');
CREATE TYPE message_type AS ENUM ('text', 'photo', 'video', 'audio', 'voice', 'document', 'sticker', 'location', 'contact', 'system');

CREATE TABLE messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    sender_id UUID NOT NULL REFERENCES users(id),
    reply_to_id UUID REFERENCES messages(id),
    forwarded_from_id UUID REFERENCES messages(id),
    type message_type NOT NULL DEFAULT 'text',
    content TEXT,
    media_id UUID, -- Reference to file/media
    media_metadata JSONB,
    status message_status NOT NULL DEFAULT 'sending',
    edit_history JSONB,
    is_edited BOOLEAN DEFAULT false,
    is_deleted BOOLEAN DEFAULT false,
    is_pinned BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    deleted_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_messages_conversation ON messages(conversation_id);
CREATE INDEX idx_messages_sender ON messages(sender_id);
CREATE INDEX idx_messages_created_at ON messages(created_at DESC);
CREATE INDEX idx_messages_reply_to ON messages(reply_to_id);
CREATE INDEX idx_messages_type ON messages(type);
CREATE INDEX idx_messages_pinned ON messages(conversation_id, is_pinned) WHERE is_pinned = true;
```

#### `message_reactions` Table

```sql
CREATE TABLE message_reactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reaction VARCHAR(10) NOT NULL, -- Emoji or reaction code
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(message_id, user_id, reaction)
);

CREATE INDEX idx_message_reactions_message ON message_reactions(message_id);
CREATE INDEX idx_message_reactions_user ON message_reactions(user_id);
```

#### `pinned_messages` Table

```sql
CREATE TABLE pinned_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    pinned_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(conversation_id, message_id)
);

CREATE INDEX idx_pinned_messages_conversation ON pinned_messages(conversation_id);
```

### Contacts & Privacy

#### `contacts` Table

```sql
CREATE TABLE contacts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    contact_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    display_name VARCHAR(100),
    is_favorite BOOLEAN DEFAULT false,
    is_blocked BOOLEAN DEFAULT false,
    added_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(user_id, contact_user_id)
);

CREATE INDEX idx_contacts_user ON contacts(user_id);
CREATE INDEX idx_contacts_contact ON contacts(contact_user_id);
```

#### `blocked_users` Table

```sql
CREATE TABLE blocked_users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    blocked_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(user_id, blocked_user_id)
);

CREATE INDEX idx_blocked_users_user ON blocked_users(user_id);
CREATE INDEX idx_blocked_users_blocked ON blocked_users(blocked_user_id);
```

#### `privacy_settings` Table

```sql
CREATE TYPE privacy_level AS ENUM ('nobody', 'contacts', 'everyone');

CREATE TABLE privacy_settings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    last_seen_visibility privacy_level DEFAULT 'everyone',
    profile_photo_visibility privacy_level DEFAULT 'everyone',
    phone_number_visibility privacy_level DEFAULT 'contacts',
    voice_call_privacy privacy_level DEFAULT 'everyone',
    invite_privacy privacy_level DEFAULT 'everyone',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(user_id)
);
```

#### `privacy_exceptions` Table

```sql
CREATE TYPE exception_type AS ENUM ('allow', 'disallow');

CREATE TABLE privacy_exceptions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    exception_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    setting VARCHAR(50) NOT NULL, -- 'last_seen', 'profile_photo', etc.
    type exception_type NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(user_id, exception_user_id, setting)
);

CREATE INDEX idx_privacy_exceptions_user ON privacy_exceptions(user_id);
```

### Groups

#### `groups` Table

```sql
CREATE TABLE groups (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    avatar_url VARCHAR(500),
    owner_id UUID NOT NULL REFERENCES users(id),
    is_public BOOLEAN DEFAULT false,
    invite_link VARCHAR(100) UNIQUE,
    member_count INTEGER DEFAULT 0,
    max_members INTEGER DEFAULT 200,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    deleted_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_groups_owner ON groups(owner_id);
CREATE INDEX idx_groups_invite_link ON groups(invite_link);
CREATE INDEX idx_groups_public ON groups(is_public) WHERE is_public = true;
```

#### `group_members` Table

```sql
CREATE TYPE group_role AS ENUM ('member', 'admin', 'owner');

CREATE TABLE group_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role group_role NOT NULL DEFAULT 'member',
    joined_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    invited_by UUID REFERENCES users(id),
    
    UNIQUE(group_id, user_id)
);

CREATE INDEX idx_group_members_group ON group_members(group_id);
CREATE INDEX idx_group_members_user ON group_members(user_id);
```

#### `group_roles` Table

```sql
CREATE TABLE group_roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    name VARCHAR(50) NOT NULL,
    permissions JSONB NOT NULL DEFAULT '{}',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(group_id, name)
);

CREATE INDEX idx_group_roles_group ON group_roles(group_id);
```

#### `group_invites` Table

```sql
CREATE TYPE invite_status AS ENUM ('pending', 'accepted', 'declined', 'expired');

CREATE TABLE group_invites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id UUID NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    inviter_id UUID NOT NULL REFERENCES users(id),
    invitee_id UUID REFERENCES users(id),
    invite_code VARCHAR(50) UNIQUE,
    status invite_status NOT NULL DEFAULT 'pending',
    expires_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    responded_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_group_invites_group ON group_invites(group_id);
CREATE INDEX idx_group_invites_invitee ON group_invites(invitee_id);
CREATE INDEX idx_group_invites_code ON group_invites(invite_code);
```

### Files & Media

#### `files` Table

```sql
CREATE TYPE file_status AS ENUM ('uploading', 'active', 'deleted', 'failed');

CREATE TABLE files (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    original_name VARCHAR(255),
    mime_type VARCHAR(100) NOT NULL,
    size BIGINT NOT NULL,
    storage_path VARCHAR(500) NOT NULL,
    storage_bucket VARCHAR(100),
    hash_sha256 VARCHAR(64),
    status file_status NOT NULL DEFAULT 'uploading',
    metadata JSONB,
    thumbnail_path VARCHAR(500),
    preview_path VARCHAR(500),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    deleted_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_files_user ON files(user_id);
CREATE INDEX idx_files_hash ON files(hash_sha256);
CREATE INDEX idx_files_status ON files(status);
```

#### `chunked_uploads` Table

```sql
CREATE TABLE chunked_uploads (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    upload_id VARCHAR(100) UNIQUE NOT NULL,
    user_id UUID NOT NULL REFERENCES users(id),
    file_name VARCHAR(255),
    mime_type VARCHAR(100),
    total_size BIGINT NOT NULL,
    chunk_size INTEGER NOT NULL,
    total_chunks INTEGER NOT NULL,
    uploaded_chunks INTEGER[] DEFAULT '{}',
    status VARCHAR(20) DEFAULT 'pending',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX idx_chunked_uploads_upload_id ON chunked_uploads(upload_id);
CREATE INDEX idx_chunked_uploads_user ON chunked_uploads(user_id);
```

### Video Conferencing

#### `conference_rooms` Table

```sql
CREATE TYPE room_status AS ENUM ('waiting', 'active', 'ended');

CREATE TABLE conference_rooms (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    description TEXT,
    created_by UUID NOT NULL REFERENCES users(id),
    group_id UUID REFERENCES groups(id),
    livekit_room VARCHAR(100) UNIQUE,
    status room_status NOT NULL DEFAULT 'waiting',
    max_participants INTEGER DEFAULT 100,
    is_recording BOOLEAN DEFAULT false,
    started_at TIMESTAMP WITH TIME ZONE,
    ended_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_conference_rooms_created_by ON conference_rooms(created_by);
CREATE INDEX idx_conference_rooms_group ON conference_rooms(group_id);
CREATE INDEX idx_conference_rooms_status ON conference_rooms(status);
```

#### `conference_participants` Table

```sql
CREATE TABLE conference_participants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    room_id UUID NOT NULL REFERENCES conference_rooms(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id),
    identity VARCHAR(100) NOT NULL,
    joined_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    left_at TIMESTAMP WITH TIME ZONE,
    is_muted BOOLEAN DEFAULT false,
    is_video_enabled BOOLEAN DEFAULT true,
    is_screen_sharing BOOLEAN DEFAULT false
);

CREATE INDEX idx_conference_participants_room ON conference_participants(room_id);
CREATE INDEX idx_conference_participants_user ON conference_participants(user_id);
```

### Administration

#### `audit_logs` Table

```sql
CREATE TABLE audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_id UUID REFERENCES users(id),
    action VARCHAR(100) NOT NULL,
    resource_type VARCHAR(50) NOT NULL,
    resource_id UUID,
    old_values JSONB,
    new_values JSONB,
    ip_address INET,
    user_agent TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_audit_logs_actor ON audit_logs(actor_id);
CREATE INDEX idx_audit_logs_action ON audit_logs(action);
CREATE INDEX idx_audit_logs_resource ON audit_logs(resource_type, resource_id);
CREATE INDEX idx_audit_logs_created_at ON audit_logs(created_at DESC);
```

#### `reports` Table

```sql
CREATE TYPE report_status AS ENUM ('pending', 'reviewing', 'resolved', 'dismissed');

CREATE TABLE reports (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reporter_id UUID NOT NULL REFERENCES users(id),
    reported_user_id UUID REFERENCES users(id),
    reported_message_id UUID REFERENCES messages(id),
    reason VARCHAR(100) NOT NULL,
    description TEXT,
    status report_status NOT NULL DEFAULT 'pending',
    reviewed_by UUID REFERENCES users(id),
    reviewed_at TIMESTAMP WITH TIME ZONE,
    resolution_note TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_reports_reporter ON reports(reporter_id);
CREATE INDEX idx_reports_reported_user ON reports(reported_user_id);
CREATE INDEX idx_reports_status ON reports(status);
```

#### `user_sanctions` Table

```sql
CREATE TYPE sanction_type AS ENUM ('warning', 'mute', 'ban', 'kick');
CREATE TYPE sanction_status AS ENUM ('active', 'expired', 'revoked');

CREATE TABLE user_sanctions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    type sanction_type NOT NULL,
    reason TEXT NOT NULL,
    issued_by UUID NOT NULL REFERENCES users(id),
    status sanction_status NOT NULL DEFAULT 'active',
    expires_at TIMESTAMP WITH TIME ZONE,
    revoked_by UUID REFERENCES users(id),
    revoked_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_user_sanctions_user ON user_sanctions(user_id);
CREATE INDEX idx_user_sanctions_status ON user_sanctions(status);
CREATE INDEX idx_user_sanctions_expires ON user_sanctions(expires_at) WHERE status = 'active';
```

### Invites & Registration

#### `invite_codes` Table

```sql
CREATE TABLE invite_codes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code VARCHAR(50) UNIQUE NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id),
    email VARCHAR(255),
    max_uses INTEGER DEFAULT 1,
    current_uses INTEGER DEFAULT 0,
    expires_at TIMESTAMP WITH TIME ZONE,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_invite_codes_code ON invite_codes(code);
CREATE INDEX idx_invite_codes_created_by ON invite_codes(created_by);
```

#### `user_invites` Table

```sql
CREATE TABLE user_invites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) NOT NULL,
    invited_by UUID NOT NULL REFERENCES users(id),
    invite_code_id UUID REFERENCES invite_codes(id),
    status VARCHAR(20) DEFAULT 'pending',
    sent_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    accepted_at TIMESTAMP WITH TIME ZONE,
    accepted_by UUID REFERENCES users(id)
);

CREATE INDEX idx_user_invites_email ON user_invites(email);
CREATE INDEX idx_user_invites_invited_by ON user_invites(invited_by);
```

### Data Retention

#### `retention_policies` Table

```sql
CREATE TABLE retention_policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    resource_type VARCHAR(50) NOT NULL,
    retention_days INTEGER NOT NULL,
    action VARCHAR(20) NOT NULL, -- 'delete', 'archive'
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
```

#### `data_export_requests` Table

```sql
CREATE TYPE export_status AS ENUM ('pending', 'processing', 'completed', 'failed');

CREATE TABLE data_export_requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    status export_status NOT NULL DEFAULT 'pending',
    file_path VARCHAR(500),
    file_size BIGINT,
    requested_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    completed_at TIMESTAMP WITH TIME ZONE,
    expires_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_data_export_requests_user ON data_export_requests(user_id);
CREATE INDEX idx_data_export_requests_status ON data_export_requests(status);
```

---

## MongoDB Collections

### `message_delivery_status`

```javascript
{
    _id: ObjectId,
    message_id: UUID,
    conversation_id: UUID,
    recipients: [
        {
            user_id: UUID,
            status: "delivered" | "read",
            timestamp: ISODate
        }
    ],
    created_at: ISODate,
    updated_at: ISODate
}
```

### `typing_indicators`

```javascript
{
    _id: ObjectId,
    conversation_id: UUID,
    user_id: UUID,
    started_at: ISODate,
    expires_at: ISODate
}
```

### `presence_status`

```javascript
{
    _id: ObjectId,
    user_id: UUID,
    status: "online" | "offline" | "away",
    last_seen: ISODate,
    device_ids: [UUID],
    updated_at: ISODate
}
```

### `push_subscriptions`

```javascript
{
    _id: ObjectId,
    user_id: UUID,
    device_id: UUID,
    platform: "windows" | "android" | "ios" | "web",
    push_token: String,
    is_active: Boolean,
    created_at: ISODate,
    updated_at: ISODate
}
```

### `call_logs`

```javascript
{
    _id: ObjectId,
    room_id: UUID,
    participants: [UUID],
    started_at: ISODate,
    ended_at: ISODate,
    duration_seconds: Number,
    recording_url: String,
    metadata: {}
}
```

---

## Redis Data Structures

### Session Data

```
# Active session
session:{session_id} = {
    user_id: UUID,
    device_id: UUID,
    created_at: timestamp,
    expires_at: timestamp
}
TTL: session expiration time

# User sessions list
user:sessions:{user_id} = [session_id, ...]
```

### Rate Limiting

```
# Request counter
ratelimit:{endpoint}:{user_id}:{window} = count
TTL: window duration

# IP-based rate limit
ratelimit:ip:{ip_address}:{endpoint} = count
TTL: window duration
```

### Caching

```
# User profile cache
cache:user:profile:{user_id} = JSON
TTL: 15 minutes

# Contact list cache
cache:user:contacts:{user_id} = JSON
TTL: 10 minutes

# Conversation metadata
cache:conversation:{conversation_id} = JSON
TTL: 30 minutes

# File metadata
cache:file:{file_id} = JSON
TTL: 60 minutes
```

### Real-time Data

```
# Online users set
online:users = {user_id, ...}

# User connection count
connections:user:{user_id} = count

# WebSocket connection mapping
ws:connection:{connection_id} = user_id

# Typing indicators
typing:{conversation_id} = {user_id, ...}
TTL: 10 seconds
```

### Badge Counts

```
# Unread message count per conversation
badge:user:{user_id}:conversation:{conversation_id} = count

# Total unread count
badge:user:{user_id}:total = count
```

---

## Indexes

### Critical Indexes

```sql
-- Messages (most queried)
CREATE INDEX idx_messages_conversation_created ON messages(conversation_id, created_at DESC);
CREATE INDEX idx_messages_sender_created ON messages(sender_id, created_at DESC);

-- Conversations
CREATE INDEX idx_conversation_members_user_status ON conversation_members(user_id, status);

-- Users
CREATE INDEX idx_users_phone_active ON users(phone_number) WHERE is_active = true;
CREATE INDEX idx_users_username_active ON users(username) WHERE is_active = true;

-- Audit logs
CREATE INDEX idx_audit_logs_created_at_desc ON audit_logs(created_at DESC);
```

### Partial Indexes

```sql
-- Active users only
CREATE INDEX idx_users_active ON users(id) WHERE is_active = true AND deleted_at IS NULL;

-- Unread messages
CREATE INDEX idx_conversation_members_unread ON conversation_members(user_id) 
WHERE unread_count > 0;

-- Pending reports
CREATE INDEX idx_reports_pending ON reports(created_at DESC) 
WHERE status = 'pending';

-- Active sanctions
CREATE INDEX idx_sanctions_active ON user_sanctions(user_id, expires_at) 
WHERE status = 'active';
```

---

## Data Migration

### Migration Scripts Location

```
/migrations/
├── postgres/
│   ├── 001_initial_schema.sql
│   ├── 002_add_2fa.sql
│   ├── 003_add_groups.sql
│   └── ...
└── mongo/
    ├── 001_initial_collections.js
    └── ...
```

### Migration Best Practices

1. **Always backup before migration**
2. **Test on staging environment first**
3. **Use transactions where possible**
4. **Plan for rollback**
5. **Monitor performance during migration**

### Example Migration

```sql
-- 005_add_message_reactions.sql
BEGIN;

-- Create enum type
CREATE TYPE reaction_type AS ENUM ('emoji', 'custom');

-- Add reactions table
CREATE TABLE message_reactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    reaction VARCHAR(10) NOT NULL,
    reaction_type reaction_type DEFAULT 'emoji',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    
    UNIQUE(message_id, user_id, reaction)
);

-- Add index
CREATE INDEX idx_message_reactions_message ON message_reactions(message_id);

-- Record migration
INSERT INTO schema_migrations (version, applied_at)
VALUES ('005', NOW());

COMMIT;
```

---

*Document created: March 2026*  
*Last updated: March 2026*
