/** Animated radar mark (logo and the "in focus" picture); decorative. */
export default function RadarSignal({ className = 'w-10' }: { className?: string }) {
  return (
    <span aria-hidden="true" className={`radar-signal shrink-0 ${className}`}>
      <span className="radar-ring" />
      <span className="radar-ring ring-three" />
      <span className="radar-cross" />
      <span className="radar-sweep" />
      <span className="radar-target target-one" />
      <span className="radar-target target-two" />
      <span className="radar-center" />
    </span>
  )
}
