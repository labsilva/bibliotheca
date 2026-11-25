import { randomBytes } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { lstat, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { isIPv4 } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { createInterface } from 'node:readline'
import { stdin, stdout } from 'node:process'
import { configuration } from './docker/app/configure.mjs'

const root = new URL('./', import.meta.url)
const template = name => readFile(new URL('docker/app/' + name, root), 'utf8')
const target = name => new URL(name, root)
const purposes = { signing: 'digitalSignature', encryption: 'keyEncipherment' }
const files = ['.env', 'appsettings.json', ...Object.keys(purposes).flatMap(purpose =>
  ['cert/' + purpose + '.pfx', 'secrets/oidc-' + purpose + '-password'])]

async function exists(path) {
  try {
    return await lstat(path)
  } catch (error) {
    if (error.code !== 'ENOENT') throw error
    return null
  }
}

function openssl(args) {
  const result = spawnSync('openssl', args, { stdio: ['ignore', 'ignore', 'pipe'] })
  if (result.error?.code === 'ENOENT') throw new Error('Install OpenSSL and make sure openssl is on PATH.')
  if (result.error || result.status !== 0) {
    throw new Error('OpenSSL failed. Check that your installation supports req and pkcs12.')
  }
}

async function ask(question, fallback = '', accept = Boolean) {
  for (;;) {
    stdout.write(question + (fallback ? ' [' + fallback + ']' : '') + ': ')
    const result = await lines.next()
    if (result.done) throw new Error('Setup cancelled before completion.')
    const value = result.value.trim() || fallback
    if (accept(value)) return value
    console.log('Invalid value. Please try again.')
  }
}

async function setup() {
  for (const name of files) {
    if (await exists(target(name))) throw new Error(name + ' already exists. Setup is for new installations.')
  }
  for (const name of ['data', 'temp', 'cert', 'secrets']) {
    const info = await exists(target(name))
    if (info && !info.isDirectory()) throw new Error(name + ' must be a local directory.')
    if (name === 'data' && info && (await readdir(target(name))).length)
      throw new Error('The data directory is not empty. Use the upgrade instructions in docs/deployment.md.')
  }
  openssl(['version'])

  const origin = await ask('Public HTTPS origin (example https://books.example.org)', '', value => {
    try {
      configuration({ PUBLIC_URL: value, TRUSTED_PROXIES: '127.0.0.1' })
      return true
    } catch {
      return false
    }
  })
  const mode = await ask('TLS mode (proxy or direct)', 'proxy', value => ['proxy', 'direct'].includes(value))
  const proxies = mode === 'proxy' ? await ask('Connecting proxy IPs or CIDRs, comma-separated', '', value => {
    try {
      configuration({ PUBLIC_URL: origin, TRUSTED_PROXIES: value })
      return true
    } catch {
      return false
    }
  }) : ''
  const address = await ask('Docker host bind address (IPv4)', mode === 'proxy' ? '127.0.0.1' : '0.0.0.0', isIPv4)
  const validPort = value => /^\d+$/.test(value) && Number(value) >= 1 && Number(value) <= 65535
  const httpPort = await ask('Host HTTP port', mode === 'proxy' ? '8080' : '80', validPort)
  const httpsPort = mode === 'direct'
    ? await ask('Host HTTPS port', new URL(origin).port || '443', value => validPort(value) && Number(value) !== Number(httpPort))
    : Number(httpPort) === 8443 ? '8444' : '8443'
  const image = await ask('Docker image (default builds from this checkout)', 'bibliotheca-app:latest',
    value => /^[a-zA-Z0-9][a-zA-Z0-9._/:@-]*$/.test(value))

  const stage = await mkdtemp(join(tmpdir(), 'bibliotheca-setup-'))
  const published = []
  try {
    const settings = JSON.parse(await template('appsettings.example.json'))
    settings.Authentication = { ...settings.Authentication, BootstrapCode: randomBytes(32).toString('hex') }
    const values = {
      PUBLIC_URL: new URL(origin).origin, TLS_MODE: mode, TRUSTED_PROXIES: proxies.replace(/\s/g, ''),
      APP_BIND_ADDRESS: address, APP_HTTP_PORT: httpPort, APP_HTTPS_PORT: httpsPort, APP_IMAGE: image,
    }
    const env = (await template('env.example')).replace(/^([A-Z_]+)=.*$/gm,
      (line, key) => Object.hasOwn(values, key) ? key + '=' + values[key] : line)
    await writeFile(join(stage, 'env'), env, { mode: 0o600 })
    await writeFile(join(stage, 'settings'), JSON.stringify(settings, null, 2) + '\n', { mode: 0o600 })
    for (const [purpose, usage] of Object.entries(purposes)) {
      const key = join(stage, purpose + '.key')
      const certificate = join(stage, purpose + '.pem')
      const password = join(stage, purpose + '.password')
      await writeFile(password, randomBytes(32).toString('hex') + '\n', { mode: 0o600 })
      openssl(['req', '-x509', '-newkey', 'rsa:3072', '-nodes', '-sha256', '-days', '730',
        '-subj', '/CN=Bibliotheca OIDC ' + purpose, '-addext', 'basicConstraints=critical,CA:FALSE',
        '-addext', 'keyUsage=critical,' + usage, '-keyout', key, '-out', certificate])
      openssl(['pkcs12', '-export', '-inkey', key, '-in', certificate,
        '-out', join(stage, purpose + '.pfx'), '-passout', 'file:' + password])
    }
    for (const name of ['data', 'temp', 'cert', 'secrets', ...(mode === 'direct' ? ['cert/tls'] : [])])
      await mkdir(target(name), { recursive: true, mode: 0o700 })
    for (const [from, to] of [
      ['settings', 'appsettings.json'],
      ...Object.keys(purposes).flatMap(purpose => [
        [purpose + '.pfx', 'cert/' + purpose + '.pfx'],
        [purpose + '.password', 'secrets/oidc-' + purpose + '-password'],
      ]),
      ['env', '.env'],
    ]) {
      await writeFile(target(to), await readFile(join(stage, from)), { flag: 'wx', mode: 0o600 })
      published.push(to)
    }
  } catch (error) {
    for (const name of published.reverse()) await rm(target(name), { force: true })
    throw error
  } finally {
    await rm(stage, { recursive: true, force: true })
  }

  console.log('\nCreated .env, appsettings.json, OIDC certificates and password files.')
  if (mode === 'direct') console.log('Place your HTTPS certificate and key at cert/tls/fullchain.pem and cert/tls/privkey.pem.')
  else console.log('Configure the reverse proxy with the HTTPS origin, forwarding headers and WebSockets.')
  console.log('Apply the host ownership and permission instructions in docs/deployment.md before starting Docker.')
  console.log('Run docker compose -f docker-compose.app.yml ' + (image === 'bibliotheca-app:latest' ? 'build' : 'pull') + '.')
  console.log('Then run docker compose -f docker-compose.app.yml up -d.')
  console.log('Use Authentication.BootstrapCode from appsettings.json to register the first account, then remove it.')
  console.log('Assign the administrator ID as described in docs/deployment.md.')
}

const input = createInterface({ input: stdin, crlfDelay: Infinity })
const lines = input[Symbol.asyncIterator]()
try {
  await setup()
} catch (error) {
  console.error(error.message)
  process.exitCode = 1
} finally {
  input.close()
}
