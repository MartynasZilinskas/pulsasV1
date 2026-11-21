import { useEffect, useState } from 'react'

export type Appearance = 'system' | 'light' | 'dark'

const storageKey = 'pulsas.appearance'

function parseAppearance(value: string | null | undefined): Appearance {
  return value === 'light' || value === 'dark' ? value : 'system'
}

function applyAppearance(appearance: Appearance) {
  const root = document.documentElement
  root.dataset.appearance = appearance
  root.dataset.theme = appearance === 'system'
    ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : appearance
  root.style.colorScheme = root.dataset.theme
}

export function useAppearance() {
  const [appearance, setPreference] = useState<Appearance>(() => parseAppearance(document.documentElement.dataset.appearance))

  useEffect(() => {
    const media = matchMedia('(prefers-color-scheme: dark)')
    const onSystemChange = () => applyAppearance(appearance)
    const onStorage = (event: StorageEvent) => {
      if (event.storageArea === localStorage && (event.key === storageKey || event.key === null)) {
        const next = parseAppearance(event.newValue)
        applyAppearance(next)
        setPreference(next)
      }
    }
    applyAppearance(appearance)
    media.addEventListener('change', onSystemChange)
    window.addEventListener('storage', onStorage)
    return () => {
      media.removeEventListener('change', onSystemChange)
      window.removeEventListener('storage', onStorage)
    }
  }, [appearance])

  function setAppearance(next: Appearance) {
    applyAppearance(next)
    setPreference(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // Appearance remains usable when browser storage is unavailable.
    }
  }

  return { appearance, setAppearance }
}
