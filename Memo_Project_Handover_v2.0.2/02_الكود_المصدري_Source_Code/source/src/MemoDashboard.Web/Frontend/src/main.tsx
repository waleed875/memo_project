import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Chart as ChartJS } from 'chart.js'
import './index.css'
import App from './App'
import { LanguageProvider } from './i18n/LanguageProvider'
import { ThemeProvider } from './theme/ThemeProvider'
import { AuthProvider } from './context/AuthContext'

ChartJS.defaults.font.family = "'ITF Qomra Arabic', 'Qomra', 'Cairo', 'Segoe UI', sans-serif"

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <LanguageProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </LanguageProvider>
    </ThemeProvider>
  </StrictMode>,
)