import { getUser, login } from './oidc'

const PUBLIC_PATHS = new Set(['/oidc-callback', '/logout-callback', '/oidc-silent-renew', '/apple-icon-180.png'])

// flag simples para evitar múltiplos redirects simultâneos
let redirecting = false

export function installAuthGuard(router) {
  router.beforeEach(async (to) => {
    // 1) Rotas públicas/directas
    if (to.meta?.public || PUBLIC_PATHS.has(to.path)) return true

    if(to.path.endsWith('.png') || 
       to.path.endsWith('.jpg') || 
       to.path.endsWith('.jpeg') || 
       to.path.endsWith('.ico') || 
       to.path.endsWith('.icon') || 
       to.path.endsWith('.svg') || 
       to.path.endsWith('.css'))
    {
      return true
    }

    // 2) Se vem com ?code=...&state=..., é o callback do OIDC (alguns IdP chamam a outras rotas)
    if (to.query && to.query.code && to.query.state) return true

    // 3) Já autenticado?
    const user = await getUser({ redirectOnExpiry: false })
    const isLogged = !!(user && !user.expired)
    if (isLogged) return true

    // 4) Evitar loops: só disparar login 1x por navegação
    if (redirecting) return false
    redirecting = true
    try {
      await login(to.fullPath)
    } finally {
      redirecting = false
    }
    return false
  })
}
