import TrendingUpRounded from '@mui/icons-material/TrendingUpRounded'
import { Box, Card, CardContent, Stack, Typography } from '@mui/material'
import type { Metric } from '../types'

const tones = {
  neutral: 'primary.main',
  success: 'success.main',
  warning: 'warning.main',
  danger: 'error.main',
} as const

export function MetricCard({ metric }: { metric: Metric }) {
  return (
    <Card sx={{ height: '100%', position: 'relative', overflow: 'hidden' }}>
      <Box sx={{ position: 'absolute', inset: '0 auto 0 0', width: 4, bgcolor: tones[metric.tone] }} />
      <CardContent sx={{ p: 2.5 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
          <Box>
            <Typography color="text.secondary" variant="body2" fontWeight={600}>{metric.label}</Typography>
            <Typography className="tabular" sx={{ mt: 0.75, fontSize: '1.8rem', fontWeight: 700, lineHeight: 1.1 }}>{metric.value}</Typography>
          </Box>
          <TrendingUpRounded sx={{ color: tones[metric.tone] }} aria-hidden="true" />
        </Stack>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>{metric.detail}</Typography>
      </CardContent>
    </Card>
  )
}
