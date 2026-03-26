# LocalTelegram FAQ (Frequently Asked Questions)

> Version: 1.0 | Date: March 2026

---

## General Questions

### What is LocalTelegram?

LocalTelegram is a self-hosted, closed messenger system designed for corporate and private environments. It provides Telegram-like functionality while keeping all data within your own infrastructure.

### How is LocalTelegram different from Telegram?

| Feature | LocalTelegram | Telegram |
|---------|---------------|----------|
| Hosting | Self-hosted | Cloud-based |
| Data Control | Full control | Stored on Telegram servers |
| User Base | Closed (invite-only) | Open to anyone |
| Customization | Fully customizable | Limited |
| LDAP/AD | Native support | No |
| Compliance | Full GDPR control | Telegram's policies |

### Is LocalTelegram free?

Yes, LocalTelegram is open source and free to use. However, you need to provide your own infrastructure (servers, storage, network).

### What platforms are supported?

- **Desktop**: Windows 10/11 (64-bit)
- **Mobile**: Android 8.0+
- **Web Admin Panel**: Any modern browser

### Can I use LocalTelegram without internet?

Yes, LocalTelegram can work completely offline within your local network. Only external push notifications require internet access.

---

## Installation & Setup

### What are the system requirements?

**Server:**
- Docker and Docker Compose
- 4+ CPU cores
- 8+ GB RAM
- 100+ GB storage
- Linux (Ubuntu 22.04+ recommended) or Windows Server

**Client:**
- Windows 10/11 (64-bit)
- Android 8.0+

### How do I install LocalTelegram?

1. Clone the repository
2. Configure environment variables
3. Run `docker-compose up -d`
4. Access the admin panel to create initial admin account
5. Distribute client apps to users

See the [Deployment Guide](DEPLOYMENT.md) for detailed instructions.

### Can I run LocalTelegram on a single server?

Yes, LocalTelegram can run on a single server for small deployments (up to ~500 users). For larger deployments, consider distributed setup.

### How do I update LocalTelegram?

1. Backup your data
2. Pull latest changes: `git pull`
3. Rebuild containers: `docker-compose build`
4. Restart services: `docker-compose up -d`

---

## User Management

### How do users register?

By default, registration is invite-only. Administrators can:
1. Create invite codes/links
2. Send email invitations
3. Manually create accounts

### Can I disable open registration?

Yes, set `REQUIRE_INVITE=true` in your environment configuration.

### How do I integrate with LDAP/Active Directory?

1. Enable LDAP in settings
2. Configure LDAP server URL and base DN
3. Set up attribute mapping
4. Test connection
5. Users can now login with LDAP credentials

### Can users change their usernames?

Yes, users can change their usernames in Settings → Profile. Usernames must be unique.

### How do I reset a user's password?

As administrator:
1. Go to Admin Panel → Users
2. Find the user
3. Click "Reset Password"
4. Choose to send reset email or set temporary password

---

## Messaging

### What message types are supported?

- Text messages (with formatting)
- Photos and images
- Videos
- Voice messages
- Audio files
- Documents (any file type)
- Locations
- Contacts

### Is there a message size limit?

- Text: 4,000 characters
- Files: 2 GB (configurable)
- Photos: 50 MB

### Can messages be edited?

Yes, users can edit their own messages within 48 hours (configurable). Edited messages show an "edited" indicator.

### Can messages be deleted?

Yes, users can delete their own messages. Administrators can delete any message. Deleted messages cannot be recovered.

### Are messages encrypted?

Messages are encrypted in transit (TLS). Optional end-to-end encryption is available for secret chats.

### How long are messages stored?

Messages are stored indefinitely by default. Administrators can configure retention policies to automatically delete old messages.

---

## Groups

### What's the maximum group size?

Default: 200 members
Maximum: 10,000 members (with performance tuning)

### Can I create public groups?

Yes, groups can be:
- **Private**: Invite-only, not searchable
- **Public**: Discoverable, anyone can join via link

### What are group roles?

| Role | Permissions |
|------|-------------|
| Owner | All permissions, can delete group |
| Admin | Manage members, edit settings, pin messages |
| Member | Send messages, view content |

### Can I limit what members can do?

Yes, admins can restrict:
- Sending messages
- Sending media
- Adding members
- Creating polls
- Pinning messages

---

## Files & Media

### Where are files stored?

Files are stored in MinIO (S3-compatible storage). You can also configure external S3 storage.

### How much storage do I need?

Estimate based on:
- Number of users
- Average files per user per day
- Average file size

Example: 100 users × 10 files/day × 5 MB = 5 GB/day

### Are files encrypted?

Files are encrypted at rest using server-side encryption. Configure encryption keys in MinIO settings.

### Can I limit file types?

Yes, administrators can configure allowed file extensions and MIME types.

### How do I backup files?

Files are backed up automatically with the backup system. You can also configure separate backup schedules for files.

---

## Video Calls

### What's needed for video calls?

Server-side:
- LiveKit SFU (included in docker-compose)
- TURN server (for NAT traversal)

Client-side:
- Camera permission
- Microphone permission
- Stable internet connection (min 1 Mbps)

### How many people can join a call?

- 1-on-1 calls: 2 participants
- Group calls: Up to 100 participants (configurable)

### Can I record calls?

Yes, call recording can be enabled. Recordings are stored in MinIO and accessible to administrators.

### Do video calls work through firewalls?

Yes, TURN servers handle NAT traversal. Ensure TURN ports (3478, 5349) are accessible.

---

## Security

### Is two-factor authentication supported?

Yes, LocalTelegram supports:
- TOTP (Google Authenticator, Authy, etc.)
- SMS codes (requires SMS gateway)

### How do I enable 2FA?

1. Go to Settings → Security
2. Click "Enable 2FA"
3. Scan QR code with authenticator app
4. Enter verification code
5. Save backup codes

### What happens if I lose my 2FA device?

Contact your administrator. They can:
1. Verify your identity
2. Disable 2FA on your account
3. You can then re-enable 2FA

### Can I see where I'm logged in?

Yes, go to Settings → Security → Active Sessions to see all devices and locations.

### How do I log out of other devices?

1. Go to Settings → Security → Active Sessions
2. Click "Terminate All Other Sessions"
3. Confirm action

---

## Privacy

### What data is collected?

LocalTelegram collects only essential data:
- Account information (email, name)
- Messages and files you share
- Login history
- Device information

### Who can see my data?

- Your messages: Only you and recipients
- Your profile: Based on your privacy settings
- System data: Administrators (for moderation)

### Can I export my data?

Yes, go to Settings → Account → Export Data. You'll receive a ZIP file with:
- Profile information
- Messages
- Files
- Contacts

### Can I delete my account?

Yes, go to Settings → Account → Delete Account. This:
- Deletes all your messages
- Removes you from groups
- Deletes your files
- Cannot be undone

### Does LocalTelegram comply with GDPR?

Yes, LocalTelegram provides:
- Data export functionality
- Account deletion
- Data retention controls
- Privacy settings
- Audit logging

---

## Administration

### How do I access the admin panel?

Navigate to `https://admin.your-domain.com` and log in with admin credentials.

### What can administrators do?

- Manage users (create, edit, disable, delete)
- Manage groups (view, edit, delete)
- Handle reports and moderation
- Configure system settings
- View audit logs
- Monitor system health

### Can I have multiple administrators?

Yes, you can create multiple admin accounts with different roles:
- Super Admin: Full access
- Admin: User management, moderation
- Moderator: Reports and warnings only
- Support: Read-only access

### How do I see what changes were made?

All administrative actions are logged in the Audit Log. Go to Admin Panel → Audit Log to view:
- Who made the change
- What was changed
- When it was changed
- Previous and new values

---

## Technical

### What database does LocalTelegram use?

- **PostgreSQL 16**: Primary database for users, messages, groups
- **MongoDB**: Optional, for logs and metadata
- **Redis**: Caching and sessions

### Can I use an external database?

Yes, configure external database connections in environment variables:
```
POSTGRES_HOST=external-db.example.com
POSTGRES_PORT=5432
```

### How do I scale LocalTelegram?

**Horizontal scaling:**
1. Add more API service instances
2. Use load balancer
3. Configure Redis cluster
4. Set up PostgreSQL read replicas

**Vertical scaling:**
1. Increase server resources
2. Tune database configuration
3. Optimize queries

### What ports does LocalTelegram use?

| Service | Port | Protocol |
|---------|------|----------|
| nginx (HTTPS) | 443 | TCP |
| API Gateway | 5000 | HTTP |
| Auth Service | 5001 | HTTP |
| Message Service | 5002 | HTTP |
| WebSocket | 5005 | WS |
| TURN | 3478, 5349 | UDP/TCP |
| PostgreSQL | 5432 | TCP |
| Redis | 6379 | TCP |
| RabbitMQ | 5672 | TCP |
| MinIO | 9000 | HTTP |

### How do I monitor LocalTelegram?

- **Prometheus**: Metrics collection
- **Grafana**: Visualization dashboards
- **Jaeger**: Distributed tracing
- **Loki**: Log aggregation

Access Grafana at `https://grafana.your-domain.com`

---

## Troubleshooting

### I can't log in

1. Check your email and password
2. If 2FA is enabled, verify the code
3. Check if your account is active
4. Try "Forgot Password"
5. Contact administrator

### Messages aren't sending

1. Check your internet connection
2. Verify you're not muted in the conversation
3. Check if recipient has blocked you
4. Restart the app
5. Check server status

### Notifications aren't working

**Desktop:**
1. Check system notification settings
2. Verify app has notification permission

**Android:**
1. Disable battery optimization for the app
2. Check notification permissions
3. Verify background data is allowed

### Video calls aren't working

1. Check camera/microphone permissions
2. Verify internet speed (min 1 Mbps)
3. Try audio-only mode
4. Check if TURN server is accessible

### The app is slow

1. Check server resource usage
2. Clear app cache
3. Check network latency
4. Contact administrator if issue persists

---

## Support

### Where can I get help?

1. **Documentation**: Check the /docs folder
2. **FAQ**: This document
3. **Administrator**: Contact your system administrator
4. **Issues**: Report bugs on GitHub (if applicable)

### How do I report a bug?

1. Note the steps to reproduce
2. Include screenshots if possible
3. Note your device and OS version
4. Report to your administrator or GitHub issues

### How do I request a feature?

Contact your administrator with:
- Feature description
- Use case
- Priority/urgency

---

## Glossary

| Term | Definition |
|------|------------|
| 2FA | Two-Factor Authentication |
| API | Application Programming Interface |
| E2EE | End-to-End Encryption |
| GDPR | General Data Protection Regulation |
| JWT | JSON Web Token |
| LDAP | Lightweight Directory Access Protocol |
| SFU | Selective Forwarding Unit |
| TURN | Traversal Using Relays around NAT |
| WebRTC | Web Real-Time Communication |

---

*Document created: March 2026*  
*Last updated: March 2026*
