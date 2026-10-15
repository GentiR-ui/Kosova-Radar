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
      initial={{ opacity: 0, y: 14 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.45, ease: [0.16, 1, 0.3, 1], delay }}
      className={className}
    >
      {children}
    </motion.div>
  )
}

/** Opening of a sub-page: h1, lead text and an optional filter panel below. */
export default function Intro({
  eyebrow,
  title,
  lead,
  children,
}: {
  eyebrow?: string
  title: ReactNode
  lead?: ReactNode
  children?: ReactNode
}) {
  return (
    <section className="page-width pt-8 sm:pt-10">
      {eyebrow && (
        <Reveal>
          <p className="badge badge-soft mb-3">{eyebrow}</p>
        </Reveal>
      )}
      <Reveal delay={0.04}>
        <h1 className="text-[40px] leading-[1.05] sm:text-5xl">{title}</h1>
      </Reveal>
      {lead && (
        <Reveal delay={0.1}>
          <p className="mt-3 max-w-2xl text-[15px] leading-relaxed text-muted">{lead}</p>
        </Reveal>
      )}
      {children && <Reveal delay={0.16}>{children}</Reveal>}
    </section>
  )
}

/** Card that holds the filters or the search form under an {@link Intro}. */
export function Panel({ children }: { children: ReactNode }) {
  return <div className="card mt-6 p-4 sm:p-5">{children}</div>
}
