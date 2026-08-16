import type { ModuleManifest, SessionCapabilities } from '../types'

export const modules: ModuleManifest[] = [
  { id: 'overview', path: '/', title: '运营总览', shortTitle: '总览', description: '订单、在制、质量和设备态势', capability: 'dashboard.view', mode: 'shared', accent: '#0f766e' },
  { id: 'orders', path: '/orders', title: '订单与计划', shortTitle: '计划', description: '生产订单、排程、版本与交付窗口', capability: 'orders.view', mode: 'management', accent: '#2563eb' },
  { id: 'engineering', path: '/engineering', title: '产品与工艺', shortTitle: '工艺', description: '产品、BOM、路线、工序和工位资格', capability: 'engineering.view', mode: 'management', accent: '#7c3aed' },
  { id: 'numbering', path: '/numbering', title: '号码与模板', shortTitle: '号码', description: '号段、标识分配、标签模板和版本', capability: 'numbering.view', mode: 'management', accent: '#0891b2' },
  { id: 'production', path: '/production', title: '生产执行', shortTitle: '生产', description: '生产单元、过站、包装、写号和打印', capability: 'production.view', mode: 'management', accent: '#0f766e' },
  { id: 'quality', path: '/quality', title: '质量与返工', shortTitle: '质量', description: '检验、冻结、处置和返工闭环', capability: 'quality.view', mode: 'management', accent: '#c2410c' },
  { id: 'warehouse', path: '/warehouse', title: '仓储与出库', shortTitle: '仓储', description: '成品入库、箱码扫描、出库确认', capability: 'warehouse.view', mode: 'management', accent: '#4d7c0f' },
  { id: 'traceability', path: '/traceability', title: '追溯与归档', shortTitle: '追溯', description: '全链路履历、归档校验和受控修复', capability: 'traceability.view', mode: 'shared', accent: '#475569' },
  { id: 'stations', path: '/stations', title: '设备与工位', shortTitle: '设备', description: '工位健康、设备配置和适配器状态', capability: 'stations.view', mode: 'management', accent: '#0369a1' },
  { id: 'workspace', path: '/workspace', title: '工位工作台', shortTitle: '工作台', description: '扫码驱动的当前任务与作业反馈', capability: 'workspace.use', mode: 'station', accent: '#0f766e' },
]

export const demoSession: SessionCapabilities = {
  userId: 'operator-demo',
  displayName: '演示操作员',
  stationId: 'STATION-07',
  shiftId: 'DAY-A',
  roles: ['ProductionOperator', 'QualityEngineer'],
  capabilities: modules.map((module) => module.capability),
}
