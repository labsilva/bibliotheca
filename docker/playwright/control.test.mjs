import test from 'node:test'
import assert from 'node:assert/strict'
import net from 'node:net'
import { once } from 'node:events'
import { startControl } from './control.mjs'

test('relays bytes only to its fixed upstream and closes active sockets on shutdown', async () => {
  const received = []
  const upstream = net.createServer(socket => {
    socket.on('data', data => { received.push(String(data)); socket.write(data) })
  })
  upstream.listen(0, '127.0.0.1')
  await once(upstream, 'listening')
  const port = upstream.address().port
  const control = await startControl({ host: '127.0.0.2', target: '127.0.0.1', ports: [port] })
  const client = net.connect({ host: '127.0.0.2', port })
  try {
    await once(client, 'connect')
    const reply = once(client, 'data')
    const payload = 'CONNECT api:5500 HTTP/1.1\r\n\r\n'
    client.write(payload)
    assert.equal(String((await reply)[0]), payload)
    assert.deepEqual(received, [payload])
    const closed = once(client, 'close')
    await control.close()
    await closed
  } finally {
    client.destroy()
    await control.close()
    await new Promise(resolve => upstream.close(resolve))
  }
})
