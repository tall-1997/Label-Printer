import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest } from './client'

describe('apiRequest', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('handles no-content responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))
    await expect(apiRequest<void>('/api/test')).resolves.toBeUndefined()
  })

  it('classifies network failures as retryable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await expect(apiRequest('/api/test')).rejects.toMatchObject({ code: 'NETWORK_ERROR', retryable: true } satisfies Partial<ApiError>)
  })
})
