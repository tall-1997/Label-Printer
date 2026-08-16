import SearchRounded from '@mui/icons-material/SearchRounded'
import {
  Alert, Box, Button, Card, CardContent, CircularProgress, InputAdornment, MenuItem, Stack, TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ApiError, queryTraceability, type TraceabilityQueryType } from '../api/client'

export function TraceabilityPage() {
  const [params, setParams] = useSearchParams()
  const [query, setQuery] = useState(params.get('value') ?? '')
  const [type, setType] = useState<TraceabilityQueryType>((params.get('type') as TraceabilityQueryType) ?? 'Order')
  const [result, setResult] = useState<unknown>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const submit = async () => {
    const value = query.trim()
    if (!value || busy) return
    setBusy(true); setError(''); setParams({ type, value })
    try { setResult(await queryTraceability(type, value)) }
    catch (reason) { setResult(null); setError(reason instanceof ApiError ? reason.message : '追溯查询失败。') }
    finally { setBusy(false) }
  }
  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1300, mx: 'auto' }}>
      <Typography variant="overline" color="primary.main" fontWeight={800}>GENEALOGY & ARCHIVE</Typography><Typography variant="h1">全链路追溯</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>使用订单、IMEI、SN、卡通箱或卡板码查询生产履历。</Typography>
      <Card sx={{ mt: 3 }}><CardContent sx={{ p: 3 }}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}><TextField select label="标识类型" value={type} onChange={(event) => setType(event.target.value as TraceabilityQueryType)} sx={{ minWidth: 150 }}>{['Order', 'Imei', 'SerialNumber', 'Carton', 'Pallet'].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField><TextField label="追溯标识" fullWidth value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter') void submit() }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded /></InputAdornment>, className: 'tabular' } }} /><Button variant="contained" disabled={!query.trim() || busy} onClick={() => void submit()}>{busy ? <CircularProgress size={22} color="inherit" /> : '查询履历'}</Button></Stack></CardContent></Card>
      <Box aria-live="polite">{error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}{result ? <Card sx={{ mt: 2 }}><CardContent><Typography variant="h2">追溯结果</Typography><Typography className="tabular" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{query}</Typography><Box component="pre" sx={{ mt: 2, p: 2, bgcolor: 'action.hover', borderRadius: 2, overflow: 'auto', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{JSON.stringify(result, null, 2)}</Box></CardContent></Card> : !error && <Alert severity="info" sx={{ mt: 2 }}>查询结果将聚合生产、过站、包装、称重、写号、打印、质量、返工、出库、归档和审计履历。</Alert>}</Box>
    </Box>
  )
}
