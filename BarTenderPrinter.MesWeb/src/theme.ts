import { createTheme } from '@mui/material/styles'

export const theme = createTheme({
  cssVariables: true,
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#0f766e' },
        secondary: { main: '#334155' },
        background: { default: '#f3f6f8', paper: '#ffffff' },
        success: { main: '#15803d' },
        warning: { main: '#b45309' },
        error: { main: '#b91c1c' },
      },
    },
    dark: {
      palette: {
        primary: { main: '#5eead4' },
        secondary: { main: '#cbd5e1' },
        background: { default: '#08111e', paper: '#101c2c' },
        success: { main: '#4ade80' },
        warning: { main: '#fbbf24' },
        error: { main: '#f87171' },
      },
    },
  },
  typography: {
    fontFamily: '"Fira Sans", "Microsoft YaHei UI", "PingFang SC", sans-serif',
    h1: { fontSize: 'clamp(1.75rem, 3vw, 2.5rem)', fontWeight: 700, letterSpacing: '-0.03em' },
    h2: { fontSize: '1.35rem', fontWeight: 700 },
    h3: { fontSize: '1rem', fontWeight: 700 },
    button: { fontWeight: 700, textTransform: 'none' },
  },
  shape: { borderRadius: 10 },
  spacing: 8,
  components: {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: { root: { minHeight: 42, borderRadius: 8 } },
    },
    MuiCard: {
      styleOverrides: {
        root: {
          border: '1px solid var(--mui-palette-divider)',
          boxShadow: '0 10px 30px rgba(15, 23, 42, 0.06)',
        },
      },
    },
    MuiChip: { styleOverrides: { root: { fontWeight: 700 } } },
    MuiTextField: { defaultProps: { size: 'small' } },
    MuiTableCell: { styleOverrides: { head: { fontWeight: 700 } } },
  },
})
