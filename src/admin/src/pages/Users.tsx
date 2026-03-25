import React, { useEffect, useState, useCallback } from 'react'
import {
  Box,
  Card,
  CardContent,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TablePagination,
  Paper,
  IconButton,
  Typography,
  TextField,
  InputAdornment,
  Chip,
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
  Edit as EditIcon,
  Delete as DeleteIcon,
  Block as BlockIcon,
  CheckCircle as ActivateIcon,
} from '@mui/icons-material'
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid'
import { User, PaginatedResponse } from '../types'
import api from '../api/client'
import { format } from 'date-fns'

const Users: React.FC = () => {
  const [users, setUsers] = useState<User[]>([])
  const [loading, setLoading] = useState(true)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const [search, setSearch] = useState('')
  const [selectedUser, setSelectedUser] = useState<User | null>(null)
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)

  const fetchUsers = useCallback(async () => {
    setLoading(true)
    try {
      const response = await api.getUsers({
        page: page + 1,
        pageSize,
        search: search || undefined,
      })
      setUsers(response.items)
      setTotal(response.total)
    } catch (error) {
      console.error('Failed to fetch users:', error)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, search])

  useEffect(() => {
    fetchUsers()
  }, [fetchUsers])

  const handleMenuOpen = (event: React.MouseEvent<HTMLElement>, user: User) => {
    setAnchorEl(event.currentTarget)
    setSelectedUser(user)
  }

  const handleMenuClose = () => {
    setAnchorEl(null)
  }

  const handleEdit = () => {
    handleMenuClose()
    setEditDialogOpen(true)
  }

  const handleDelete = () => {
    handleMenuClose()
    setDeleteDialogOpen(true)
  }

  const handleActivate = async () => {
    handleMenuClose()
    if (selectedUser) {
      try {
        await api.activateUser(selectedUser.id)
        fetchUsers()
      } catch (error) {
        console.error('Failed to activate user:', error)
      }
    }
  }

  const handleDeactivate = async () => {
    handleMenuClose()
    if (selectedUser) {
      try {
        await api.deactivateUser(selectedUser.id)
        fetchUsers()
      } catch (error) {
        console.error('Failed to deactivate user:', error)
      }
    }
  }

  const confirmDelete = async () => {
    if (selectedUser) {
      try {
        await api.deleteUser(selectedUser.id)
        fetchUsers()
      } catch (error) {
        console.error('Failed to delete user:', error)
      }
    }
    setDeleteDialogOpen(false)
    setSelectedUser(null)
  }

  const columns: GridColDef[] = [
    { field: 'id', headerName: 'ID', width: 200 },
    {
      field: 'username',
      headerName: 'Username',
      width: 150,
    },
    {
      field: 'fullName',
      headerName: 'Name',
      width: 200,
      valueGetter: (params) => `${params.row.firstName || ''} ${params.row.lastName || ''}`,
    },
    { field: 'email', headerName: 'Email', width: 200 },
    { field: 'phone', headerName: 'Phone', width: 150 },
    {
      field: 'status',
      headerName: 'Status',
      width: 100,
      renderCell: (params: GridRenderCellParams<User>) => (
        <Chip
          label={params.value}
          size="small"
          color={
            params.value === 'online' ? 'success' : params.value === 'away' ? 'warning' : 'default'
          }
        />
      ),
    },
    {
      field: 'isActive',
      headerName: 'Active',
      width: 100,
      renderCell: (params: GridRenderCellParams<User>) => (
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
      renderCell: (params: GridRenderCellParams<User>) => (
        <IconButton onClick={(e) => handleMenuOpen(e, params.row)}>
          <MoreVertIcon />
        </IconButton>
      ),
    },
  ]

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Typography variant="h4">Users</Typography>
        <TextField
          placeholder="Search users..."
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
              rows={users}
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
        <MenuItem onClick={handleEdit}>
          <EditIcon sx={{ mr: 1 }} /> Edit
        </MenuItem>
        {selectedUser?.isActive ? (
          <MenuItem onClick={handleDeactivate}>
            <BlockIcon sx={{ mr: 1 }} /> Deactivate
          </MenuItem>
        ) : (
          <MenuItem onClick={handleActivate}>
            <ActivateIcon sx={{ mr: 1 }} /> Activate
          </MenuItem>
        )}
        <MenuItem onClick={handleDelete} sx={{ color: 'error.main' }}>
          <DeleteIcon sx={{ mr: 1 }} /> Delete
        </MenuItem>
      </Menu>

      <Dialog open={deleteDialogOpen} onClose={() => setDeleteDialogOpen(false)}>
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          Are you sure you want to delete user "{selectedUser?.username}"? This action cannot be
          undone.
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

export default Users
