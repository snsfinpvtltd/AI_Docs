import { type ReactElement } from 'react'

interface ClinevoLogoProps {
  /** full = image at full size; compact = image constrained to sidebar/topbar; icon = square crop */
  variant?: 'full' | 'compact' | 'icon'
  className?: string
}

export default function ClinevoLogo({ variant = 'full', className = '' }: ClinevoLogoProps): ReactElement {
  if (variant === 'icon') {
    return (
      <img
        src="/clinevo-logo.jpg"
        alt="Clinevo Technologies"
        className={`object-contain rounded-lg bg-white ${className || 'w-10 h-10'}`}
      />
    )
  }

  if (variant === 'compact') {
    return (
      <img
        src="/clinevo-logo.jpg"
        alt="Clinevo Technologies"
        className={`object-contain ${className || 'h-9'}`}
      />
    )
  }

  // full
  return (
    <img
      src="/clinevo-logo.jpg"
      alt="Clinevo Technologies"
      className={`object-contain ${className || 'h-12'}`}
    />
  )
}
