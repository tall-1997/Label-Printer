import BoltRounded from '@mui/icons-material/BoltRounded'
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded'
import PrintRounded from '@mui/icons-material/PrintRounded'
import QrCodeScannerRounded from '@mui/icons-material/QrCodeScannerRounded'
import ScaleRounded from '@mui/icons-material/ScaleRounded'
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, InputAdornment, Stack,
  TextField, Typography,
} from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import type { ScanResult, SessionCapabilities } from '../types'

function classify(code: string): ScanResult {
  const normalized = code.trim().toUpperCase()
  if (normalized.startsWith('MO-')) return { kind: '订单', code: normalized, headline: '生产订单已载入', detail: '订单处于生产中，当前工位允许执行组装与包装任务。', actions: ['设为当前订单', '查看路线'] }
  if (normalized.startsWith('PLT') || normalized.startsWith('CTN')) return { kind: '包装单元', code: normalized, headline: '包装单元已识别', detail: '包装关系与质量状态校验通过，可继续绑定或称重。', actions: ['绑定子项', '执行称重'] }
  if (/^\d{15}$/.test(normalized)) return { kind: '标识码', code: normalized, headline: 'IMEI 已识别', detail: '标识已分配到当前订单，等待过站确认。', actions: ['组装过站', '核对写号'] }
  return { kind: '生产单元', code: normalized, headline: '生产单元已识别', detail: '已加载当前工序、包装上下文和未完成任务。', actions: ['完成当前工序', '报告异常'] }
}

export function StationWorkspacePage({ session }: { session: SessionCapabilities }) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [scan, setScan] = useState('')
  const [result, setResult] = useState<ScanResult | null>(() => { const value = sessionStorage.getItem('station-workspace-result'); return value ? JSON.parse(value) as ScanResult : null })
  const [busy, setBusy] = useState(false)
  useEffect(() => { inputRef.current?.focus() }, [])

  const submit = () => {
    if (!scan.trim() || busy) return
    setBusy(true)
    window.setTimeout(() => { const next = classify(scan); setResult(next); sessionStorage.setItem('station-workspace-result', JSON.stringify(next)); setScan(''); setBusy(false); inputRef.current?.focus() }, 350)
  }

  return (
    <Box sx={{ p: { xs: 2, sm: 3 }, maxWidth: 1480, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', lg: 'row' }} justifyContent="space-between" gap={2} alignItems={{ lg: 'center' }}>
        <Box><Typography variant="overline" color="primary.main" fontWeight={800}>STATION WORKSPACE</Typography><Typography variant="h1">扫码作业工作台</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>{session.stationId} · {session.shiftId} · {session.displayName}</Typography></Box>
        <Stack direction="row" spacing={1} flexWrap="wrap"><Chip color="success" variant="outlined" label="中心在线" /><Chip color="success" variant="outlined" label="BarTender 就绪" /><Chip color="warning" variant="outlined" label="模拟设备" /></Stack>
      </Stack>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', xl: 'minmax(0, 1.55fr) minmax(360px, .7fr)' }, gap: 2, mt: 3 }}>
        <Stack spacing={2}>
          <Card sx={{ borderTop: 4, borderColor: 'primary.main' }}>
            <CardContent sx={{ p: { xs: 2.5, md: 4 } }}>
              <Stack direction="row" spacing={1.5} alignItems="center"><QrCodeScannerRounded color="primary" /><Box><Typography variant="h2">扫描订单、生产单元或包装码</Typography><Typography variant="body2" color="text.secondary">扫描完成后自动识别业务对象并加载允许动作</Typography></Box></Stack>
              <TextField
                inputRef={inputRef}
                value={scan}
                onChange={(event) => setScan(event.target.value)}
                onKeyDown={(event) => { if (event.key === 'Enter') submit() }}
                fullWidth
                label="扫码输入"
                disabled={busy}
                sx={{ mt: 3, '& .MuiInputBase-root': { minHeight: 62, fontSize: '1.2rem' } }}
                slotProps={{ input: { startAdornment: <InputAdornment position="start"><BoltRounded color="primary" /></InputAdornment>, className: 'tabular' } }}
              />
              <Button fullWidth variant="contained" size="large" disabled={!scan.trim() || busy} onClick={submit} sx={{ mt: 1.5 }}>{busy ? '正在识别…' : '识别并继续'}</Button>
            </CardContent>
          </Card>

          {result ? (
            <Card role="status" aria-live="polite">
              <CardContent sx={{ p: 3 }}>
                <Stack direction="row" spacing={1.5} alignItems="flex-start"><CheckCircleRounded color="success" sx={{ mt: 0.25 }} /><Box sx={{ flex: 1 }}><Typography variant="overline" color="success.main" fontWeight={800}>{result.kind}</Typography><Typography variant="h2">{result.headline}</Typography><Typography className="tabular" sx={{ mt: 1, fontWeight: 700, overflowWrap: 'anywhere' }}>{result.code}</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>{result.detail}</Typography><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mt: 3 }}>{result.actions.map((action, index) => <Button key={action} variant={index === 0 ? 'contained' : 'outlined'}>{action}</Button>)}</Stack></Box></Stack>
              </CardContent>
            </Card>
          ) : <Alert severity="info">等待扫描。工作台会自动继承当前订单、工位和班次，操作员无需录入内部 ID。</Alert>}
        </Stack>

        <Stack spacing={2}>
          <Card><CardContent><Typography variant="h2">当前任务</Typography><Typography variant="body2" color="text.secondary">MO-20260816-018 · 智能终端 X7</Typography><Divider sx={{ my: 2 }} /><Stack spacing={1.5}>{[['当前工序', '整机组装 OP-20'], ['计划 / 完成', '1,200 / 744'], ['当前节拍', '48 秒'], ['质量状态', '允许生产']].map(([label, value]) => <Stack key={label} direction="row" justifyContent="space-between"><Typography color="text.secondary" variant="body2">{label}</Typography><Typography variant="body2" fontWeight={700} className={label.includes('/') ? 'tabular' : ''}>{value}</Typography></Stack>)}</Stack></CardContent></Card>
          <Card><CardContent><Typography variant="h2">快捷作业</Typography><Stack spacing={1} sx={{ mt: 2 }}><Button variant="outlined" startIcon={<ScaleRounded />} sx={{ justifyContent: 'flex-start' }}>包装称重</Button><Button variant="outlined" startIcon={<PrintRounded />} sx={{ justifyContent: 'flex-start' }}>领取打印作业</Button><Button variant="outlined" color="warning" sx={{ justifyContent: 'flex-start' }}>进入异常处理</Button></Stack></CardContent></Card>
          <Card><CardContent><Stack direction="row" justifyContent="space-between"><Typography variant="h2">待恢复操作</Typography><Chip size="small" label="Agent" /></Stack><Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>恢复队列由本机 Station Agent SQLite outbox 提供。</Typography><Button disabled sx={{ mt: 1.5, px: 0 }}>等待 Agent 连接</Button></CardContent></Card>
        </Stack>
      </Box>
    </Box>
  )
}
