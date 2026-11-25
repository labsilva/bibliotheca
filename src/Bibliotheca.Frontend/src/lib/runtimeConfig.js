// These public deployment settings are generated on every container start.
export function setting(name, fallback) {
  const value = typeof window !== 'undefined' ? window.__APP_CONFIG__?.[name] : undefined
  return typeof value === 'string' && !value.startsWith('__') ? value : fallback
}
