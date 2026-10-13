import { useEffect, useRef, useState } from 'react'
import { selectClass } from './Field'

/** Search box that keeps the typed text locally and commits it (to the URL) after a short pause. */
export default function SearchInput({
  id,
  value,
  onCommit,
  placeholder,
  className = selectClass,
}: {
  id: string
  value: string
  onCommit: (value: string) => void
  placeholder?: string
  className?: string
}) {
  const [text, setText] = useState(value)
  const committed = useRef(value)
  // The URL changed from outside (link, Back): show it instead of re-applying the stale text.
  useEffect(() => {
    if (value === committed.current) return
    committed.current = value
    setText(value)
  }, [value])
  useEffect(() => {
    if (text === value) return
    const timer = setTimeout(() => {
      committed.current = text
      onCommit(text)
    }, 300)
    return () => clearTimeout(timer)
  }, [text, value, onCommit])
  return (
    <input
      id={id}
      type="search"
      value={text}
      onChange={(e) => setText(e.target.value)}
      placeholder={placeholder}
      className={className}
    />
  )
}
