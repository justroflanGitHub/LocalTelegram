import { useState, useEffect, useCallback, createContext, useContext, ReactNode } from 'react'
import { User } from '../types'
import api from '../api/client'

interface AuthContextType {
  isAuthenticated: boolean
  isLoading: boolean
  user: User | null
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextType | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const checkAuth = useCallback(async () => {
    const token = localStorage.getItem('admin_token')
    if (!token) {
      setIsLoading(false)
      return
    }

    try {
      const userData = await api.getCurrentUser()
      setUser(userData)
    } catch {
      localStorage.removeItem('admin_token')
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    checkAuth()
  }, [checkAuth])

  const login = async (username: string, password: string) => {
    const response = await api.login({ username, password })
    localStorage.setItem('admin_token', response.token)
    setUser(response.user)
  }

  const logout = async () => {
    try {
      await api.logout()
    } finally {
      localStorage.removeItem('admin_token')
      setUser(null)
    }
  }

  return (
    <AuthContext.Provider value={{ isAuthenticated: !!user, isLoading, user, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
