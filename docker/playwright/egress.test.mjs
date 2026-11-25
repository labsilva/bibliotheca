import test from 'node:test'
import assert from 'node:assert/strict'
import net from 'node:net'
import { once } from 'node:events'
import { destination, publicIPv4, startEgress } from './egress.mjs'

test('rejects loopback, private, metadata, link-local, transition and reserved addresses', () => {
  for (const ip of ['127.0.0.1', '10.1.2.3', '172.16.0.2', '192.168.1.1', '169.254.169.254', '100.64.0.1', '0.0.0.0', '224.0.0.1', '198.18.0.1', '::1', '::ffff:127.0.0.1'])
    assert.equal(publicIPv4(ip), false, ip)
  assert.equal(publicIPv4('8.8.8.8'), true)
})

async function tunnel(proxy) {
  const client = net.connect({ host: '127.0.0.1', port: proxy.address().port })
  await once(client, 'connect')
  client.write('CONNECT www.wook.pt:443 HTTP/1.1\r\nHost: www.wook.pt:443\r\n\r\n')
  return client
}

test('bounds pending CONNECT work and rejects private DNS answers at the wire', async () => {
  let finish
  let started
  const resolving = new Promise(resolve => { started = resolve })
  const proxy = await startEgress('127.0.0.1', { port: 0, maxConnections: 1,
    resolve: () => { started(); return new Promise(resolve => { finish = resolve }) },
    connect: () => assert.fail('Private DNS answer must never be connected') })
  let first, second
  try {
    first = await tunnel(proxy)
    await resolving
    second = await tunnel(proxy)
    assert.match(String((await once(second, 'data'))[0]), /^HTTP\/1.1 503 /)
    finish([{ address: '127.0.0.1' }])
    assert.match(String((await once(first, 'data'))[0]), /^HTTP\/1.1 403 /)
  } finally {
    first?.destroy()
    second?.destroy()
    await new Promise(resolve => proxy.close(resolve))
  }
})

test('does not connect after a pending DNS lookup outlives its client', async () => {
  let finish
  let started
  const resolving = new Promise(resolve => { started = resolve })
  const proxy = await startEgress('127.0.0.1', { port: 0, timeout: 100, maxConnections: 1,
    resolve: () => { started(); return new Promise(resolve => { finish = resolve }) },
    connect: () => assert.fail('Expired CONNECT must not open an upstream socket') })
  const client = await tunnel(proxy)
  try {
    await resolving
    await once(client, 'close')
    const excess = await tunnel(proxy)
    try {
      assert.match(String((await once(excess, 'data'))[0]), /^HTTP\/1.1 503 /,
        'An unresolved lookup still consumes capacity after its client expires')
    } finally { excess.destroy() }
    finish([{ address: '8.8.8.8' }])
    await new Promise(resolve => setImmediate(resolve))
  } finally {
    client.destroy()
    await new Promise(resolve => proxy.close(resolve))
  }
})

test('pins validated DNS results and rejects mixed public/private DNS answers', async () => {
  const resolve = async () => [{ address: '8.8.8.8' }]
  assert.equal(await destination('www.wook.pt', 443, resolve), '8.8.8.8')
  await assert.rejects(destination('www.wook.pt', 443, async () => [{ address: '8.8.8.8' }, { address: '10.0.0.1' }]))
  for (const [host, port] of [['127.0.0.1', 443], ['metadata.internal', 80], ['localhost', 443]])
    await assert.rejects(destination(host, port, async () => [{ address: '127.0.0.1' }] ))
})
