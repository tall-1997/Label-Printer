export type PlatformMode = 'management' | 'station'

export interface SessionCapabilities {
  userId: string
  displayName: string
  stationId: string
  shiftId: string
  roles: string[]
  capabilities: string[]
}

export interface ModuleManifest {
  id: string
  path: string
  title: string
  shortTitle: string
  description: string
  capability: string
  mode: PlatformMode | 'shared'
  accent: string
}

export interface Metric {
  label: string
  value: string
  detail: string
  tone: 'neutral' | 'success' | 'warning' | 'danger'
}

export interface ActivityItem {
  id: string
  time: string
  title: string
  detail: string
  state: '完成' | '执行中' | '待处理' | '异常'
}

export interface ScanResult {
  kind: '订单' | '生产单元' | '包装单元' | '标识码'
  code: string
  headline: string
  detail: string
  actions: string[]
}
