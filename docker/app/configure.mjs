import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { isIP } from 'node:net'
import { fileURLToPath } from 'node:url'
import { writeRuntimeConfig } from '../web/runtime-config.mjs'

export function configuration(env) {
  const origin = new URL(env.PUBLIC_URL || '')
  if (origin.protocol !== 'https:' || origin.pathname !== '/' || origin.search || origin.hash || origin.username || origin.password)
    throw new Error('PUBLIC_URL must be an HTTPS origin without a path, query or credentials.')
  if (!/^[a-zA-Z0-9.-]+$/.test(origin.hostname)) throw new Error('PUBLIC_URL must use a DNS hostname or IPv4 address.')
  const mode = env.TLS_MODE || 'proxy'
  if (!['proxy', 'direct'].includes(mode)) throw new Error('TLS_MODE must be proxy or direct.')
  const proxies = (env.TRUSTED_PROXIES || '').split(',').map(value => value.trim()).filter(Boolean)
  for (const proxy of proxies) {
    const [address, prefix, extra] = proxy.split('/')
    const family = isIP(address)
    if (!family || extra !== undefined || (prefix !== undefined && (!/^\d+$/.test(prefix) || Number(prefix) > (family === 4 ? 32 : 128))))
      throw new Error('TRUSTED_PROXIES must contain comma-separated IP addresses or CIDR networks.')
    if (prefix !== undefined && Number(prefix) === 0) throw new Error('TRUSTED_PROXIES cannot trust every address.')
  }
  if (mode === 'proxy' && !proxies.length) throw new Error('Proxy mode requires explicit TRUSTED_PROXIES.')
  const bodyLimit = env.MAX_UPLOAD_SIZE || '25m'
  if (!/^\d+[kmg]?$/i.test(bodyLimit)) throw new Error('MAX_UPLOAD_SIZE must be an nginx size, for example 25m.')
  const tlsPath = name => {
    const value = env[name]
    if (!value || !/^\/[a-zA-Z0-9_./-]+$/.test(value)) throw new Error(`${name} must be an absolute certificate path.`)
    return value
  }
  const tls = mode === 'direct'
    ? `listen 443 ssl; ssl_certificate ${tlsPath('TLS_CERTIFICATE')}; ssl_certificate_key ${tlsPath('TLS_CERTIFICATE_KEY')}; ssl_protocols TLSv1.2 TLSv1.3;`
    : 'listen 80;'
  const trust = mode === 'proxy' ? proxies.map(value => `set_real_ip_from ${value};`).join('\n') + '\nreal_ip_header X-Forwarded-For; real_ip_recursive on;' : ''
  const geo = proxies.map(value => `${value} 1;`).join('\n')
  const upstream = `proxy_pass http://127.0.0.1:5500; proxy_http_version 1.1;
    proxy_set_header Host ${origin.host}; proxy_set_header X-Forwarded-Host ${origin.host};
    proxy_set_header X-Forwarded-Proto $public_proto; proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Prefix ""; proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade; proxy_read_timeout 3600s; proxy_buffering off;`
  const nginx = `
geo $realip_remote_addr $trusted_proxy { default 0; ${geo} }
map "$trusted_proxy:$http_x_forwarded_proto" $forwarded_proto { default ""; "1:https" https; }
map $http_upgrade $connection_upgrade { default upgrade; "" close; }
map $scheme $public_proto { default $scheme; http $forwarded_proto; }
# Never include the query string: SignalR and invitation links may carry secrets.
log_format bibliotheca_safe '$remote_addr - $remote_user [$time_local] "$request_method $uri $server_protocol" $status $body_bytes_sent';
server {
  ${tls}
  server_name ${origin.hostname};
  ${trust}
  ${mode === 'proxy' ? 'if ($forwarded_proto = "") { return 400; }' : ''}
  if ($host != ${origin.hostname}) { return 421; }
  client_max_body_size ${bodyLimit};
  root /usr/share/nginx/html; index index.html;
  access_log /dev/stdout bibliotheca_safe; error_log /dev/stderr;
  server_tokens off;
  add_header Strict-Transport-Security "max-age=31536000" always;
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy no-referrer always;
  add_header X-Frame-Options DENY always;
  location ~* ^/api/health(/|$) { return 404; }
  location ~* ^/(openapi|scalar)(/|$) { return 404; }
  # Registration is rendered by the API; its script must come from the same backend.
  location = /js/register-invitation.js { ${upstream} }
  location ~ ^/(api|account|connect|\\.well-known|img|openapi|scalar)(/|$) { ${upstream} }
  location /assets/ { expires 1y; try_files $uri =404; }
  location ~ ^/(config\\.js|index\\.html|sw\\.js|registerSW\\.js|manifest\\.webmanifest|remove-legacy-caches\\.js)$ {
    add_header Cache-Control "no-store";
    add_header Strict-Transport-Security "max-age=31536000" always;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy no-referrer always;
    add_header X-Frame-Options DENY always;
    try_files $uri =404;
  }
  location / { try_files $uri $uri/ /index.html; }
}
# Private health listener bypasses public proxy validation but verifies Kestrel through nginx.
server { listen 127.0.0.1:8090; location = /api/health { proxy_pass http://127.0.0.1:5500; proxy_set_header Host ${origin.host}; proxy_set_header X-Forwarded-Proto https; } location / { return 404; } }
${mode === 'direct' ? `server { listen 80; server_name ${origin.hostname}; return 308 https://${origin.host}$request_uri; }` : ''}
`
  return { nginx, runtime: {
    API_URL: origin.origin, OIDC_AUTHORITY: origin.origin,
    OIDC_CLIENT_ID: 'bibliotheca-web', OIDC_SCOPE: 'openid profile email offline_access',
    OIDC_REDIRECT_URI: `${origin.origin}/oidc-callback`, OIDC_POST_LOGOUT_REDIRECT_URI: `${origin.origin}/`,
  } }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const result = configuration(process.env)
  if ((process.env.TLS_MODE || 'proxy') === 'direct') {
    readFileSync(process.env.TLS_CERTIFICATE)
    readFileSync(process.env.TLS_CERTIFICATE_KEY)
  }
  mkdirSync('/app/data', { recursive: true })
  mkdirSync('/app/temp', { recursive: true })
  writeFileSync('/etc/nginx/conf.d/bibliotheca.conf', result.nginx)
  writeRuntimeConfig('/usr/share/nginx/html/config.js', result.runtime)
}
