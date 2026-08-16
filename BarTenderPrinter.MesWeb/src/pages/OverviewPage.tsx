import ArrowForwardRounded from '@mui/icons-material/ArrowForwardRounded'
import CheckCircleRounded from '@mui/icons-material/CheckCircleRounded'
import {
  Box, Button, Card, CardContent, Chip, LinearProgress, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, Typography,
} from '@mui/material'
import { useNavigate } from 'react-router-dom'
import { MetricCard } from '../components/MetricCard'
import type { ActivityItem, Metric } from '../types'

const metrics: Metric[] = [
  { label: '今日计划', value: '12,480', detail: '较昨日增加 6.2%', tone: 'neutral' },
  { label: '当前在制', value: '3,216', detail: '覆盖 6 条产线', tone: 'success' },
  { label: '待质量处置', value: '18', detail: '其中高优先级 3 项', tone: 'warning' },
  { label: '设备异常', value: '2', detail: '均已分派维护人员', tone: 'danger' },
]

const activities: ActivityItem[] = [
  { id: '1', time: '10:42', title: '订单 MO-20260816-018 开始生产', detail: 'Line-03 · OP-Assembly-20', state: '执行中' },
  { id: '2', time: '10:37', title: '质量处置 QD-8842 已放行', detail: '操作员：质量主管-02', state: '完成' },
  { id: '3', time: '10:25', title: '卡板 PLT-260816-004 已满板', detail: '48 箱 · 标签作业已创建', state: '待处理' },
  { id: '4', time: '10:14', title: '写号任务返回回读不一致', detail: 'STATION-09 · 已冻结关联号码', state: '异常' },
]

const stateColor = { 完成: 'success', 执行中: 'info', 待处理: 'warning', 异常: 'error' } as const

export function OverviewPage() {
  const navigate = useNavigate()
  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: 1680, mx: 'auto' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2} alignItems={{ md: 'flex-end' }}>
        <Box>
          <Typography variant="overline" color="primary.main" fontWeight={800}>OPERATIONS CONTROL</Typography>
          <Typography variant="h1">生产运营总览</Typography>
          <Typography color="text.secondary" sx={{ mt: 1 }}>聚合计划、执行、质量、仓储与设备状态，数据更新时间 10:45:08</Typography>
        </Box>
        <Stack direction="row" spacing={1}>
          <Button variant="outlined">导出日报</Button>
          <Button variant="contained" endIcon={<ArrowForwardRounded />} onClick={() => navigate('/workspace')}>进入工位</Button>
        </Stack>
      </Stack>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', xl: 'repeat(4, 1fr)' }, gap: 2, mt: 3 }}>
        {metrics.map((metric) => <MetricCard key={metric.label} metric={metric} />)}
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', xl: 'minmax(0, 1.65fr) minmax(340px, .85fr)' }, gap: 2, mt: 2 }}>
        <Card>
          <CardContent sx={{ p: 0 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2.5, pb: 1.5 }}>
              <Box><Typography variant="h2">产线执行进度</Typography><Typography variant="body2" color="text.secondary">按计划完成率和当前节拍排序</Typography></Box>
              <Button size="small">查看全部</Button>
            </Stack>
            <TableContainer>
              <Table size="small" aria-label="产线执行进度">
                <TableHead><TableRow><TableCell>产线</TableCell><TableCell>当前订单</TableCell><TableCell>完成率</TableCell><TableCell>节拍</TableCell><TableCell>状态</TableCell></TableRow></TableHead>
                <TableBody>
                  {[
                    ['Line-01', 'MO-20260816-011', 84, '42s', '稳定'],
                    ['Line-03', 'MO-20260816-018', 62, '48s', '稳定'],
                    ['Line-05', 'MO-20260816-022', 39, '57s', '关注'],
                    ['Line-07', 'MO-20260816-024', 18, '45s', '换线'],
                  ].map(([line, order, progress, takt, state]) => (
                    <TableRow key={line as string} hover>
                      <TableCell className="tabular" sx={{ fontWeight: 700 }}>{line}</TableCell>
                      <TableCell className="tabular">{order}</TableCell>
                      <TableCell sx={{ minWidth: 140 }}><Stack direction="row" spacing={1} alignItems="center"><LinearProgress variant="determinate" value={progress as number} sx={{ flex: 1, height: 7, borderRadius: 4 }} /><Typography variant="caption" className="tabular">{progress}%</Typography></Stack></TableCell>
                      <TableCell className="tabular">{takt}</TableCell>
                      <TableCell><Chip size="small" color={state === '关注' ? 'warning' : 'success'} variant="outlined" label={state} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>

        <Card>
          <CardContent sx={{ p: 2.5 }}>
            <Typography variant="h2">实时活动</Typography>
            <Typography variant="body2" color="text.secondary">最近 30 分钟关键制造事件</Typography>
            <Stack spacing={2.25} sx={{ mt: 2.5 }}>
              {activities.map((activity) => (
                <Stack key={activity.id} direction="row" spacing={1.5} alignItems="flex-start">
                  <CheckCircleRounded color={stateColor[activity.state]} fontSize="small" />
                  <Box sx={{ flex: 1, minWidth: 0 }}><Typography variant="body2" fontWeight={700}>{activity.title}</Typography><Typography variant="caption" color="text.secondary">{activity.detail}</Typography></Box>
                  <Typography variant="caption" className="tabular" color="text.secondary">{activity.time}</Typography>
                </Stack>
              ))}
            </Stack>
          </CardContent>
        </Card>
      </Box>
    </Box>
  )
}
