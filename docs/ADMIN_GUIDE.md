# LocalTelegram Administrator Guide

> Version: 1.0 | Date: March 2026

---

## Table of Contents

1. [Administration Overview](#administration-overview)
2. [Admin Panel](#admin-panel)
3. [User Management](#user-management)
4. [Group Management](#group-management)
5. [Moderation](#moderation)
6. [System Configuration](#system-configuration)
7. [Monitoring](#monitoring)
8. [Backup & Recovery](#backup--recovery)
9. [Security Administration](#security-administration)
10. [Maintenance](#maintenance)

---

## Administration Overview

### Administrator Roles

| Role | Access Level |
|------|-------------|
| Super Admin | Full system access, can manage other admins |
| Admin | User management, moderation, reports |
| Moderator | View reports, issue warnings, mute users |
| Support | Read-only access, view user info |

### Admin Panel Access

1. **Web Admin Panel**
   - URL: `https://admin.localtelegram.com`
   - Login with admin credentials
   - 2FA required for admin accounts

2. **API Access**
   - Base URL: `https://api.localtelegram.com/api/admin`
   - Authentication: Bearer token
   - Requires admin role

### Dashboard Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  LocalTelegram Admin Panel                                    [Admin User ▼] │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌──────────────────────────────────────────────────────┐  │
│  │             │  │  Dashboard                                            │  │
│  │ Dashboard   │  │  ┌────────────┐ ┌────────────┐ ┌────────────┐        │  │
│  │             │  │  │ Users      │ │ Messages   │ │ Active     │        │  │
│  │ Users       │  │  │ 1,234      │ │ 45.2K      │ │ 89         │        │  │
│  │             │  │  │ (+12 today)│ │ (+1.2K)    │ │ online     │        │  │
│  │ Groups      │  │  └────────────┘ └────────────┘ └────────────┘        │  │
│  │             │  │                                                        │  │
│  │ Reports     │  │  ┌─────────────────────────────────────────────────┐ │  │
│  │             │  │  │  Activity Chart                                 │ │  │
│  │ Moderation  │  │  │  [Chart showing message/activity trends]        │ │  │
│  │             │  │  └─────────────────────────────────────────────────┘ │  │
│  │ Audit Log   │  │                                                        │  │
│  │             │  │  ┌──────────────────┐ ┌──────────────────┐            │  │
│  │ Settings    │  │  │ Pending Reports  │ │ System Health    │            │  │
│  │             │  │  │ 5 require review │ │ All systems OK   │            │  │
│  │ Analytics   │  │  │ [View →]         │ │ [Details →]      │            │  │
│  │             │  │  └──────────────────┘ └──────────────────┘            │  │
│  └─────────────┘  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Admin Panel

### Accessing Admin Panel

1. Navigate to `https://admin.localtelegram.com`
2. Enter admin credentials
3. Complete 2FA verification
4. Dashboard loads with system overview

### Navigation

| Section | Description |
|---------|-------------|
| Dashboard | System overview, statistics, health |
| Users | User management, search, actions |
| Groups | Group management, settings |
| Reports | User reports, content flags |
| Moderation | Bans, mutes, warnings |
| Audit Log | System activity log |
| Settings | System configuration |
| Analytics | Usage statistics, trends |

---

## User Management

### Viewing Users

**User List:**
1. Go to Users section
2. View paginated list of all users
3. Filter by:
   - Status (active, inactive, banned)
   - Registration date
   - Last active
   - Role

**User Search:**
1. Enter search term (email, username, name)
2. Results update in real-time
3. Click user to view details

### User Details

**Profile Information:**
- User ID
- Email
- Username
- Name
- Phone (if set)
- Registration date
- Last active
- Status

**Statistics:**
- Messages sent
- Groups joined
- Files uploaded
- Reports received
- Warnings/Bans

**Sessions:**
- Active sessions
- Device information
- IP addresses
- Last activity

### User Actions

**Edit User:**
1. Open user details
2. Click "Edit"
3. Modify:
   - Name
   - Username
   - Email verification status
   - Role
4. Save changes

**Reset Password:**
1. Open user details
2. Click "Reset Password"
3. Choose:
   - Send reset email
   - Set temporary password
4. User must change on next login

**Disable 2FA:**
1. Open user details
2. Click "Disable 2FA"
3. Confirm action
4. Audit log entry created

**Verify Email:**
1. Open user details
2. Click "Verify Email"
3. Email marked as verified

### Bulk Actions

1. Select multiple users from list
2. Choose action:
   - Export selected
   - Send notification
   - Bulk verify email
   - Bulk disable
3. Confirm action

### Creating Users

**Manual Creation:**
1. Click "Create User"
2. Fill required fields:
   - Email
   - Username
   - Password (or generate)
   - Name
3. Set options:
   - Email verified
   - Require password change
   - Send welcome email
4. Create user

**Via Invite:**
1. Go to Settings → Invites
2. Create invite code or link
3. Share with intended user
4. Track invite status

---

## Group Management

### Viewing Groups

1. Go to Groups section
2. View all groups
3. Filter by:
   - Member count
   - Creation date
   - Activity
   - Public/Private

### Group Details

- Group ID
- Name
- Description
- Owner
- Member count
- Creation date
- Invite link
- Settings

### Group Actions

**Edit Group:**
- Change name/description
- Update avatar
- Modify settings

**Transfer Ownership:**
1. Open group details
2. Click "Transfer Ownership"
3. Select new owner from members
4. Confirm transfer

**Delete Group:**
1. Open group details
2. Click "Delete Group"
3. Enter confirmation
4. Group and messages deleted

**Revoke Invite Link:**
1. Open group details
2. Click "Revoke Link"
3. New link generated automatically

### Group Moderation

**View Reports:**
- See reports about group content
- Review reported messages
- Take action

**Restrict Group:**
- Disable new members
- Disable file sharing
- Read-only mode

---

## Moderation

### Reports Queue

**Viewing Reports:**
1. Go to Reports section
2. See pending reports
3. Sort by:
   - Priority
   - Date
   - Reporter
   - Type

**Report Details:**
- Report ID
- Reporter information
- Reported user/content
- Reason
- Description
- Timestamp
- Related reports

### Handling Reports

**Review Process:**
1. Open report
2. Review reported content
3. Check user history
4. Take action or dismiss

**Actions:**
- **Dismiss**: No violation found
- **Warn**: Send warning to user
- **Mute**: Temporary message restriction
- **Ban**: Account suspension
- **Delete Content**: Remove offending content

### Warnings

**Issue Warning:**
1. Open user or report
2. Click "Issue Warning"
3. Select reason:
   - Spam
   - Harassment
   - Inappropriate content
   - Other
4. Add custom message
5. Send warning

**Warning Limits:**
- Configurable max warnings
- Auto-action after limit
- Warning expiration (optional)

### Mutes

**Mute User:**
1. Open user details
2. Click "Mute"
3. Set duration:
   - 1 hour
   - 24 hours
   - 7 days
   - Custom
   - Permanent
4. Add reason
5. Confirm mute

**Mute Effects:**
- User cannot send messages
- Can still read messages
- Receives mute notification

### Bans

**Ban User:**
1. Open user details
2. Click "Ban"
3. Set duration:
   - Temporary (with end date)
   - Permanent
4. Add reason
5. Optional: Revoke all sessions
6. Confirm ban

**Ban Effects:**
- Cannot login
- All sessions terminated
- Cannot register with same email

**Unban User:**
1. Go to Moderation → Bans
2. Find user
3. Click "Unban"
4. Add note
5. Confirm

### Sanctions History

1. Go to Moderation → History
2. View all sanctions
3. Filter by:
   - User
   - Type (warning/mute/ban)
   - Date range
   - Issuing admin

---

## System Configuration

### General Settings

**Instance Information:**
- Instance name
- Instance description
- Admin contact email
- Terms of service URL
- Privacy policy URL

**Registration:**
- Enable/disable registration
- Require invite code
- Allowed email domains
- Email verification required

**Limits:**
- Max users
- Max groups per user
- Max group members
- Max file size

### Authentication Settings

**Password Policy:**
- Minimum length
- Require uppercase
- Require numbers
- Require symbols
- Password expiration

**Session Settings:**
- Session duration
- Refresh token lifetime
- Max sessions per user
- Concurrent session handling

**2FA Settings:**
- Require 2FA for all users
- Require 2FA for admins
- Allowed methods (TOTP, SMS)
- Backup codes count

### Rate Limiting

**Global Limits:**
```yaml
rate_limits:
  messages:
    per_minute: 30
    per_hour: 500
    per_day: 5000
  
  file_uploads:
    per_hour: 50
    max_size_mb: 100
  
  api_requests:
    per_minute: 100
    per_hour: 2000
```

### Feature Flags

Toggle features on/off:
- [ ] Public group creation
- [ ] File uploads
- [ ] Voice messages
- [ ] Video calls
- [ ] Screen sharing
- [ ] Message editing
- [ ] Message deletion
- [ ] Message reactions

---

## Monitoring

### System Health

**Health Dashboard:**
- Service status (green/yellow/red)
- Response times
- Error rates
- Resource usage

**Health Checks:**
```
Service         Status    Response Time
────────────────────────────────────────
Auth Service    ● OK      45ms
Message Service ● OK      32ms
User Service    ● OK      28ms
File Service    ● OK      67ms
Push Service    ● OK      12ms
PostgreSQL      ● OK      5ms
Redis           ● OK      1ms
RabbitMQ        ● OK      3ms
MinIO           ● OK      15ms
```

### Metrics

**Key Metrics:**
- Active users (daily/weekly/monthly)
- Messages sent per day
- Files uploaded per day
- API response times
- Error rates
- Storage usage

**Grafana Dashboards:**
- System Overview: `https://grafana.localtelegram.com/d/system`
- API Metrics: `https://grafana.localtelegram.com/d/api`
- Database: `https://grafana.localtelegram.com/d/database`

### Alerts

**Alert Configuration:**
1. Go to Settings → Alerts
2. Configure alert rules
3. Set notification channels:
   - Email
   - Webhook
   - Slack/Teams

**Default Alerts:**
- Service down
- High error rate (>5%)
- High latency (>1s)
- Disk space low (<10%)
- Memory usage high (>90%)

### Logs

**Log Access:**
1. Go to Monitoring → Logs
2. Search and filter logs
3. Export logs

**Log Levels:**
- Error: Critical issues
- Warning: Potential issues
- Information: Normal events
- Debug: Detailed debugging

**Loki Queries:**
```
# Errors in last hour
{service="auth-service"} |= "error" | json

# Failed logins
{service="auth-service"} |= "login_failed"

# Slow requests
{service=~".+"} | json | duration > 1000
```

---

## Backup & Recovery

### Backup Configuration

**Automated Backups:**
```yaml
backup:
  postgresql:
    enabled: true
    schedule: "0 2 * * *"  # Daily at 2 AM
    retention_days: 30
    destination: /backup/postgres
    
  minio:
    enabled: true
    schedule: "0 3 * * *"  # Daily at 3 AM
    retention_days: 30
    destination: /backup/minio
```

### Manual Backup

**Database Backup:**
```bash
# Via script
./scripts/backup.sh --full

# Manual
docker exec postgres pg_dump -U postgres localtelegram > backup_$(date +%Y%m%d).sql
```

**File Backup:**
```bash
# MinIO backup
docker exec minio mc mirror local/ /backup/minio
```

### Recovery

**Database Restore:**
```bash
# Stop services
docker-compose stop auth-service message-service user-service

# Restore database
cat backup.sql | docker exec -i postgres psql -U postgres localtelegram

# Start services
docker-compose start auth-service message-service user-service
```

**Point-in-Time Recovery:**
```bash
# Enable WAL archiving first
# Restore base backup + WAL files
pg_basebackup -h postgres -D /restore/base
recovery_target_time = '2026-03-26 10:00:00'
```

---

## Security Administration

### Security Audit

**Audit Log:**
1. Go to Audit Log section
2. View all administrative actions
3. Filter by:
   - Action type
   - Actor
   - Date range
   - Resource

**Logged Actions:**
- User login/logout
- User creation/deletion
- Permission changes
- Configuration changes
- Moderation actions
- Data exports

### Security Reports

**Weekly Security Report:**
- Failed login attempts
- New registrations
- Account recoveries
- 2FA enrollments
- Suspicious activity

### Access Control

**Admin Users:**
1. Go to Settings → Admins
2. Manage admin accounts
3. Assign roles
4. Review access logs

**API Keys:**
1. Go to Settings → API Keys
2. Create service API keys
3. Set permissions
4. Monitor usage
5. Revoke if compromised

### Incident Response

**Security Incident:**
1. Identify the threat
2. Contain (disable accounts, revoke tokens)
3. Investigate (audit logs, traces)
4. Remediate (patch, config change)
5. Report (document incident)

**Compromised Account:**
```bash
# 1. Force logout
docker exec redis redis-cli DEL "user:sessions:<user_id>"

# 2. Disable account
curl -X PUT https://api.localtelegram.com/api/admin/users/<id>/disable

# 3. Reset password
curl -X POST https://api.localtelegram.com/api/admin/users/<id>/reset-password

# 4. Audit activity
curl https://api.localtelegram.com/api/admin/audit-log?user_id=<id>
```

---

## Maintenance

### Routine Tasks

**Daily:**
- Check system health dashboard
- Review pending reports
- Monitor error rates
- Check backup status

**Weekly:**
- Review security reports
- Check storage usage
- Review audit logs
- Update documentation

**Monthly:**
- Review and rotate API keys
- Check certificate expiration
- Review user access
- Performance review

### Updates

**Update Process:**
1. Check release notes
2. Backup system
3. Test in staging
4. Schedule maintenance window
5. Apply updates
6. Verify functionality
7. Monitor for issues

**Rollback:**
```bash
# Rollback to previous version
docker-compose down
docker tag localtelegram/auth-service:current localtelegram/auth-service:backup
docker tag localtelegram/auth-service:previous localtelegram/auth-service:current
docker-compose up -d
```

### Performance Tuning

**Database:**
```sql
-- Analyze query performance
SELECT query, calls, total_time, mean_time
FROM pg_stat_statements
ORDER BY mean_time DESC LIMIT 10;

-- Reindex tables
REINDEX TABLE messages;

-- Update statistics
ANALYZE;
```

**Redis:**
```bash
# Check memory usage
docker exec redis redis-cli info memory

# Clear cache if needed
docker exec redis redis-cli FLUSHALL
```

### Scaling

**Horizontal Scaling:**
```bash
# Scale API services
docker-compose up -d --scale auth-service=3
docker-compose up -d --scale message-service=3

# Update load balancer
# Configure nginx upstream
```

**Vertical Scaling:**
```yaml
# Increase resources in docker-compose.yml
services:
  postgres:
    deploy:
      resources:
        limits:
          memory: 4G
          cpus: '2'
```

---

## API Reference

### Admin API Endpoints

**Authentication:**
```
POST /api/admin/auth/login
POST /api/admin/auth/refresh
POST /api/admin/auth/logout
```

**Users:**
```
GET    /api/admin/users
GET    /api/admin/users/{id}
PUT    /api/admin/users/{id}
DELETE /api/admin/users/{id}
POST   /api/admin/users/{id}/reset-password
PUT    /api/admin/users/{id}/status
POST   /api/admin/users/{id}/disable-2fa
```

**Moderation:**
```
GET  /api/admin/reports
PUT  /api/admin/reports/{id}/resolve
POST /api/admin/users/{id}/warn
POST /api/admin/users/{id}/mute
POST /api/admin/users/{id}/ban
DELETE /api/admin/users/{id}/ban
```

**System:**
```
GET  /api/admin/health
GET  /api/admin/metrics
GET  /api/admin/audit-log
GET  /api/admin/settings
PUT  /api/admin/settings
```

---

## Support

### Admin Support

- **Documentation**: /docs folder
- **Issue Tracker**: Internal ticketing system
- **Emergency Contact**: On-call administrator

### Escalation

| Level | Issue Type | Response Time |
|-------|------------|---------------|
| L1 | User questions, minor issues | 4 hours |
| L2 | System issues, moderate bugs | 1 hour |
| L3 | Security incidents, outages | 15 minutes |

---

*Document created: March 2026*  
*Last updated: March 2026*
