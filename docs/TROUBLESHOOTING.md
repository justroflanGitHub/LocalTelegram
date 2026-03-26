# LocalTelegram Troubleshooting Guide

> Version: 1.0 | Date: March 2026

---

## Table of Contents

1. [Common Issues](#common-issues)
2. [Service-Specific Issues](#service-specific-issues)
3. [Database Issues](#database-issues)
4. [Network Issues](#network-issues)
5. [Performance Issues](#performance-issues)
6. [Security Issues](#security-issues)
7. [Client Issues](#client-issues)
8. [Monitoring & Debugging](#monitoring--debugging)
9. [Recovery Procedures](#recovery-procedures)

---

## Common Issues

### Service Won't Start

**Symptoms:**
- Container exits immediately
- Service shows as unhealthy
- Connection refused errors

**Diagnosis:**
```bash
# Check container logs
docker logs <container_name>

# Check container status
docker ps -a | grep <container_name>

# Check resource usage
docker stats <container_name>
```

**Common Causes & Solutions:**

1. **Missing environment variables**
   ```bash
   # Check .env file exists
   cat .env
   
   # Verify required variables
   docker-compose config
   ```

2. **Port conflicts**
   ```bash
   # Check what's using the port
   netstat -tlnp | grep <port>
   
   # Kill conflicting process or change port in docker-compose.yml
   ```

3. **Dependency not ready**
   ```yaml
   # Add healthcheck dependencies in docker-compose.yml
   depends_on:
     postgres:
       condition: service_healthy
   ```

### Database Connection Errors

**Symptoms:**
- "Connection refused" errors
- "Too many connections" errors
- Timeout errors

**Solutions:**

```bash
# Check PostgreSQL is running
docker exec -it postgres pg_isready

# Check connection count
docker exec -it postgres psql -U postgres -c "SELECT count(*) FROM pg_stat_activity;"

# Check max connections
docker exec -it postgres psql -U postgres -c "SHOW max_connections;"

# Kill idle connections
docker exec -it postgres psql -U postgres -c "
SELECT pg_terminate_backend(pid) 
FROM pg_stat_activity 
WHERE datname = 'localtelegram' 
AND pid <> pg_backend_pid() 
AND state = 'idle';"
```

### Redis Connection Issues

**Symptoms:**
- "NOAUTH Authentication required"
- "LOADING Redis is loading"
- Connection timeouts

**Solutions:**

```bash
# Check Redis status
docker exec -it redis redis-cli ping

# Check Redis memory
docker exec -it redis redis-cli info memory

# Clear Redis cache (use with caution!)
docker exec -it redis redis-cli FLUSHALL

# Check Redis configuration
docker exec -it redis redis-cli CONFIG GET "*"
```

---

## Service-Specific Issues

### Auth Service

#### Login Fails

**Symptoms:**
- "Invalid credentials" error
- Token generation fails
- Session not created

**Debug Steps:**
```bash
# Check auth service logs
docker logs auth-service --tail 100

# Verify user exists in database
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT id, email, is_active, is_verified 
FROM users 
WHERE email = 'user@example.com';"

# Check Redis for sessions
docker exec -it redis redis-cli KEYS "session:*"

# Test token validation
curl -X POST https://api.localtelegram.com/api/auth/validate \
  -H "Authorization: Bearer <token>"
```

#### 2FA Issues

**Symptoms:**
- TOTP codes not accepted
- Backup codes don't work
- 2FA can't be disabled

**Solutions:**
```bash
# Check 2FA status for user
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT u.email, t.is_enabled, t.method 
FROM users u 
LEFT JOIN two_factor_secrets t ON u.id = t.user_id 
WHERE u.email = 'user@example.com';"

# Disable 2FA for user (admin action)
docker exec -it postgres psql -U postgres -d localtelegram -c "
UPDATE two_factor_secrets 
SET is_enabled = false 
WHERE user_id = (SELECT id FROM users WHERE email = 'user@example.com');"
```

### Message Service

#### Messages Not Delivering

**Symptoms:**
- Messages stuck in "sending" state
- Recipients not receiving messages
- WebSocket not receiving updates

**Debug Steps:**
```bash
# Check message service logs
docker logs message-service --tail 100

# Check RabbitMQ queues
docker exec -it rabbitmq rabbitmqctl list_queues name messages consumers

# Check message status in database
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT id, status, created_at 
FROM messages 
WHERE conversation_id = '<conversation_id>' 
ORDER BY created_at DESC 
LIMIT 10;"

# Check WebSocket connections
docker logs push-service --tail 100 | grep -i "websocket"
```

#### Message History Not Loading

**Symptoms:**
- Empty conversation history
- Pagination not working
- Timeouts on history requests

**Solutions:**
```bash
# Check index usage
docker exec -it postgres psql -U postgres -d localtelegram -c "
EXPLAIN ANALYZE 
SELECT * FROM messages 
WHERE conversation_id = '<id>' 
ORDER BY created_at DESC 
LIMIT 50;"

# Reindex if needed
docker exec -it postgres psql -U postgres -d localtelegram -c "
REINDEX TABLE messages;"
```

### File Service

#### Upload Fails

**Symptoms:**
- "Upload failed" error
- Chunked upload incomplete
- File size limit errors

**Debug Steps:**
```bash
# Check MinIO status
docker exec -it minio mc admin info local

# Check bucket exists
docker exec -it minio mc ls local/

# Check file service logs
docker logs file-service --tail 100

# Check disk space
df -h

# Check upload size limits
grep -r "MaxRequestBodySize" src/server/FileService/
```

#### Download Issues

**Symptoms:**
- File not found errors
- Corrupted downloads
- Slow download speeds

**Solutions:**
```bash
# Verify file exists in MinIO
docker exec -it minio mc ls local/files/

# Check file integrity
docker exec -it minio mc stat local/files/<file_id>

# Check presigned URL generation
curl -I "<presigned_url>"
```

### Push Service

#### Notifications Not Received

**Symptoms:**
- No push notifications
- Delayed notifications
- Notifications on some devices only

**Debug Steps:**
```bash
# Check SignalR connections
docker logs push-service --tail 100 | grep -i "connected"

# Check notification queue
docker exec -it rabbitmq rabbitmqctl list_queues name messages

# Verify user subscription
docker exec -it mongo mongosh localtelegram --eval "
db.push_subscriptions.find({user_id: '<user_id>'})"

# Test WebSocket connection
wscat -c wss://api.localtelegram.com/notificationHub
```

### Conference Service

#### Video Call Issues

**Symptoms:**
- Can't join calls
- Poor video quality
- Echo/audio issues

**Debug Steps:**
```bash
# Check LiveKit status
docker logs livekit --tail 100

# Check room creation
curl -H "Authorization: Bearer <token>" \
  https://livekit.localtelegram.com/twirp/livekit.RoomService/ListRooms

# Check TURN server
turnutils_uclient -v -T -u username -w password turn.localtelegram.com

# Check WebRTC logs
docker logs conference-service --tail 100 | grep -i "webrtc"
```

---

## Database Issues

### PostgreSQL Performance

**Slow Queries:**
```sql
-- Find slow queries
SELECT query, calls, total_time, mean_time
FROM pg_stat_statements
ORDER BY mean_time DESC
LIMIT 10;

-- Check for table bloat
SELECT schemaname, tablename, 
       pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Check for missing indexes
SELECT schemaname, tablename, attname, n_distinct, correlation
FROM pg_stats
WHERE schemaname = 'public'
AND n_distinct > 100
AND correlation < 0.5;
```

**Lock Issues:**
```sql
-- Check for locks
SELECT pid, state, application_name, query_start, query
FROM pg_stat_activity
WHERE state != 'idle'
ORDER BY query_start;

-- Kill blocking query
SELECT pg_terminate_backend(<pid>);
```

### MongoDB Issues

```bash
# Check MongoDB status
docker exec -it mongo mongosh --eval "db.adminCommand('replSetGetStatus')"

# Check collection sizes
docker exec -it mongo mongosh localtelegram --eval "
db.getCollectionNames().forEach(function(c) {
    print(c + ': ' + db[c].stats().size + ' bytes');
});"

# Repair database
docker exec -it mongo mongosh localtelegram --eval "db.repairDatabase()"
```

---

## Network Issues

### DNS Resolution

```bash
# Check DNS resolution
nslookup api.localtelegram.com
dig api.localtelegram.com

# Check /etc/hosts
cat /etc/hosts | grep localtelegram

# Test internal DNS
docker exec -it auth-service nslookup postgres
```

### SSL/TLS Issues

```bash
# Check certificate
openssl s_client -connect api.localtelegram.com:443 -showcerts

# Verify certificate chain
openssl verify -CAfile /path/to/ca.crt /path/to/cert.crt

# Check certificate expiration
openssl x509 -enddate -noout -in /path/to/cert.crt
```

### Firewall Issues

```bash
# Check open ports
netstat -tlnp

# Check firewall rules (Ubuntu)
sudo ufw status

# Check iptables
sudo iptables -L -n

# Test port connectivity
telnet api.localtelegram.com 443
```

---

## Performance Issues

### High CPU Usage

**Diagnosis:**
```bash
# Check container CPU
docker stats

# Check process CPU
top -p $(pgrep -d',' -f "LocalTelegram")

# Profile .NET application
dotnet-trace collect -p <pid> --duration 00:00:30
```

**Solutions:**
1. Scale horizontally
2. Optimize queries
3. Add caching
4. Review background jobs

### High Memory Usage

**Diagnosis:**
```bash
# Check memory breakdown
docker exec -it <service> cat /proc/meminfo

# Check .NET memory
dotnet-dump collect -p <pid>
dotnet-gcdump analyze <dump_file>

# Check for memory leaks
dotnet-counters monitor -p <pid> --counters gc-heap
```

### Slow Response Times

**Diagnosis:**
```bash
# Check response times in logs
docker logs <service> | grep -i "duration"

# Check Prometheus metrics
curl http://prometheus:9090/api/v1/query?query=http_request_duration_seconds

# Analyze with Jaeger
# Open http://jaeger:16686
```

---

## Security Issues

### Suspicious Activity

**Check for:**
```bash
# Failed login attempts
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT ip_address, COUNT(*) as attempts
FROM audit_logs
WHERE action = 'login_failed'
AND created_at > NOW() - INTERVAL '1 hour'
GROUP BY ip_address
ORDER BY attempts DESC;"

# Unusual API patterns
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT user_id, action, COUNT(*) as count
FROM audit_logs
WHERE created_at > NOW() - INTERVAL '1 hour'
GROUP BY user_id, action
HAVING COUNT(*) > 100
ORDER BY count DESC;"

# Check rate limit hits
docker exec -it redis redis-cli KEYS "ratelimit:*" | wc -l
```

### Compromised Account

**Response:**
```bash
# 1. Force logout all sessions
docker exec -it redis redis-cli DEL "user:sessions:<user_id>"

# 2. Disable account
docker exec -it postgres psql -U postgres -d localtelegram -c "
UPDATE users SET is_active = false WHERE id = '<user_id>';"

# 3. Revoke all tokens
docker exec -it postgres psql -U postgres -d localtelegram -c "
UPDATE sessions SET is_active = false WHERE user_id = '<user_id>';"

# 4. Audit user activity
docker exec -it postgres psql -U postgres -d localtelegram -c "
SELECT * FROM audit_logs WHERE actor_id = '<user_id>' ORDER BY created_at DESC LIMIT 100;"
```

---

## Client Issues

### Windows Desktop Client

**Won't Connect:**
```
1. Check server URL in settings
2. Verify SSL certificate is trusted
3. Check firewall allows outbound 443
4. Clear client cache: %APPDATA%\LocalTelegram
```

**Crashes on Startup:**
```
1. Check Visual C++ Redistributable installed
2. Verify Qt dependencies
3. Check logs: %APPDATA%\LocalTelegram\logs
4. Try portable version
```

### Android Client

**Push Notifications Not Working:**
```
1. Check battery optimization disabled
2. Verify Google Play Services
3. Check notification permissions
4. Verify FCM configuration
```

**Can't Connect:**
```
1. Check server URL
2. Verify network connectivity
3. Check SSL certificate
4. Clear app data
```

---

## Monitoring & Debugging

### Log Analysis

```bash
# View all service logs
docker-compose logs -f --tail=100

# Filter by service
docker logs <service> --since 1h | grep -i error

# Export logs
docker logs <service> > service.log 2>&1

# Search in Loki
logcli query '{service="auth-service"} |= "error"' --limit=100
```

### Health Checks

```bash
# Check all services health
docker-compose ps

# Manual health check
curl http://localhost:5001/health
curl http://localhost:5002/health
curl http://localhost:5003/health

# Detailed health check
curl http://localhost:5001/health/ready
curl http://localhost:5001/health/live
```

### Tracing Requests

```bash
# Get trace ID from response header
curl -v https://api.localtelegram.com/api/auth/login

# Look up in Jaeger
# Open http://jaeger:16686/trace/<trace_id>
```

### Metrics Analysis

```bash
# Query Prometheus
curl 'http://prometheus:9090/api/v1/query?query=up'

# Check error rate
curl 'http://prometheus:9090/api/v1/query?query=rate(http_requests_total{status=~"5.."}[5m])'

# Check latency
curl 'http://prometheus:9090/api/v1/query?query=histogram_quantile(0.95,rate(http_request_duration_seconds_bucket[5m]))'
```

---

## Recovery Procedures

### Service Recovery

```bash
# Restart single service
docker-compose restart <service>

# Restart all services
docker-compose restart

# Full rebuild
docker-compose down
docker-compose up -d --build
```

### Database Recovery

```bash
# Restore from backup
docker exec -i postgres psql -U postgres localtelegram < backup.sql

# Point-in-time recovery
# 1. Stop PostgreSQL
docker stop postgres

# 2. Restore base backup
tar -xf base_backup.tar -C /var/lib/postgresql/data

# 3. Configure recovery
echo "restore_command = 'cp /backup/wal/%f %p'" >> postgresql.conf
echo "recovery_target_time = '2026-03-26 12:00:00'" >> postgresql.conf

# 4. Start PostgreSQL
docker start postgres
```

### Redis Recovery

```bash
# Restore from RDB
docker cp dump.rdb redis:/data/
docker restart redis

# Restore from AOF
docker cp appendonly.aof redis:/data/
docker restart redis
```

### Complete System Recovery

```bash
# 1. Stop all services
docker-compose down

# 2. Restore volumes
./scripts/restore-backup.sh /backup/latest

# 3. Start services
docker-compose up -d

# 4. Verify services
docker-compose ps
curl http://localhost:5001/health

# 5. Check logs
docker-compose logs -f
```

---

## Error Reference

### HTTP Status Codes

| Code | Meaning | Common Causes |
|------|---------|---------------|
| 400 | Bad Request | Invalid request body, missing parameters |
| 401 | Unauthorized | Invalid/expired token, session expired |
| 403 | Forbidden | Insufficient permissions, blocked user |
| 404 | Not Found | Resource doesn't exist, deleted |
| 409 | Conflict | Duplicate resource, version conflict |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Error | Server bug, unhandled exception |
| 502 | Bad Gateway | Upstream service unavailable |
| 503 | Service Unavailable | Service overloaded, maintenance |
| 504 | Gateway Timeout | Upstream service timeout |

### Error Codes

| Code | Description | Solution |
|------|-------------|----------|
| AUTH001 | Invalid credentials | Check email/password |
| AUTH002 | Account locked | Wait or contact admin |
| AUTH003 | 2FA required | Provide 2FA code |
| AUTH004 | Session expired | Re-login required |
| MSG001 | Conversation not found | Verify conversation ID |
| MSG002 | User not in conversation | Join conversation first |
| MSG003 | Message too long | Split message |
| FILE001 | File too large | Reduce file size |
| FILE002 | Invalid file type | Check allowed types |
| FILE003 | Upload failed | Retry or check storage |
| RATE001 | Rate limit exceeded | Wait and retry |

---

## Support Contacts

For issues not covered in this guide:

1. **Check GitHub Issues**: https://github.com/your-org/localtelegram/issues
2. **Review Documentation**: /docs folder
3. **Contact Admin**: admin@yourcompany.com

---

*Document created: March 2026*  
*Last updated: March 2026*
