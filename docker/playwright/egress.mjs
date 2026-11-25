import http from 'node:http'
import net from 'node:net'
import { lookup } from 'node:dns/promises'

// Resolve once, validate every returned address, then connect to that exact IP.
// IPv6 is denied deliberately, including IPv4-mapped and transition addresses.
export function publicIPv4(address) {
  if (net.isIP(address) !== 4) return false
  const [a, b] = address.split('.').map(Number)
  return !(a === 0 || a === 10 || a === 127 || a >= 224 ||
    (a === 100 && b >= 64 && b <= 127) || (a === 169 && b === 254) ||
    (a === 172 && b >= 16 && b <= 31) || (a === 192 && [0, 168].includes(b)) ||
    (a === 198 && [18, 19, 51].includes(b)) || (a === 203 && b === 0))
}

export async function destination(host, port, resolve = lookup) {
  if (Number(port) !== 443 || net.isIP(host) || !/^[a-z0-9.-]+$/i.test(host)) throw new Error('Forbidden destination')
  const addresses = await resolve(host, { all: true, family: 4 })
  if (!addresses.length || addresses.some(({ address }) => !publicIPv4(address))) throw new Error('Forbidden address')
  return addresses[0].address
}

export function startEgress(host = '127.0.0.1', { port = 8082, maxConnections = 32, timeout = 60000, resolve = lookup, connect = net.connect } = {}) {
  const proxy = http.createServer({ maxHeaderSize: 8192, headersTimeout: 10000, requestTimeout: 10000 },
    (request, response) => response.writeHead(403).end())
  // Count pending DNS resolutions too: a browser cannot create an unbounded
  // number of sockets/resolutions by opening CONNECT requests concurrently.
  const clients = new Set()
  let resolving = 0
  proxy.maxConnections = maxConnections * 2
  proxy.dropMaxConnection = true
  proxy.on('connect', async (request, client, head) => {
    if (clients.size >= maxConnections || resolving >= maxConnections) {
      client.end('HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\n\r\n')
      return
    }
    clients.add(client)
    let upstream
    client.on('error', () => upstream?.destroy())
    client.on('close', () => {
      clients.delete(client)
      upstream?.destroy()
    })
    // Socket inactivity alone does not bound DNS work: a peer can keep sending
    // bytes. Enforce a separate deadline until the upstream is connected.
    const pending = setTimeout(() => client.destroy(), timeout)
    client.once('close', () => clearTimeout(pending))
    client.setTimeout(timeout, () => client.destroy())
    try {
      const target = new URL(`https://${request.url}`)
      if (target.username || target.password || target.pathname !== '/' || target.search || target.hash) throw new Error('Invalid target')
      let address
      resolving++
      try { address = await destination(target.hostname, target.port || 443, resolve) }
      finally { resolving-- }
      if (client.destroyed) return
      upstream = connect({ host: address, port: 443 })
      upstream.setTimeout(timeout, () => upstream.destroy())
      upstream.on('error', () => client.destroy())
      upstream.once('connect', () => {
        clearTimeout(pending)
        client.write('HTTP/1.1 200 Connection Established\r\n\r\n')
        if (head.length) upstream.write(head)
        client.pipe(upstream).pipe(client)
      })
    } catch {
      clearTimeout(pending)
      client.end('HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n')
    }
  })
  // http.Server.close does not close upgraded CONNECT sockets by itself.
  proxy.on('close', () => { for (const client of clients) client.destroy() })
  return new Promise((resolve, reject) => {
    proxy.once('error', reject)
    proxy.listen(port, host, () => resolve(proxy))
  })
}

if (process.argv[1]?.endsWith('/egress.mjs')) await startEgress('0.0.0.0')
