import { useEffect, useState } from 'react'
import { selectClass } from './Field'

/** Search box that keeps the typed text locally and commits it (to the URL) after a short pause. */
export default function SearchInput({
  id,
  value,
  onCommit,
}: {
  id: string
  value: string
  onCommit: (value: string) => void
}) {
  const [text, setText] = useState(value)
  useEffect(() => {
    if (text === value) return
    const timer = setTimeout(() => onCommit(text), 300)
    return () => clearTimeout(timer)
  }, [text, value, onCommit])
  return (
    <input
      id={id}
      type="search"
      value={text}
      onChange={(e) => setText(e.target.value)}
      className={selectClass}
    />
  )
}
