# LocalTelegram API Documentation

> OpenAPI 3.0 specification for LocalTelegram REST API
> 
> Version: 1.0.0 | Base URL: `https://your-domain.com/api`

---

## Table of Contents

1. [Authentication](#authentication)
2. [Error Handling](#error-handling)
3. [Rate Limiting](#rate-limiting)
4. [Endpoints](#endpoints)
   - [Auth API](#auth-api)
   - [Users API](#users-api)
   - [Messages API](#messages-api)
   - [Groups API](#groups-api)
   - [Files API](#files-api)
   - [Media API](#media-api)
   - [Conferences API](#conferences-api)
   - [Admin API](#admin-api)
5. [WebSocket Events](#websocket-events)
6. [Data Models](#data-models)

---

## Authentication

All API requests (except auth endpoints) require a Bearer token in the Authorization header:

```
Authorization: Bearer <access_token>
```

### Token Types

| Token Type | Lifetime | Purpose |
|------------|----------|---------|
| Access Token | 15 minutes | API access |
| Refresh Token | 7 days | Token refresh |

---

## Error Handling

All errors follow this format:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message",
    "details": {}
  }
}
```

### HTTP Status Codes

| Code | Description |
|------|-------------|
| 200 | Success |
| 201 | Created |
| 204 | No Content |
| 400 | Bad Request |
| 401 | Unauthorized |
| 403 | Forbidden |
| 404 | Not Found |
| 409 | Conflict |
| 422 | Validation Error |
| 429 | Too Many Requests |
| 500 | Internal Server Error |

---

## Rate Limiting

Rate limits are applied per IP address:

| Endpoint | Limit |
|----------|-------|
| General | 60 req/min |
| Auth | 10 req/min |
| Upload | 20 req/min |
| Messages | 100 req/min |

Headers:
- `X-RateLimit-Limit`: Maximum requests per window
- `X-RateLimit-Remaining`: Remaining requests in current window
- `Retry-After`: Seconds until reset (when rate limited)

---

## Endpoints

### Auth API

Base path: `/api/auth`

#### Register User

```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "string",
  "phone": "+1234567890",
  "password": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Response 201:**
```json
{
  "userId": "string",
  "username": "string",
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": 900
}
```

#### Login

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "string",
  "password": "string"
}
```

**Response 200:**
```json
{
  "userId": "string",
  "username": "string",
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": 900
}
```

#### Refresh Token

```http
POST /api/auth/refresh
Content-Type: application/json

{
  "refreshToken": "string"
}
```

**Response 200:**
```json
{
  "accessToken": "string",
  "refreshToken": "string",
  "expiresIn": 900
}
```

#### Logout

```http
POST /api/auth/logout
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Logout All Sessions

```http
POST /api/auth/logout-all
Authorization: Bearer <token>
```

**Response 204:** No Content

---

### Users API

Base path: `/api/users`

#### Get Current User

```http
GET /api/users/me
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "id": "string",
  "username": "string",
  "phone": "string",
  "firstName": "string",
  "lastName": "string",
  "avatar": "string",
  "status": "online|offline",
  "lastSeen": "2024-01-01T00:00:00Z",
  "createdAt": "2024-01-01T00:00:00Z"
}
```

#### Update Profile

```http
PUT /api/users/me
Authorization: Bearer <token>
Content-Type: application/json

{
  "firstName": "string",
  "lastName": "string",
  "bio": "string"
}
```

**Response 200:** User object

#### Upload Avatar

```http
POST /api/users/me/avatar
Authorization: Bearer <token>
Content-Type: multipart/form-data

file: <binary>
```

**Response 200:**
```json
{
  "avatar": "https://..."
}
```

#### Get User by ID

```http
GET /api/users/{userId}
Authorization: Bearer <token>
```

**Response 200:** User object

#### Search Users

```http
GET /api/users/search?q={query}&page=1&limit=20
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "data": [User],
  "total": 100,
  "page": 1,
  "limit": 20
}
```

#### Get Contacts

```http
GET /api/users/contacts
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "contacts": [
    {
      "id": "string",
      "username": "string",
      "firstName": "string",
      "lastName": "string",
      "avatar": "string",
      "status": "online|offline"
    }
  ]
}
```

#### Add Contact

```http
POST /api/users/contacts/{userId}
Authorization: Bearer <token>
```

**Response 201:** Contact object

#### Remove Contact

```http
DELETE /api/users/contacts/{userId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Block User

```http
POST /api/users/block/{userId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Unblock User

```http
DELETE /api/users/block/{userId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Get Blocked Users

```http
GET /api/users/blocked
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "blocked": [User]
}
```

---

### Messages API

Base path: `/api/messages`

#### Send Message

```http
POST /api/messages
Authorization: Bearer <token>
Content-Type: application/json

{
  "chatId": "string",
  "type": "text|image|video|audio|file|voice",
  "content": "string",
  "replyTo": "string?",
  "metadata": {}
}
```

**Response 201:**
```json
{
  "id": "string",
  "chatId": "string",
  "senderId": "string",
  "type": "text",
  "content": "string",
  "replyTo": "string?",
  "metadata": {},
  "status": "sent",
  "createdAt": "2024-01-01T00:00:00Z",
  "editedAt": null
}
```

#### Get Messages

```http
GET /api/messages/{chatId}?page=1&limit=50&before={messageId}
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "data": [Message],
  "total": 1000,
  "page": 1,
  "limit": 50,
  "hasMore": true
}
```

#### Get Message by ID

```http
GET /api/messages/{chatId}/{messageId}
Authorization: Bearer <token>
```

**Response 200:** Message object

#### Edit Message

```http
PUT /api/messages/{chatId}/{messageId}
Authorization: Bearer <token>
Content-Type: application/json

{
  "content": "string"
}
```

**Response 200:** Message object

#### Delete Message

```http
DELETE /api/messages/{chatId}/{messageId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Forward Message

```http
POST /api/messages/{messageId}/forward
Authorization: Bearer <token>
Content-Type: application/json

{
  "targetChatId": "string"
}
```

**Response 201:** New Message object

#### Mark as Read

```http
POST /api/messages/{chatId}/read
Authorization: Bearer <token>
Content-Type: application/json

{
  "messageId": "string"
}
```

**Response 204:** No Content

#### Add Reaction

```http
POST /api/messages/{chatId}/{messageId}/reactions
Authorization: Bearer <token>
Content-Type: application/json

{
  "emoji": "👍"
}
```

**Response 200:**
```json
{
  "emoji": "👍",
  "count": 5,
  "users": ["userId1", "userId2"]
}
```

#### Remove Reaction

```http
DELETE /api/messages/{chatId}/{messageId}/reactions?emoji=👍
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Pin Message

```http
POST /api/messages/{chatId}/{messageId}/pin
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Unpin Message

```http
DELETE /api/messages/{chatId}/{messageId}/pin
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Search Messages

```http
GET /api/messages/search?q={query}&chatId={chatId}&page=1&limit=20
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "data": [
    {
      "message": Message,
      "highlights": ["...text <mark>query</mark> text..."]
    }
  ],
  "total": 50
}
```

---

### Groups API

Base path: `/api/groups`

#### Create Group

```http
POST /api/groups
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "string",
  "description": "string?",
  "type": "private|public",
  "memberIds": ["userId1", "userId2"]
}
```

**Response 201:**
```json
{
  "id": "string",
  "name": "string",
  "description": "string?",
  "type": "private",
  "avatar": "string?",
  "creatorId": "string",
  "memberCount": 3,
  "createdAt": "2024-01-01T00:00:00Z"
}
```

#### Get Group

```http
GET /api/groups/{groupId}
Authorization: Bearer <token>
```

**Response 200:** Group object

#### Update Group

```http
PUT /api/groups/{groupId}
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "string",
  "description": "string?"
}
```

**Response 200:** Group object

#### Delete Group

```http
DELETE /api/groups/{groupId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Get Group Members

```http
GET /api/groups/{groupId}/members
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "members": [
    {
      "userId": "string",
      "role": "creator|admin|moderator|member",
      "joinedAt": "2024-01-01T00:00:00Z",
      "user": User
    }
  ]
}
```

#### Add Member

```http
POST /api/groups/{groupId}/members
Authorization: Bearer <token>
Content-Type: application/json

{
  "userId": "string",
  "role": "member"
}
```

**Response 201:** Member object

#### Remove Member

```http
DELETE /api/groups/{groupId}/members/{userId}
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Update Member Role

```http
PUT /api/groups/{groupId}/members/{userId}/role
Authorization: Bearer <token>
Content-Type: application/json

{
  "role": "admin|moderator|member"
}
```

**Response 200:** Member object

#### Leave Group

```http
POST /api/groups/{groupId}/leave
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Generate Invite Link

```http
POST /api/groups/{groupId}/invite
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "inviteLink": "https://.../join/abc123",
  "expiresAt": "2024-01-08T00:00:00Z"
}
```

#### Join by Invite

```http
POST /api/groups/join/{inviteCode}
Authorization: Bearer <token>
```

**Response 200:** Group object

#### Upload Group Avatar

```http
POST /api/groups/{groupId}/avatar
Authorization: Bearer <token>
Content-Type: multipart/form-data

file: <binary>
```

**Response 200:**
```json
{
  "avatar": "https://..."
}
```

---

### Files API

Base path: `/api/files`

#### Upload File (Simple)

```http
POST /api/files/upload
Authorization: Bearer <token>
Content-Type: multipart/form-data

file: <binary>
chatId: string
```

**Response 201:**
```json
{
  "id": "string",
  "name": "filename.ext",
  "mimeType": "application/octet-stream",
  "size": 1024,
  "url": "https://...",
  "thumbnailUrl": "https://...",
  "createdAt": "2024-01-01T00:00:00Z"
}
```

#### Upload File (Chunked)

**Step 1: Initialize upload**

```http
POST /api/files/upload/init
Authorization: Bearer <token>
Content-Type: application/json

{
  "filename": "video.mp4",
  "fileSize": 1073741824,
  "mimeType": "video/mp4",
  "chunkSize": 5242880,
  "chatId": "string"
}
```

**Response 201:**
```json
{
  "uploadId": "string",
  "chunkSize": 5242880,
  "totalChunks": 205
}
```

**Step 2: Upload chunks**

```http
PUT /api/files/upload/{uploadId}/chunks/{chunkNumber}
Authorization: Bearer <token>
Content-Type: application/octet-stream

<binary data>
```

**Response 200:**
```json
{
  "chunkNumber": 1,
  "etag": "string"
}
```

**Step 3: Complete upload**

```http
POST /api/files/upload/{uploadId}/complete
Authorization: Bearer <token>
Content-Type: application/json

{
  "parts": [
    { "partNumber": 1, "etag": "string" },
    { "partNumber": 2, "etag": "string" }
  ]
}
```

**Response 200:** File object

#### Download File

```http
GET /api/files/{fileId}
Authorization: Bearer <token>
```

**Response 200:** Binary data with Content-Disposition header

#### Get File Info

```http
GET /api/files/{fileId}/info
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "id": "string",
  "name": "filename.ext",
  "mimeType": "application/octet-stream",
  "size": 1024,
  "url": "https://...",
  "thumbnailUrl": "https://...",
  "metadata": {
    "width": 1920,
    "height": 1080,
    "duration": 120
  }
}
```

#### Delete File

```http
DELETE /api/files/{fileId}
Authorization: Bearer <token>
```

**Response 204:** No Content

---

### Media API

Base path: `/api/media`

#### Transcode Video

```http
POST /api/media/transcode
Authorization: Bearer <token>
Content-Type: application/json

{
  "fileId": "string",
  "qualities": ["720p", "480p", "360p"],
  "generateThumbnail": true
}
```

**Response 202:**
```json
{
  "taskId": "string",
  "status": "pending"
}
```

#### Get Transcoding Status

```http
GET /api/media/transcode/{taskId}
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "taskId": "string",
  "status": "processing|completed|failed",
  "progress": 45,
  "results": [
    {
      "quality": "720p",
      "url": "https://...",
      "size": 52428800
    }
  ]
}
```

#### Stream Video

```http
GET /api/media/stream/{fileId}?quality={quality}
Authorization: Bearer <token>
Range: bytes=0-1024
```

**Response 206:** Partial Content with video data

#### Get Video Thumbnail

```http
GET /api/media/thumbnail/{fileId}?time={seconds}
Authorization: Bearer <token>
```

**Response 200:** Image data

---

### Conferences API

Base path: `/api/conferences`

#### Create Conference Room

```http
POST /api/conferences
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "string",
  "type": "audio|video",
  "groupId": "string?",
  "maxParticipants": 100
}
```

**Response 201:**
```json
{
  "id": "string",
  "name": "string",
  "type": "video",
  "creatorId": "string",
  "participantCount": 1,
  "maxParticipants": 100,
  "createdAt": "2024-01-01T00:00:00Z",
  "livekitRoom": "room_name",
  "token": "livekit_jwt_token"
}
```

#### Join Conference

```http
POST /api/conferences/{roomId}/join
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "roomId": "string",
  "token": "livekit_jwt_token",
  "serverUrl": "wss://livekit.example.com"
}
```

#### Leave Conference

```http
POST /api/conferences/{roomId}/leave
Authorization: Bearer <token>
```

**Response 204:** No Content

#### End Conference

```http
POST /api/conferences/{roomId}/end
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Get Room Participants

```http
GET /api/conferences/{roomId}/participants
Authorization: Bearer <token>
```

**Response 200:**
```json
{
  "participants": [
    {
      "userId": "string",
      "name": "string",
      "avatar": "string",
      "isMuted": false,
      "isVideoEnabled": true,
      "isScreenSharing": false,
      "joinedAt": "2024-01-01T00:00:00Z"
    }
  ]
}
```

#### Mute Participant

```http
POST /api/conferences/{roomId}/participants/{userId}/mute
Authorization: Bearer <token>
```

**Response 204:** No Content

#### Kick Participant

```http
POST /api/conferences/{roomId}/participants/{userId}/kick
Authorization: Bearer <token>
```

**Response 204:** No Content

---

### Admin API

Base path: `/api/admin`

**Requires admin role**

#### Get System Stats

```http
GET /api/admin/stats
Authorization: Bearer <admin_token>
```

**Response 200:**
```json
{
  "users": {
    "total": 1000,
    "active": 850,
    "newToday": 15
  },
  "messages": {
    "total": 50000,
    "today": 500
  },
  "groups": {
    "total": 100,
    "active": 80
  },
  "storage": {
    "used": 1073741824,
    "total": 107374182400,
    "filesCount": 500
  },
  "conferences": {
    "active": 5,
    "totalToday": 20
  }
}
```

#### Get All Users

```http
GET /api/admin/users?page=1&limit=50&search={query}&status={status}
Authorization: Bearer <admin_token>
```

**Response 200:**
```json
{
  "data": [User],
  "total": 1000,
  "page": 1,
  "limit": 50
}
```

#### Update User Status

```http
PUT /api/admin/users/{userId}/status
Authorization: Bearer <admin_token>
Content-Type: application/json

{
  "status": "active|suspended|banned",
  "reason": "string"
}
```

**Response 200:** User object

#### Get Audit Log

```http
GET /api/admin/audit-log?page=1&limit=50&action={action}&userId={userId}&startDate={date}&endDate={date}
Authorization: Bearer <admin_token>
```

**Response 200:**
```json
{
  "data": [
    {
      "id": "string",
      "action": "user.login|user.logout|message.send|...",
      "userId": "string",
      "details": {},
      "ipAddress": "string",
      "userAgent": "string",
      "createdAt": "2024-01-01T00:00:00Z"
    }
  ],
  "total": 10000,
  "page": 1,
  "limit": 50
}
```

#### Get System Settings

```http
GET /api/admin/settings
Authorization: Bearer <admin_token>
```

**Response 200:**
```json
{
  "registration": {
    "enabled": true,
    "requireEmailVerification": false,
    "allowedDomains": []
  },
  "features": {
    "videoCalls": true,
    "screenSharing": true,
    "fileSharing": true,
    "maxFileSize": 2147483648
  },
  "ldap": {
    "enabled": false,
    "server": "",
    "baseDn": ""
  }
}
```

#### Update System Settings

```http
PUT /api/admin/settings
Authorization: Bearer <admin_token>
Content-Type: application/json

{
  "registration": { ... },
  "features": { ... },
  "ldap": { ... }
}
```

**Response 200:** Settings object

---

## WebSocket Events

Connect to: `wss://your-domain.com/hubs/notification`

### Authentication

```json
{
  "type": "authenticate",
  "token": "access_token"
}
```

### Events

#### New Message

```json
{
  "type": "message.new",
  "data": {
    "message": Message,
    "chatId": "string"
  }
}
```

#### Message Updated

```json
{
  "type": "message.updated",
  "data": {
    "message": Message,
    "chatId": "string"
  }
}
```

#### Message Deleted

```json
{
  "type": "message.deleted",
  "data": {
    "messageId": "string",
    "chatId": "string"
  }
}
```

#### User Status Changed

```json
{
  "type": "user.status",
  "data": {
    "userId": "string",
    "status": "online|offline",
    "lastSeen": "2024-01-01T00:00:00Z"
  }
}
```

#### Typing Indicator

```json
{
  "type": "typing",
  "data": {
    "userId": "string",
    "chatId": "string",
    "isTyping": true
  }
}
```

#### Conference Invitation

```json
{
  "type": "conference.invite",
  "data": {
    "roomId": "string",
    "roomName": "string",
    "inviterId": "string",
    "type": "audio|video"
  }
}
```

#### Push Notification

```json
{
  "type": "notification",
  "data": {
    "id": "string",
    "title": "string",
    "body": "string",
    "data": {}
  }
}
```

### Client Methods

#### Send Typing Indicator

```json
{
  "type": "typing",
  "chatId": "string",
  "isTyping": true
}
```

#### Mark Messages as Read

```json
{
  "type": "markRead",
  "chatId": "string",
  "messageId": "string"
}
```

---

## Data Models

### User

```typescript
interface User {
  id: string;
  username: string;
  phone: string;
  firstName: string;
  lastName: string;
  avatar?: string;
  bio?: string;
  status: 'online' | 'offline';
  lastSeen: Date;
  createdAt: Date;
}
```

### Message

```typescript
interface Message {
  id: string;
  chatId: string;
  senderId: string;
  type: 'text' | 'image' | 'video' | 'audio' | 'file' | 'voice';
  content: string;
  replyTo?: string;
  forwardFrom?: string;
  metadata?: {
    fileName?: string;
    fileSize?: number;
    mimeType?: string;
    width?: number;
    height?: number;
    duration?: number;
    thumbnailUrl?: string;
  };
  reactions?: {
    emoji: string;
    count: number;
    users: string[];
  }[];
  isPinned: boolean;
  isEdited: boolean;
  status: 'sent' | 'delivered' | 'read';
  createdAt: Date;
  editedAt?: Date;
}
```

### Group

```typescript
interface Group {
  id: string;
  name: string;
  description?: string;
  type: 'private' | 'public';
  avatar?: string;
  creatorId: string;
  memberCount: number;
  createdAt: Date;
}
```

### File

```typescript
interface File {
  id: string;
  name: string;
  mimeType: string;
  size: number;
  url: string;
  thumbnailUrl?: string;
  metadata?: {
    width?: number;
    height?: number;
    duration?: number;
  };
  createdAt: Date;
}
```

---

## OpenAPI Specification

Full OpenAPI 3.0 specification available at: `/api/openapi.json`

Or in YAML format at: `/api/openapi.yaml`

---

## SDK Examples

### JavaScript/TypeScript

```typescript
import { LocalTelegramClient } from '@localtelegram/sdk';

const client = new LocalTelegramClient({
  baseUrl: 'https://your-domain.com/api',
  wsUrl: 'wss://your-domain.com/hubs/notification'
});

// Login
await client.auth.login({ username: 'user', password: 'pass' });

// Send message
await client.messages.send({
  chatId: 'chat-123',
  type: 'text',
  content: 'Hello!'
});

// Listen for new messages
client.on('message.new', (message) => {
  console.log('New message:', message);
});
```

### C# (.NET)

```csharp
using LocalTelegram.Client;

var client = new LocalTelegramClient("https://your-domain.com/api");

// Login
await client.Auth.LoginAsync("user", "password");

// Send message
await client.Messages.SendAsync(new SendMessageRequest
{
    ChatId = "chat-123",
    Type = MessageType.Text,
    Content = "Hello!"
});

// Listen for new messages
client.OnMessageReceived += (sender, message) =>
{
    Console.WriteLine($"New message: {message.Content}");
};
```

---

*Last updated: March 2026*
