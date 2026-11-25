import test from 'node:test'
import assert from 'node:assert/strict'
import { configuration } from './configure.mjs'
import { runtimeConfig, writeRuntimeConfig } from '../web/runtime-config.mjs'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import vm from 'node:vm'

const proxy = { PUBLIC_URL: 'https://books.example.org:8443', TRUSTED_PROXIES: '172.30.0.2,fd00::/64' }
test('proxy config requires explicit trust and an HTTPS origin', () => {
  for (const env of [{}, { ...proxy, PUBLIC_URL: 'http://books.example.org' }, { ...proxy, PUBLIC_URL: 'https://books.example.org/subpath' }, { ...proxy, TRUSTED_PROXIES: '' }, { ...proxy, TRUSTED_PROXIES: '0.0.0.0; return 200' }, { ...proxy, TRUSTED_PROXIES: '10.0.0.1/33' }])
    assert.throws(() => configuration(env))
})
test('proxy headers are normalized and OIDC uses the public origin including port', () => {
  const { nginx, runtime } = configuration(proxy)
  assert.match(nginx, /"1:https" https/)
  assert.match(nginx, /if \(\$forwarded_proto = ""\)/)
  assert.match(nginx, /set_real_ip_from 172\.30\.0\.2/)
  assert.match(nginx, /proxy_set_header Host books\.example\.org:8443/)
  assert.match(nginx, /proxy_set_header Connection \$connection_upgrade/)
  assert.match(nginx, /location = \/js\/register-invitation\.js \{ proxy_pass http:\/\/127\.0\.0\.1:5500;/)
  assert.equal(runtime.OIDC_AUTHORITY, 'https://books.example.org:8443')
  assert.equal(runtime.OIDC_REDIRECT_URI, 'https://books.example.org:8443/oidc-callback')
})
test('access logs exclude request query strings that can contain credentials', () => {
  const { nginx } = configuration(proxy)
  const format = nginx.match(/log_format bibliotheca_safe ([^;]+);/)
  assert.ok(format)
  assert.match(format[1], /\$uri/)
  assert.doesNotMatch(format[1], /\$(?:request_uri|request|args|query_string)\b/)
  assert.match(nginx, /access_log \/dev\/stdout bibliotheca_safe;/)
})
test('direct TLS requires certificate paths, ignores forwarded protocol and redirects to public port', () => {
  const env = { PUBLIC_URL: proxy.PUBLIC_URL, TLS_MODE: 'direct', TLS_CERTIFICATE: '/cert/site.pem', TLS_CERTIFICATE_KEY: '/cert/site.key' }
  const { nginx } = configuration(env)
  assert.match(nginx, /listen 443 ssl/)
  assert.match(nginx, /return 308 https:\/\/books\.example\.org:8443\$request_uri/)
  assert.doesNotMatch(nginx, /set_real_ip_from/)
  assert.throws(() => configuration({ ...env, TLS_CERTIFICATE: '/cert/site.pem; inject' }))
})
test('runtime configuration safely serializes values and accepts legacy OIDC aliases', () => {
  const path = mkdtempSync(join(tmpdir(), 'bibliotheca-config-'))
  try {
    const config = runtimeConfig({ API_URL: 'https://example.org/a"\n</script>', VUE_APP_VITE_OIDC_AUTHORITY: 'https://issuer.example.org' })
    const output = join(path, 'config.js')
    writeRuntimeConfig(output, config)
    const source = readFileSync(output, 'utf8')
    assert.doesNotMatch(source, /<\/script>/)
    const context = { window: {} }
    vm.runInNewContext(source, context)
    assert.equal(context.window.__APP_CONFIG__.API_URL, config.API_URL)
    assert.equal(context.window.__APP_CONFIG__.OIDC_AUTHORITY, 'https://issuer.example.org')
    writeRuntimeConfig(output, runtimeConfig({ API_URL: 'https://other.example.org' }))
    assert.doesNotMatch(readFileSync(output, 'utf8'), /issuer.example.org/)
  } finally { rmSync(path, { recursive: true, force: true }) }
})

test('public endpoints hide internal readiness and development tooling, and set security headers', () => {
  const { nginx } = configuration(proxy)
  assert.match(nginx, /location ~\* \^\/api\/health\(\/\|\$\) \{ return 404; \}/)
  assert.match(nginx, /location ~\* \^\/\(openapi\|scalar\)/)
  assert.match(nginx, /listen 127\.0\.0\.1:8090/)
  for (const header of ['Strict-Transport-Security', 'X-Content-Type-Options', 'Referrer-Policy', 'X-Frame-Options'])
    assert.match(nginx, new RegExp(`add_header ${header} .* always;`))
  assert.throws(() => configuration({ ...proxy, TRUSTED_PROXIES: '0.0.0.0/0' }))
  assert.throws(() => configuration({ ...proxy, PUBLIC_URL: 'https://example.org%0a.evil' }))
})
