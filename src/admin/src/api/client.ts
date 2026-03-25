import axios, { AxiosInstance, AxiosRequestConfig } from 'axios'
import { User, Group, AuditLogEntry, SystemStats, PaginatedResponse, LoginRequest, LoginResponse } from '../types'

class ApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      baseURL: '/api/admin',
      timeout: 10000,
      headers: {
        'Content-Type': 'application/json',
      },
    })

    // Add auth token to requests
    this.client.interceptors.request.use((config) => {
      const token = localStorage.getItem('admin_token')
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
      }
      return config
    })

    // Handle 401 responses
    this.client.interceptors.response.use(
      (response) => response,
      (error) => {
        if (error.response?.status === 401) {
          localStorage.removeItem('admin_token')
          window.location.href = '/login'
        }
        return Promise.reject(error)
      }
    )
  }

  // Auth
  async login(data: LoginRequest): Promise<LoginResponse> {
    const response = await this.client.post<LoginResponse>('/auth/login', data)
    return response.data
  }

  async logout(): Promise<void> {
    await this.client.post('/auth/logout')
  }

  async getCurrentUser(): Promise<User> {
    const response = await this.client.get<User>('/auth/me')
    return response.data
  }

  // Dashboard
  async getStats(): Promise<SystemStats> {
    const response = await this.client.get<SystemStats>('/dashboard/stats')
    return response.data
  }

  // Users
  async getUsers(params?: {
    page?: number
    pageSize?: number
    search?: string
    status?: string
  }): Promise<PaginatedResponse<User>> {
    const response = await this.client.get<PaginatedResponse<User>>('/users', { params })
    return response.data
  }

  async getUser(id: string): Promise<User> {
    const response = await this.client.get<User>(`/users/${id}`)
    return response.data
  }

  async updateUser(id: string, data: Partial<User>): Promise<User> {
    const response = await this.client.put<User>(`/users/${id}`, data)
    return response.data
  }

  async deleteUser(id: string): Promise<void> {
    await this.client.delete(`/users/${id}`)
  }

  async activateUser(id: string): Promise<User> {
    const response = await this.client.post<User>(`/users/${id}/activate`)
    return response.data
  }

  async deactivateUser(id: string): Promise<User> {
    const response = await this.client.post<User>(`/users/${id}/deactivate`)
    return response.data
  }

  // Groups
  async getGroups(params?: {
    page?: number
    pageSize?: number
    search?: string
  }): Promise<PaginatedResponse<Group>> {
    const response = await this.client.get<PaginatedResponse<Group>>('/groups', { params })
    return response.data
  }

  async getGroup(id: string): Promise<Group> {
    const response = await this.client.get<Group>(`/groups/${id}`)
    return response.data
  }

  async updateGroup(id: string, data: Partial<Group>): Promise<Group> {
    const response = await this.client.put<Group>(`/groups/${id}`, data)
    return response.data
  }

  async deleteGroup(id: string): Promise<void> {
    await this.client.delete(`/groups/${id}`)
  }

  // Audit Log
  async getAuditLog(params?: {
    page?: number
    pageSize?: number
    userId?: string
    action?: string
    startDate?: string
    endDate?: string
  }): Promise<PaginatedResponse<AuditLogEntry>> {
    const response = await this.client.get<PaginatedResponse<AuditLogEntry>>('/audit-log', { params })
    return response.data
  }

  // Settings
  async getSettings(): Promise<Record<string, unknown>> {
    const response = await this.client.get<Record<string, unknown>>('/settings')
    return response.data
  }

  async updateSettings(data: Record<string, unknown>): Promise<Record<string, unknown>> {
    const response = await this.client.put<Record<string, unknown>>('/settings', data)
    return response.data
  }
}

export const api = new ApiClient()
export default api
