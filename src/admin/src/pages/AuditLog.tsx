import React, { useEffect, useState, useCallback } from 'react'
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  InputAdornment,
  Chip,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  Grid,
} from '@mui/material'
import { Search as SearchIcon, FilterList as FilterIcon } from '@mui/icons-material'
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid'
import { AuditLogEntry, PaginatedResponse } from '../types'
import api from '../api/client'
import { format } from 'date-fns'

const actionColors: Record<string, 'default' | 'primary' | 'secondary' | 'success' | 'error' | 'info' | 'warning'> = {
  create: 'success',
  update: 'primary',
  delete: 'error',
  login: 'info',
  logout: 'default',
}

const AuditLog: React.FC = () => {
  const [entries, setEntries] = useState<AuditLogEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [search, setSearch] = useState('')
  const [actionFilter, setActionFilter] = useState('')
  const [startDate, setStartDate] = useState('')
  const [endDate, setEndDate] = useState('')

  const fetchAuditLog = useCallback(async () => {
    setLoading(true)
    try {
      const response = await api.getAuditLog({
        page: page + 1,
        pageSize,
        action: actionFilter || undefined,
        startDate: startDate || undefined,
        endDate: endDate || undefined,
      })
      setEntries(response.items)
      setTotal(response.total)
    } catch (error) {
      console.error('Failed to fetch audit log:', error)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, actionFilter, startDate, endDate])

  useEffect(() => {
    fetchAuditLog()
  }, [fetchAuditLog])

  const columns: GridColDef[] = [
    {
      field: 'createdAt',
      headerName: 'Timestamp',
      width: 180,
      valueFormatter: (params) =>
        format(new Date(params.value as string), 'MMM dd, yyyy HH:mm:ss'),
    },
    {
      field: 'action',
      headerName: 'Action',
      width: 120,
      renderCell: (params: GridRenderCellParams<AuditLogEntry>) => (
        <Chip
          label={params.value}
          size="small"
          color={actionColors[params.value] || 'default'}
        />
      ),
    },
    {
      field: 'entityType',
      headerName: 'Entity Type',
      width: 120,
    },
    {
      field: 'entityId',
      headerName: 'Entity ID',
      width: 200,
    },
    {
      field: 'user',
      headerName: 'User',
      width: 180,
      valueGetter: (params) =>
        params.row.user
          ? `${params.row.user.firstName} ${params.row.user.lastName} (${params.row.user.username})`
          : params.row.userId,
    },
    {
      field: 'ipAddress',
      headerName: 'IP Address',
      width: 140,
    },
    {
      field: 'details',
      headerName: 'Details',
      flex: 1,
      renderCell: (params: GridRenderCellParams<AuditLogEntry>) => (
        <Typography variant="body2" sx={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {JSON.stringify(params.value)}
        </Typography>
      ),
    },
  ]

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Audit Log
      </Typography>

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Grid container spacing={2} alignItems="center">
            <Grid item xs={12} sm={4}>
              <TextField
                fullWidth
                placeholder="Search..."
                size="small"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon />
                    </InputAdornment>
                  ),
                }}
              />
            </Grid>
            <Grid item xs={6} sm={2}>
              <FormControl fullWidth size="small">
                <InputLabel>Action</InputLabel>
                <Select
                  value={actionFilter}
                  label="Action"
                  onChange={(e) => setActionFilter(e.target.value)}
                >
                  <MenuItem value="">All</MenuItem>
                  <MenuItem value="create">Create</MenuItem>
                  <MenuItem value="update">Update</MenuItem>
                  <MenuItem value="delete">Delete</MenuItem>
                  <MenuItem value="login">Login</MenuItem>
                  <MenuItem value="logout">Logout</MenuItem>
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={6} sm={3}>
              <TextField
                fullWidth
                label="Start Date"
                type="date"
                size="small"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </Grid>
            <Grid item xs={6} sm={3}>
              <TextField
                fullWidth
                label="End Date"
                type="date"
                size="small"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <div style={{ height: 600, width: '100%' }}>
            <DataGrid
              rows={entries}
              columns={columns}
              loading={loading}
              pagination
              paginationMode="server"
              rowCount={total}
              page={page}
              pageSize={pageSize}
              rowsPerPageOptions={[25, 50, 100]}
              onPageChange={(newPage) => setPage(newPage)}
              onPageSizeChange={(newPageSize) => setPageSize(newPageSize)}
              getRowId={(row) => row.id}
            />
          </div>
        </CardContent>
      </Card>
    </Box>
  )
}

export default AuditLog
