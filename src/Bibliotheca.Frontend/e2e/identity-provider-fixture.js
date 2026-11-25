const idToken = [
  Buffer.from('{"alg":"RS256"}').toString('base64url'),
  Buffer.from('{"sub":"first"}').toString('base64url'),
  'fixture',
].join('.')

export async function handleIdentityProvider(req, res, url, json) {
  const origin = url.origin
  switch (url.pathname) {
    case '/.well-known/openid-configuration':
      return json(res, {
        issuer: origin, authorization_endpoint: origin + '/connect/authorize', token_endpoint: origin + '/connect/token',
        userinfo_endpoint: origin + '/connect/userinfo', end_session_endpoint: origin + '/connect/logout',
      })
    case '/connect/authorize': {
      if (!url.searchParams.get('scope')?.split(' ').includes('offline_access') ||
          url.searchParams.get('code_challenge_method') !== 'S256') return json(res, { error: 'invalid_request' }, 400)
      if (!req.headers.cookie?.includes('fixture_session=first')) return redirect(res, '/account/login' + url.search)
      const callback = new URL('/oidc-callback', origin)
      callback.searchParams.set('code', 'fixture-code')
      callback.searchParams.set('state', url.searchParams.get('state'))
      return redirect(res, callback.href)
    }
    case '/account/login':
      if (req.method === 'POST') {
        const form = await readForm(req)
        const expires = form.has('rememberMe') ? '; Max-Age=1209600' : ''
        return redirect(res, '/connect/authorize' + url.search, 'fixture_session=first; Path=/; HttpOnly; SameSite=Lax' + expires)
      }
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' })
      return res.end(`<!doctype html><form method="post">
        <label><input type="checkbox" name="rememberMe"> Manter sessão iniciada</label>
        <button>Entrar</button>
      </form>`)
    case '/connect/token': {
      const tokens = await readForm(req)
      if (tokens.get('grant_type') === 'refresh_token' ? tokens.get('refresh_token') !== 'fixture-refresh' : !tokens.get('code_verifier'))
        return json(res, { error: 'invalid_grant' }, 400)
      return json(res, {
        access_token: 'first-token', token_type: 'Bearer', refresh_token: 'fixture-refresh', expires_in: 900,
        id_token: idToken, scope: 'openid profile email offline_access',
      })
    }
    case '/connect/userinfo':
      return json(res, { sub: 'first', name: 'First Reader' })
    case '/connect/logout':
      return redirect(res, '/logout-callback', 'fixture_session=; Path=/; Max-Age=0')
    default:
      return json(res, {}, 404)
  }
}

async function readForm(req) {
  const chunks = []
  for await (const chunk of req) chunks.push(chunk)
  return new URLSearchParams(Buffer.concat(chunks).toString())
}

function redirect(res, location, cookie) {
  if (cookie) res.setHeader('set-cookie', cookie)
  res.writeHead(303, { location, 'cache-control': 'no-store' })
  res.end()
}
