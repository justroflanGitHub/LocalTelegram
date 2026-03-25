import React, { useEffect, useState, useCallback } from 'react'
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  InputAdornment,
  Chip,
  IconButton,
  Menu,
  MenuItem,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
} from '@mui/material'
import {
  Search as SearchIcon,
  MoreVert as MoreVertIcon,
  Delete as DeleteIcon,
  Group as GroupIcon,
} from '@mui/icons-material'
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid'
import { Group } from '../types'
import api from '../api/client'
import { format } from 'date-fns'

const Groups: React.FC = () => {
  const [groups, setGroups] = useState<Group[]>([])
  const [loading, setLoading] = useState(true)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const [search, setSearch] = useState('')
  const [selectedGroup, setSelectedGroup] = useState<Group | null>(null)
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)

  const fetchGroups = useCallback(async () => {
    setLoading(true)
    try {
      const response = await api.getGroups({
        page: page + 1,
        pageSize,
        search: search || undefined,
      })
      setGroups(response.items)
      setTotal(response.total)
    } catch (error) {
      console.error('Failed to fetch groups:', error)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, search])

  useEffect(() => {
    fetchGroups()
  }, [fetchGroups])

  const handleMenuOpen = (event: React.MouseEvent<HTMLElement>, group: Group) => {
    setAnchorEl(event.currentTarget)
    setSelectedGroup(group)
  }

  const handleMenuClose = () => {
    setAnchorEl(null)
  }

  const handleDelete = () => {
    handleMenuClose()
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (selectedGroup) {
      try {
        await api.deleteGroup(selectedGroup.id)
        fetchGroups()
      } catch (error) {
        console.error('Failed to delete group:', error)
      }
    }
    setDeleteDialogOpen(false)
    setSelectedGroup(null)
  }

  const columns: GridColDef[] = [
    { field: 'id', headerName: 'ID', width: 200 },
    {
      field: 'name',
      headerName: 'Name',
      width: 200,
      renderCell: (params: GridRenderCellParams<Group>) => (
        <Box sx={{ display: 'flex', alignItems: 'center' }}>
          {params.row.avatarUrl ? (
            <img
              src={params.row.avatarUrl}
              alt={params.value}
              style={{ width: 32, height: 32, borderRadius: '50%', marginRight: 8 }}
            />
          ) : (
            <GroupIcon sx={{ mr: 1, color: 'text.secondary' }} />
          )}
          {params.value}
        </Box>
      ),
    },
    { field: 'description', headerName: 'Description', width: 300, flex: 1 },
    {
      field: 'memberCount',
      headerName: 'Members',
      width: 100,
      valueFormatter: (params) => params.value?.toLocaleString(),
    },
    {
      field: 'isActive',
      headerName: 'Status',
      width: 100,
      renderCell: (params: GridRenderCellParams<Group>) => (
        <Chip
          label={params.value ? 'Active' : 'Inactive'}
          size="small"
          color={params.value ? 'success' : 'error'}
        />
      ),
    },
    {
      field: 'createdAt',
      headerName: 'Created',
      width: 150,
      valueFormatter: (params) => format(new Date(params.value as string), 'MMM dd, yyyy'),
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 80,
      sortable: false,
      renderCell: (params: GridRenderCellParams<Group>) => (
        <IconButton onClick={(e) => handleMenuOpen(e, params.row)}>
          <MoreVertIcon />
        </IconButton>
      ),
    },
  ]

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Typography variant="h4">Groups</Typography>
        <TextField
          placeholder="Search groups..."
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
      </Box>

      <Card>
        <CardContent>
          <div style={{ height: 600, width: '100%' }}>
            <DataGrid
              rows={groups}
              columns={columns}
              loading={loading}
              pagination
              paginationMode="server"
              rowCount={total}
              page={page}
              pageSize={pageSize}
              rowsPerPageOptions={[10, 25, 50]}
              onPageChange={(newPage) => setPage(newPage)}
              onPageSizeChange={(newPageSize) => setPageSize(newPageSize)}
              getRowId={(row) => row.id}
            />
          </div>
        </CardContent>
      </Card>

      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={handleMenuClose}>
        <MenuItem onClick={handleDelete} sx={{ color: 'error.main' }}>
          <DeleteIcon sx={{ mr: 1 }} /> Delete
        </MenuItem>
      </Menu>

      <Dialog open={deleteDialogOpen} onClose={() => setDeleteDialogOpen(false)}>
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          Are you sure you want to delete group "{selectedGroup?.name}"? All messages and data will
          be permanently deleted. This action cannot be undone.
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteDialogOpen(false)}>Cancel</Button>
          <Button onClick={confirmDelete} color="error" variant="contained">
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}

export default Groups
