import AddRounded from '@mui/icons-material/AddRounded'
import DownloadRounded from '@mui/icons-material/DownloadRounded'
import FilterAltRounded from '@mui/icons-material/FilterAltRounded'
import {
  Box, Button, Card, CardContent, Chip, InputAdornment, Stack, Tab, Tabs,
  TextField, Typography,
} from '@mui/material'
import SearchRounded from '@mui/icons-material/SearchRounded'
import { useSearchParams } from 'react-router-dom'
import type { ModuleManifest } from '../types'

export function DomainPage({ module }: { module: ModuleManifest }) {
  const [params, setParams] = useSearchParams()
  const query = params.get('q') ?? ''
  const tab = Number(params.get('tab') ?? 0)
  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1600, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}>
        <Box><Typography variant="overline" sx={{ color: module.accent, fontWeight: 800 }}>BUSINESS DOMAIN</Typography><Typography variant="h1">{module.title}</Typography><Typography color="text.secondary" sx={{ mt: 1 }}>{module.description}</Typography></Box>
        <Stack direction="row" spacing={1}><Button variant="outlined" startIcon={<DownloadRounded />}>导出</Button><Button variant="contained" startIcon={<AddRounded />}>新建业务单据</Button></Stack>
      </Stack>
      <Card sx={{ mt: 3 }}>
        <CardContent sx={{ pb: 0 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} alignItems={{ md: 'center' }}>
            <TextField label={`搜索${module.title}`} fullWidth value={query} onChange={(event) => { const next = new URLSearchParams(params); if (event.target.value) next.set('q', event.target.value); else next.delete('q'); setParams(next, { replace: true }) }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRounded /></InputAdornment> } }} />
            <Button variant="outlined" startIcon={<FilterAltRounded />} sx={{ whiteSpace: 'nowrap' }}>高级筛选</Button>
          </Stack>
          <Tabs value={tab} onChange={(_, value) => { const next = new URLSearchParams(params); next.set('tab', String(value)); setParams(next, { replace: true }) }} variant="scrollable" scrollButtons="auto" sx={{ mt: 2 }}><Tab label="全部" /><Tab label="执行中" /><Tab label="待处理" /><Tab label="已完成" /></Tabs>
        </CardContent>
      </Card>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 2, mt: 2 }}>
        {['待我处理', '本周完成', '异常关注'].map((title, index) => (
          <Card key={title}><CardContent><Stack direction="row" justifyContent="space-between"><Typography variant="h3">{title}</Typography><Chip size="small" label={index === 0 ? '12' : index === 1 ? '148' : '3'} color={index === 2 ? 'warning' : 'default'} /></Stack><Typography variant="body2" color="text.secondary" sx={{ mt: 3 }}>此区域将承载迁移后的 {module.shortTitle} 业务列表、批量操作和可配置列视图。</Typography><Button sx={{ mt: 2, px: 0 }}>查看工作队列</Button></CardContent></Card>
        ))}
      </Box>
    </Box>
  )
}
