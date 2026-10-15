import { motion } from 'motion/react'
import type { ReactNode } from 'react'

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

/** Opening of a sub-page: live eyebrow, large h1, lead text and an optional panel below. */
export default function Intro({
  eyebrow,
  title,
  lead,
  children,
}: {
  eyebrow: string
  title: ReactNode
  lead?: ReactNode
  children?: ReactNode
}) {
  return (
    <section className="page-width pb-2 pt-8 sm:pt-12">
      <Reveal>
        <p className="eyebrow text-accent">
          <span className="live-dot" /> {eyebrow}
        </p>
      </Reveal>
      <Reveal delay={0.06}>
        <h1 className="mt-3 text-[38px] leading-[1.06] sm:text-[52px]">{title}</h1>
      </Reveal>
      {lead && (
        <Reveal delay={0.12}>
          <p className="mt-3 max-w-2xl text-[15px] leading-relaxed text-muted">{lead}</p>
        </Reveal>
      )}
      {children && <Reveal delay={0.18}>{children}</Reveal>}
    </section>
  )
}

/** White card that holds the filters or the search form under an {@link Intro}. */
export function Panel({ children }: { children: ReactNode }) {
  return <div className="card mt-7 p-4 sm:p-5">{children}</div>
}
