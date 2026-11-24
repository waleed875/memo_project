/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ["'ITF Qomra Arabic'", "'Qomra'", "'Cairo'", "'Segoe UI'", 'system-ui', '-apple-system', 'Tahoma', 'Arial', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
