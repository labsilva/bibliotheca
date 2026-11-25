import { setting } from './runtimeConfig.js'

function pickBase() {
  const isDev = import.meta.env.DEV

  // Lê runtime config (se existir)
  const runtime = setting('API_URL', undefined)
  
  // Lê env de build do Vite (se definiste)
  const build = import.meta.env.VITE_API_BASE_URL || import.meta.env.VITE_API_URL || ''

  // DEV: prioriza env/proxy do Vite; PROD: prioriza runtime (config.js)
  const chosen = isDev ? (build || runtime) : (runtime ?? build)

  return chosen
}

function normalize(url) {
  if (!url) return ''
  // remove barra final para evitar // nas requests
  return url.replace(/\/+$/, '')
}

let cached // opcional: cache para não recomputar sempre

export function getApiBase() {
  if (cached !== undefined) return cached
  const base = normalize(pickBase())
  if (!base || !/^https?:\/|^\//i.test(base)) {
    // Aviso útil: sem base absoluta ou path base -> as chamadas serão relativas
    console.warn('[API] Base URL não definida/absoluta; chamadas ficarão relativas ao host atual:', base)
  }
  cached = base
  return cached
}
