export interface User {
  id: string
  username: string
  firstName: string
  lastName: string
  phone?: string
  email?: string
  avatarUrl?: string
  status: 'online' | 'offline' | 'away'
  isActive: boolean
  createdAt: string
  lastSeenAt?: string
}

export interface Group {
  id: string
  name: string
  description?: string
  avatarUrl?: string
  ownerId: string
  owner?: User
  memberCount: number
  isActive: boolean
  createdAt: string
}

export interface GroupMember {
  id: string
  groupId: string
  userId: string
  user?: User
  role: 'owner' | 'admin' | 'member'
  joinedAt: string
}

export interface AuditLogEntry {
  id: string
  action: string
  entityType: string
  entityId: string
  userId: string
  user?: User
  details: Record<string, unknown>
  ipAddress?: string
  userAgent?: string
  createdAt: string
}

export interface SystemStats {
  totalUsers: number
  activeUsers: number
  totalGroups: number
  totalMessages: number
  storageUsed: number
  storageLimit: number
}

export interface PaginatedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: User
}
