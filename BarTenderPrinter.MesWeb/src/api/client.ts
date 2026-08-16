import type { SessionCapabilities } from '../types'

const tokenKey = 'bartender-mes-session-token'

export class ApiError extends Error {
  constructor(
    public readonly code: string,
    message: string,
    public readonly correlationId?: string,
    public readonly retryable = false,
  ) {
    super(message)
  }
}

export function getToken() {
  return sessionStorage.getItem(tokenKey) ?? ''
}

export function setToken(token: string) {
  const value = token.trim()
  if (value) sessionStorage.setItem(tokenKey, value)
  else sessionStorage.removeItem(tokenKey)
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const token = getToken()
  let response: Response
  try {
    response = await fetch(path, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...init?.headers,
      },
    })
  } catch {
    throw new ApiError('NETWORK_ERROR', '无法连接中心服务。', undefined, true)
  }

  if (!response.ok) {
    const body = await response.json().catch(() => null) as null | {
      code?: string
      message?: string
      correlationId?: string
      retryable?: boolean
    }
    throw new ApiError(
      body?.code ?? `HTTP_${response.status}`,
      body?.message ?? '中心服务请求失败。',
      body?.correlationId,
      body?.retryable,
    )
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export function loadSession() {
  return apiRequest<SessionCapabilities>('/api/v1/session')
}

export type TraceabilityQueryType = 'Order' | 'Imei' | 'SerialNumber' | 'Carton' | 'Pallet'

export function queryTraceability(type: TraceabilityQueryType, value: string, signal?: AbortSignal) {
  const search = new URLSearchParams({ type, value })
  return apiRequest<unknown>(`/api/v1/traceability?${search}`, { signal })
}
