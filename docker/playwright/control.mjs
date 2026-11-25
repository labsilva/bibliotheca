import net from 'node:net'

// This is a pair of fixed TCP relays, never a forward/CONNECT proxy. The API
// network has no browser on it, and browser peers cannot select an API target.
export async function startControl({ host = '0.0.0.0', target = 'playwright', ports = [8080, 8081] } = {}) {
  const sockets = new Set()
  const servers = []
  const close = async () => {
    for (const socket of sockets) socket.destroy()
    await Promise.all(servers.map(server => new Promise(resolve => server.close(resolve))))
  }
  try {
    for (const port of ports) {
      const server = net.createServer(client => {
        sockets.add(client)
        const upstream = net.connect({ host: target, port })
        sockets.add(upstream)
        const destroy = () => { client.destroy(); upstream.destroy() }
        for (const socket of [client, upstream]) {
          socket.on('error', destroy)
          socket.on('close', () => { sockets.delete(socket); destroy() })
          socket.setTimeout(60000, destroy)
        }
        client.pipe(upstream).pipe(client)
      })
      server.maxConnections = 32
      server.dropMaxConnection = true
      servers.push(server)
      await new Promise((resolve, reject) => {
        server.once('error', reject)
        server.listen(port, host, resolve)
      })
    }
    return { servers, close }
  } catch (error) { await close(); throw error }
}

if (process.argv[1]?.replaceAll('\\', '/').endsWith('/control.mjs')) {
  const control = await startControl()
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, async () => {
    await control.close()
    process.exit(0)
  })
}
