import { ThemeProvider } from '@mui/material'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import { theme } from './theme'

vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('offline'))))

describe('MES web application', () => {
  it('renders the operations overview with demo fallback', async () => {
    render(<ThemeProvider theme={theme}><MemoryRouter><App /></MemoryRouter></ThemeProvider>)
    expect(await screen.findByRole('heading', { name: '生产运营总览' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '工位' }))
    expect(await screen.findByRole('heading', { name: '扫码作业工作台' })).toBeInTheDocument()
  })
})
