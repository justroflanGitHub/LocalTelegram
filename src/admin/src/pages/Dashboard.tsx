import React, { useEffect, useState } from 'react'
import {
  Box,
  Card,
  CardContent,
  Grid,
  Typography,
  LinearProgress,
} from '@mui/material'
import {
  People as PeopleIcon,
  Group as GroupIcon,
  Message as MessageIcon,
  Storage as StorageIcon,
} from '@mui/icons-material'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from 'recharts'
import { SystemStats } from '../types'
import api from '../api/client'

interface StatCardProps {
  title: string
  value: number | string
  icon: React.ReactNode
  color?: string
}

const StatCard: React.FC<StatCardProps> = ({ title, value, icon, color = '#0088cc' }) => (
  <Card>
    <CardContent>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Box>
          <Typography color="textSecondary" gutterBottom variant="body2">
            {title}
          </Typography>
          <Typography variant="h4" component="div">
            {value}
          </Typography>
        </Box>
        <Box
          sx={{
            backgroundColor: color,
            borderRadius: '50%',
            p: 1,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {icon}
        </Box>
      </Box>
    </CardContent>
  </Card>
)

// Mock data for charts
const activityData = [
  { name: 'Mon', messages: 4000, users: 240 },
  { name: 'Tue', messages: 3000, users: 139 },
  { name: 'Wed', messages: 2000, users: 980 },
  { name: 'Thu', messages: 2780, users: 390 },
  { name: 'Fri', messages: 1890, users: 480 },
  { name: 'Sat', messages: 2390, users: 380 },
  { name: 'Sun', messages: 3490, users: 430 },
]

const Dashboard: React.FC = () => {
  const [stats, setStats] = useState<SystemStats | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const fetchStats = async () => {
      try {
        const data = await api.getStats()
        setStats(data)
      } catch (error) {
        console.error('Failed to fetch stats:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchStats()
  }, [])

  if (loading) {
    return <LinearProgress />
  }

  const storagePercent = stats ? (stats.storageUsed / stats.storageLimit) * 100 : 0

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Dashboard
      </Typography>

      <Grid container spacing={3} sx={{ mb: 3 }}>
        <Grid item xs={12} sm={6} md={3}>
          <StatCard
            title="Total Users"
            value={stats?.totalUsers || 0}
            icon={<PeopleIcon sx={{ color: 'white' }} />}
            color="#0088cc"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <StatCard
            title="Active Users"
            value={stats?.activeUsers || 0}
            icon={<PeopleIcon sx={{ color: 'white' }} />}
            color="#4caf50"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <StatCard
            title="Total Groups"
            value={stats?.totalGroups || 0}
            icon={<GroupIcon sx={{ color: 'white' }} />}
            color="#ff9800"
          />
        </Grid>
        <Grid item xs={12} sm={6} md={3}>
          <StatCard
            title="Total Messages"
            value={stats?.totalMessages || 0}
            icon={<MessageIcon sx={{ color: 'white' }} />}
            color="#9c27b0"
          />
        </Grid>
      </Grid>

      <Grid container spacing={3}>
        <Grid item xs={12} md={8}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Activity Overview
              </Typography>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={activityData}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" />
                  <YAxis />
                  <Tooltip />
                  <Line
                    type="monotone"
                    dataKey="messages"
                    stroke="#0088cc"
                    strokeWidth={2}
                  />
                  <Line
                    type="monotone"
                    dataKey="users"
                    stroke="#4caf50"
                    strokeWidth={2}
                  />
                </LineChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        </Grid>

        <Grid item xs={12} md={4}>
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Storage Usage
              </Typography>
              <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
                <StorageIcon sx={{ mr: 1 }} />
                <Typography variant="body2">
                  {((stats?.storageUsed || 0) / 1024 / 1024 / 1024).toFixed(2)} GB /{' '}
                  {((stats?.storageLimit || 0) / 1024 / 1024 / 1024).toFixed(2)} GB
                </Typography>
              </Box>
              <LinearProgress
                variant="determinate"
                value={storagePercent}
                sx={{
                  height: 10,
                  borderRadius: 5,
                  backgroundColor: '#e0e0e0',
                  '& .MuiLinearProgress-bar': {
                    backgroundColor: storagePercent > 80 ? '#f44336' : '#0088cc',
                  },
                }}
              />
              <Typography variant="body2" color="textSecondary" sx={{ mt: 1 }}>
                {storagePercent.toFixed(1)}% used
              </Typography>
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Box>
  )
}

export default Dashboard
