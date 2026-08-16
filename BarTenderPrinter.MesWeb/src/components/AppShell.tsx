import DarkModeRounded from '@mui/icons-material/DarkModeRounded'
import DashboardRounded from '@mui/icons-material/DashboardRounded'
import FactoryRounded from '@mui/icons-material/FactoryRounded'
import LightModeRounded from '@mui/icons-material/LightModeRounded'
import MenuRounded from '@mui/icons-material/MenuRounded'
import PrecisionManufacturingRounded from '@mui/icons-material/PrecisionManufacturingRounded'
import SearchRounded from '@mui/icons-material/SearchRounded'
import {
  AppBar, Avatar, Badge, Box, Button, Chip, Divider, Drawer, IconButton, List,
  ListItemButton, ListItemIcon, ListItemText, Stack, Toolbar, Tooltip, Typography,
} from '@mui/material'
import { useColorScheme } from '@mui/material/styles'
import { useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { modules } from '../app/modules'
import type { PlatformMode, SessionCapabilities } from '../types'

const drawerWidth = 264

interface Props {
  children: ReactNode
  session: SessionCapabilities
  mode: PlatformMode
  onModeChange: (mode: PlatformMode) => void
}

export function AppShell({ children, session, mode, onModeChange }: Props) {
  const [mobileOpen, setMobileOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()
  const { mode: colorMode, setMode } = useColorScheme()
  const visibleModules = modules.filter((module) =>
    (module.mode === mode || module.mode === 'shared') && session.capabilities.includes(module.capability))

  const navigation = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar sx={{ gap: 1.5, px: 2.5 }}>
        <Avatar variant="rounded" sx={{ bgcolor: 'primary.main', color: 'primary.contrastText' }}><FactoryRounded /></Avatar>
        <Box>
          <Typography fontWeight={800} lineHeight={1.1}>BarTender MES</Typography>
          <Typography variant="caption" color="text.secondary">制造执行协同平台</Typography>
        </Box>
      </Toolbar>
      <Divider />
      <Stack direction="row" spacing={1} sx={{ p: 1.5 }}>
        <Button fullWidth size="small" variant={mode === 'management' ? 'contained' : 'text'} startIcon={<DashboardRounded />} onClick={() => onModeChange('management')}>管理</Button>
        <Button fullWidth size="small" variant={mode === 'station' ? 'contained' : 'text'} startIcon={<PrecisionManufacturingRounded />} onClick={() => onModeChange('station')}>工位</Button>
      </Stack>
      <List component="nav" aria-label="主要功能" sx={{ px: 1.25, py: 0.5, flex: 1, overflow: 'auto' }}>
        {visibleModules.map((module) => {
          const active = module.path === '/' ? location.pathname === '/' : location.pathname.startsWith(module.path)
          return (
            <ListItemButton key={module.id} selected={active} onClick={() => { navigate(module.path); setMobileOpen(false) }} sx={{ mb: 0.5, borderRadius: 1.5, minHeight: 46 }}>
              <ListItemIcon sx={{ minWidth: 34 }}><Box sx={{ width: 10, height: 10, borderRadius: '3px', bgcolor: module.accent }} /></ListItemIcon>
              <ListItemText primary={module.title} secondary={module.description} slotProps={{ primary: { fontWeight: active ? 700 : 600 }, secondary: { noWrap: true, fontSize: 11 } }} />
            </ListItemButton>
          )
        })}
      </List>
      <Divider />
      <Box sx={{ p: 2 }}>
        <Stack direction="row" spacing={1.5} alignItems="center">
          <Avatar sx={{ width: 36, height: 36 }}>{session.displayName.slice(0, 1)}</Avatar>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" fontWeight={700} noWrap>{session.displayName}</Typography>
            <Typography variant="caption" color="text.secondary" noWrap>{session.stationId} · {session.shiftId}</Typography>
          </Box>
        </Stack>
      </Box>
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100dvh' }}>
      <Box component="a" href="#main-content" sx={{ position: 'fixed', left: 8, top: -60, zIndex: 2000, bgcolor: 'background.paper', p: 1.5, '&:focus': { top: 8 } }}>跳到主要内容</Box>
      <AppBar position="fixed" color="inherit" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider', width: { md: `calc(100% - ${drawerWidth}px)` }, ml: { md: `${drawerWidth}px` } }}>
        <Toolbar sx={{ gap: 1 }}>
          <IconButton aria-label="打开导航" edge="start" onClick={() => setMobileOpen(true)} sx={{ display: { md: 'none' } }}><MenuRounded /></IconButton>
          <Box sx={{ flex: 1 }}>
            <Typography variant="body2" fontWeight={700}>{mode === 'station' ? '生产工位模式' : '运营管理模式'}</Typography>
            <Typography variant="caption" color="text.secondary">中心连接正常 · PostgreSQL v17</Typography>
          </Box>
          <Chip size="small" color="success" variant="outlined" label="在线" sx={{ display: { xs: 'none', sm: 'flex' } }} />
          <Tooltip title="全局搜索"><IconButton aria-label="全局搜索"><SearchRounded /></IconButton></Tooltip>
          <Tooltip title="切换明暗主题"><IconButton aria-label="切换明暗主题" onClick={() => setMode(colorMode === 'dark' ? 'light' : 'dark')}>{colorMode === 'dark' ? <LightModeRounded /> : <DarkModeRounded />}</IconButton></Tooltip>
          <Tooltip title="待处理任务"><IconButton aria-label="待处理任务"><Badge color="warning" badgeContent={3}><Box sx={{ width: 18, height: 18 }} /></Badge></IconButton></Tooltip>
        </Toolbar>
      </AppBar>
      <Box component="nav" aria-label="平台导航" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)} ModalProps={{ keepMounted: true }} sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: drawerWidth } }}>{navigation}</Drawer>
        <Drawer variant="permanent" open sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: drawerWidth, borderRight: 1, borderColor: 'divider' } }}>{navigation}</Drawer>
      </Box>
      <Box component="main" id="main-content" tabIndex={-1} sx={{ flex: 1, minWidth: 0, pt: 8, minHeight: '100dvh' }}>
        {children}
      </Box>
    </Box>
  )
}
