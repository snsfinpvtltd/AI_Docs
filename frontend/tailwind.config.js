/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      // ── Clinevo Technologies brand colors ─────────────────────────────────
      colors: {
        brand: {
          blue:    '#0072ff',   // gradient start
          purple:  '#8200f4',   // gradient mid
          coral:   '#ff6c5f',   // gradient end
          cyan:    '#1EB5C7',   // secondary CTA
          teal:    '#12E58D',   // success accent
          dark:    '#212529',   // sidebar / card dark bg
          darker:  '#1a1d20',   // sidebar hover
          charcoal:'#495057',   // secondary text
          muted:   '#6c757d',   // placeholder / subtle text
          border:  '#e9ecef',   // dividers
          surface: '#f8f9fa',   // page background
        },
      },

      // ── Typography: Poppins headings + Manrope body (loaded from Google Fonts)
      fontFamily: {
        sans:    ['Manrope', 'ui-sans-serif', 'system-ui', 'sans-serif'],
        heading: ['Poppins', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },

      // ── Gradient utilities ─────────────────────────────────────────────────
      backgroundImage: {
        'brand-gradient':   'linear-gradient(135deg, #0072ff 0%, #8200f4 50%, #ff6c5f 100%)',
        'brand-gradient-h': 'linear-gradient(90deg,  #0072ff 0%, #8200f4 50%, #ff6c5f 100%)',
        'brand-gradient-v': 'linear-gradient(180deg, #0072ff 0%, #8200f4 50%, #ff6c5f 100%)',
        'brand-subtle':     'linear-gradient(135deg, rgba(0,114,255,0.08) 0%, rgba(130,0,244,0.06) 50%, rgba(255,108,95,0.04) 100%)',
      },

      // ── Shadows matching Clinevo design ───────────────────────────────────
      boxShadow: {
        'brand':    '0 4px 8px rgba(0, 0, 0, 0.12)',
        'brand-lg': '0 0 40px -10px rgba(0, 0, 0, 0.25)',
        'brand-xl': '12px 12px 50px rgba(0, 0, 0, 0.40)',
        'card':     '0 2px 5px rgba(0, 0, 0, 0.08)',
        'card-hover':'0 8px 16px rgba(0, 0, 0, 0.15)',
      },

      // ── Border radius matching Clinevo 5–9px style ────────────────────────
      borderRadius: {
        brand: '9px',
      },
    },
  },
  plugins: [],
}
