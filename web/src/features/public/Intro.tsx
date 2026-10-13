import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { container } from './PublicLayout'

/** Fades and lifts its content into place; `delay` staggers siblings. */
export function Reveal({
  children,
  delay = 0,
  className,
}: {
  children: ReactNode
  delay?: number
  className?: string
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1], delay }}
      className={className}
    >
      {children}
    </motion.div>
  )
}

/** Page opening: live eyebrow, large h1, lead text, optional right-hand note and content below. */
export default function Intro({
  eyebrow,
  title,
  lead,
  aside,
  children,
}: {
  eyebrow: string
  title: ReactNode
  lead?: ReactNode
  aside?: ReactNode
  children?: ReactNode
}) {
  return (
    <section className={`${container} pb-6 pt-7 sm:pb-7 sm:pt-9`}>
      <div className="grid grid-cols-[minmax(0,1fr)_auto] items-end gap-5">
        <div className="min-w-0">
          <Reveal>
            <p className="eyebrow">
              <span className="live-dot" /> {eyebrow}
            </p>
          </Reveal>
          <Reveal delay={0.06}>
            <h1 className="mt-2 text-[38px] leading-[1.15] sm:text-[54px]">{title}</h1>
          </Reveal>
          {lead && (
            <Reveal delay={0.12}>
              <p className="mt-2.5 max-w-[750px] text-[13px] leading-[1.8] text-muted sm:text-sm">
                {lead}
              </p>
            </Reveal>
          )}
        </div>
        {aside && (
          <Reveal delay={0.12} className="hidden pb-1 lg:block">
            {aside}
          </Reveal>
        )}
      </div>
      {children && <Reveal delay={0.18}>{children}</Reveal>}
    </section>
  )
}

/** Bordered panel used for filters and the search form. */
export function Panel({ children }: { children: ReactNode }) {
  return <div className="mt-6 rounded-md border border-line bg-surface p-4 sm:p-5">{children}</div>
}
