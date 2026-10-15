import { useId } from 'react'

/**
 * Brand mark: a map pin with radar waves and a signal blip, drawn as SVG so it stays crisp,
 * follows the surrounding text colour and animates without a background image.
 */
export default function LogoMark({ className = 'w-10' }: { className?: string }) {
  const id = useId()
  const grad = `${id}-g`
  const glow = `${id}-glow`
  return (
    <svg
      viewBox="0 0 48 48"
      aria-hidden="true"
      className={`logo-mark shrink-0 ${className}`}
      style={{ aspectRatio: '1' }}
    >
      <defs>
        <linearGradient id={grad} x1="6" y1="40" x2="42" y2="6" gradientUnits="userSpaceOnUse">
          <stop offset="0%" stopColor="var(--color-brand-2)" />
          <stop offset="55%" stopColor="var(--color-brand)" />
          <stop offset="100%" stopColor="var(--color-brand-3)" />
        </linearGradient>
        <radialGradient id={glow}>
          <stop offset="0%" stopColor="var(--color-brand-3)" stopOpacity="0.55" />
          <stop offset="100%" stopColor="var(--color-brand-3)" stopOpacity="0" />
        </radialGradient>
      </defs>
      {/* Radar waves to the left of the pin, pulsing one after the other. */}
      <g fill="none" stroke={`url(#${grad})`} strokeWidth="2.6" strokeLinecap="round">
        <path className="logo-wave" d="M19.25 32.69A13.5 13.5 0 0 1 19.25 9.31" />
        <path className="logo-wave" d="M17.25 36.16A17.5 17.5 0 0 1 17.25 5.84" />
        <path className="logo-wave" d="M15.25 39.62A21.5 21.5 0 0 1 15.25 2.38" />
      </g>
      {/* Signal line and blip. */}
      <path
        d="M34.1 12.9 41.4 6.4"
        stroke={`url(#${grad})`}
        strokeWidth="2.2"
        strokeLinecap="round"
        fill="none"
      />
      <circle cx="43" cy="5" r="5.5" fill={`url(#${glow})`} className="logo-blip-glow" />
      <circle cx="43" cy="5" r="2.4" fill="var(--color-brand-3)" className="logo-blip" />
      {/* Pin: takes the surrounding text colour, with a bright dot inside. */}
      <path
        d="M26 44s11.5-15.1 11.5-23A11.5 11.5 0 1 0 14.5 21c0 7.9 11.5 23 11.5 23Z"
        fill="currentColor"
      />
      <circle cx="26" cy="21" r="4.6" fill={`url(#${grad})`} />
    </svg>
  )
}
