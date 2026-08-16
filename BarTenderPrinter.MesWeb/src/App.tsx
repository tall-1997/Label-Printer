import { Alert, Button, LinearProgress, Snackbar } from '@mui/material'
import { lazy, Suspense, useEffect, useState } from 'react'
import { Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { loadSession } from './api/client'
import { demoSession, modules } from './app/modules'
import { AppShell } from './components/AppShell'
import type { PlatformMode, SessionCapabilities } from './types'

const OverviewPage = lazy(() => import('./pages/OverviewPage').then((module) => ({ default: module.OverviewPage })))
const DomainPage = lazy(() => import('./pages/DomainPage').then((module) => ({ default: module.DomainPage })))
const StationWorkspacePage = lazy(() => import('./pages/StationWorkspacePage').then((module) => ({ default: module.StationWorkspacePage })))
const TraceabilityPage = lazy(() => import('./pages/TraceabilityPage').then((module) => ({ default: module.TraceabilityPage })))

export default function App() {
  const [session, setSession] = useState<SessionCapabilities>(demoSession)
  const [demo, setDemo] = useState(true)
  const location = useLocation()
  const navigate = useNavigate()
  const mode: PlatformMode = location.pathname === '/workspace' ? 'station' : 'management'

  useEffect(() => {
    loadSession().then((value) => { setSession(value); setDemo(false) }).catch(() => setDemo(true))
  }, [])

  const changeMode = (next: PlatformMode) => {
    if (next === 'station') navigate('/workspace')
    else if (location.pathname === '/workspace') navigate('/')
  }

  return (
    <AppShell session={session} mode={mode} onModeChange={changeMode}>
      <Suspense fallback={<LinearProgress aria-label="正在加载页面" />}>
        <Routes>
          <Route path="/" element={<OverviewPage />} />
          {modules.filter((module) => module.mode === 'management' && module.path !== '/traceability').map((module) => <Route key={module.id} path={`${module.path}/*`} element={<DomainPage module={module} />} />)}
          <Route path="/workspace" element={<StationWorkspacePage session={session} />} />
          <Route path="/traceability" element={<TraceabilityPage />} />
          <Route path="*" element={<Alert severity="error" sx={{ m: 4 }}>页面不存在。<Button onClick={() => navigate('/')}>返回首页</Button></Alert>} />
        </Routes>
      </Suspense>
      <Snackbar open={demo} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        <Alert severity="warning" variant="filled" action={<Button color="inherit" size="small" onClick={() => setDemo(false)}>知道了</Button>}>当前使用演示数据。配置中心会话后将自动加载实时权限与业务数据。</Alert>
      </Snackbar>
    </AppShell>
  )
}
