export interface ApiErrorContract {
  code: string
  message: string
  correlationId: string
  retryable: boolean
  details?: unknown
}

export interface PlatformSessionContract {
  userId: string
  displayName: string
  stationId: string
  shiftId: string
  roles: string[]
  capabilities: string[]
}

export type TraceabilityQueryContract = 'Order' | 'Imei' | 'SerialNumber' | 'Carton' | 'Pallet'
export type TraceabilityResultContract = Record<string, unknown>
