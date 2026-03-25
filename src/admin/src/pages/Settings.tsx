import React, { useEffect, useState } from 'react'
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  Button,
  Grid,
  Switch,
  FormControlLabel,
  Divider,
  Alert,
  Snackbar,
} from '@mui/material'
import { Save as SaveIcon } from '@mui/icons-material'
import api from '../api/client'

interface SystemSettings {
  siteName: string
  maxFileSize: number
  maxMessageLength: number
  allowRegistration: boolean
  requireEmailVerification: boolean
  enablePushNotifications: boolean
  enableVideoCalls: boolean
  enableGroupCalls: boolean
  maxGroupSize: number
  ldapEnabled: boolean
  ldapServer?: string
  ldapBaseDn?: string
}

const Settings: React.FC = () => {
  const [settings, setSettings] = useState<SystemSettings>({
    siteName: 'LocalTelegram',
    maxFileSize: 2147483648, // 2GB
    maxMessageLength: 4096,
    allowRegistration: true,
    requireEmailVerification: false,
    enablePushNotifications: true,
    enableVideoCalls: true,
    enableGroupCalls: true,
    maxGroupSize: 200,
    ldapEnabled: false,
  })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [snackbar, setSnackbar] = useState({ open: false, message: '', severity: 'success' as 'success' | 'error' })

  useEffect(() => {
    const fetchSettings = async () => {
      try {
        const data = await api.getSettings()
        setSettings({ ...settings, ...data })
      } catch (error) {
        console.error('Failed to fetch settings:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchSettings()
  }, [])

  const handleSave = async () => {
    setSaving(true)
    try {
      await api.updateSettings(settings as unknown as Record<string, unknown>)
      setSnackbar({ open: true, message: 'Settings saved successfully', severity: 'success' })
    } catch (error) {
      console.error('Failed to save settings:', error)
      setSnackbar({ open: true, message: 'Failed to save settings', severity: 'error' })
    } finally {
      setSaving(false)
    }
  }

  const handleChange = (key: keyof SystemSettings, value: unknown) => {
    setSettings({ ...settings, [key]: value })
  }

  if (loading) {
    return <Typography>Loading...</Typography>
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Settings
      </Typography>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            General Settings
          </Typography>
          <Grid container spacing={3}>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Site Name"
                value={settings.siteName}
                onChange={(e) => handleChange('siteName', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Max File Size (bytes)"
                type="number"
                value={settings.maxFileSize}
                onChange={(e) => handleChange('maxFileSize', parseInt(e.target.value))}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Max Message Length"
                type="number"
                value={settings.maxMessageLength}
                onChange={(e) => handleChange('maxMessageLength', parseInt(e.target.value))}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Max Group Size"
                type="number"
                value={settings.maxGroupSize}
                onChange={(e) => handleChange('maxGroupSize', parseInt(e.target.value))}
              />
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Features
          </Typography>
          <Grid container spacing={2}>
            <Grid item xs={12} sm={6}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.allowRegistration}
                    onChange={(e) => handleChange('allowRegistration', e.target.checked)}
                  />
                }
                label="Allow User Registration"
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.requireEmailVerification}
                    onChange={(e) => handleChange('requireEmailVerification', e.target.checked)}
                  />
                }
                label="Require Email Verification"
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.enablePushNotifications}
                    onChange={(e) => handleChange('enablePushNotifications', e.target.checked)}
                  />
                }
                label="Enable Push Notifications"
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.enableVideoCalls}
                    onChange={(e) => handleChange('enableVideoCalls', e.target.checked)}
                  />
                }
                label="Enable Video Calls"
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.enableGroupCalls}
                    onChange={(e) => handleChange('enableGroupCalls', e.target.checked)}
                  />
                }
                label="Enable Group Calls"
              />
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            LDAP Settings
          </Typography>
          <Grid container spacing={2}>
            <Grid item xs={12}>
              <FormControlLabel
                control={
                  <Switch
                    checked={settings.ldapEnabled}
                    onChange={(e) => handleChange('ldapEnabled', e.target.checked)}
                  />
                }
                label="Enable LDAP Authentication"
              />
            </Grid>
            {settings.ldapEnabled && (
              <>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth
                    label="LDAP Server"
                    value={settings.ldapServer || ''}
                    onChange={(e) => handleChange('ldapServer', e.target.value)}
                    placeholder="ldap://server:389"
                  />
                </Grid>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth
                    label="Base DN"
                    value={settings.ldapBaseDn || ''}
                    onChange={(e) => handleChange('ldapBaseDn', e.target.value)}
                    placeholder="dc=example,dc=com"
                  />
                </Grid>
              </>
            )}
          </Grid>
        </CardContent>
      </Card>

      <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button
          variant="contained"
          color="primary"
          startIcon={<SaveIcon />}
          onClick={handleSave}
          disabled={saving}
        >
          {saving ? 'Saving...' : 'Save Settings'}
        </Button>
      </Box>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={5000}
        onClose={() => setSnackbar({ ...snackbar, open: false })}
      >
        <Alert severity={snackbar.severity} onClose={() => setSnackbar({ ...snackbar, open: false })}>
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  )
}

export default Settings
