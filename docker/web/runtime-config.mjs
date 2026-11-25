import { writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

export function runtimeConfig(env) {
  const config = {}
  if (env.API_URL !== undefined) config.API_URL = env.API_URL
  for (const name of ['AUTHORITY', 'CLIENT_ID', 'SCOPE', 'REDIRECT_URI', 'POST_LOGOUT_REDIRECT_URI']) {
    const value = env[`OIDC_${name}`] ?? env[`VUE_APP_VITE_OIDC_${name}`]
    if (value !== undefined && value !== '') config[`OIDC_${name}`] = value
  }
  return config
}

export function writeRuntimeConfig(path, config) {
  writeFileSync(path, `window.__APP_CONFIG__ = ${JSON.stringify(config).replace(/</g, '\\u003c')};\n`)
}

if (process.argv[1] === fileURLToPath(import.meta.url) && process.argv[2])
  writeRuntimeConfig(process.argv[2], runtimeConfig(process.env))
