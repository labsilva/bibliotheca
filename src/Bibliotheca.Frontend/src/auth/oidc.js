import { catalogueAccount, catalogueAccountKey } from './catalogueAccount.js'
import { UserManager, WebStorageStateStore, Log } from 'oidc-client-ts'
import { setting } from '../lib/runtimeConfig.js'

const authority = setting('OIDC_AUTHORITY', import.meta.env.VITE_OIDC_AUTHORITY)
const client_id = setting('OIDC_CLIENT_ID', import.meta.env.VITE_OIDC_CLIENT_ID)
const scopes = new Set(setting('OIDC_SCOPE', import.meta.env.VITE_OIDC_SCOPE || 'openid profile email').split(/\s+/))
scopes.add('offline_access')

Log.setLogger(undefined)
Log.setLevel(Log.NONE)

export const oidc = new UserManager({
  authority,
  client_id,
  redirect_uri: setting('OIDC_REDIRECT_URI', import.meta.env.VITE_OIDC_REDIRECT_URI),
  post_logout_redirect_uri: setting('OIDC_POST_LOGOUT_REDIRECT_URI', import.meta.env.VITE_OIDC_POST_LOGOUT_REDIRECT_URI),
  response_type: 'code',
  scope: [...scopes].join(' '),
  userStore: new WebStorageStateStore({ store: sessionStorage }),
  // The expiry event and foreground requests share one refresh-token exchange.
  automaticSilentRenew: false,
  filterProtocolClaims: true,
  loadUserInfo: true,
})

function updateCatalogueAccount(user) {
  catalogueAccount.value = catalogueAccountKey(user, authority)
}

oidc.events.addUserLoaded(updateCatalogueAccount)
oidc.events.addUserUnloaded(() => { catalogueAccount.value = null })

let renewal
let redirect
let ending = false
const sessionChannel = typeof window.BroadcastChannel === 'function'
  ? new window.BroadcastChannel('bibliotheca-session:' + authority + ':' + client_id)
  : null

function renew() {
  renewal ??= oidc.signinSilent().catch(async error => {
    if (error.error !== 'invalid_grant') throw error
    await oidc.removeUser()
    return null
  }).finally(() => { renewal = undefined })
  return renewal
}

oidc.events.addAccessTokenExpired(async () => {
  if (!ending && navigator.onLine) {
    try {
      const user = await getUser()
      if (user?.expired) await login()
    } catch {
      // A foreground request retries after a transient network failure.
    }
  }
})

if (sessionChannel) {
  sessionChannel.onmessage = async ({ data }) => {
    if (data?.type !== 'signin' && data?.type !== 'signout') return
    const user = await oidc.getUser()
    if (ending || !user || (data.type === 'signin' && (data.account
      ? data.account === catalogueAccountKey(user, authority)
      : data.subject === user.profile.sub))) return
    ending = true
    await renewal?.catch(() => {})
    await oidc.removeUser()
    window.location.replace(data.type === 'signout' ? '/logout-callback' : '/')
  }
}

export function login(redirectTo) {
  if (ending) return
  redirect ??= (async () => {
    await oidc.clearStaleState()
    await oidc.signinRedirect({
      state: { target: redirectTo ?? window.location.pathname + window.location.search + window.location.hash },
    })
  })().finally(() => { redirect = undefined })
  return redirect
}

export async function handleSigninCallback() {
  const user = await oidc.signinRedirectCallback()
  if (user?.profile?.sub) sessionChannel?.postMessage({
    type: 'signin', subject: user.profile.sub, account: catalogueAccountKey(user, authority),
  })
  return user?.state?.target || '/'
}

export async function logout() {
  ending = true
  try {
    await renewal?.catch(() => {})
    sessionChannel?.postMessage({ type: 'signout' })
    await oidc.signoutRedirect()
  } finally {
    ending = false
  }
}

export async function getUser({ redirectOnExpiry = true } = {}) {
  if (ending) return null
  const account = catalogueAccount.value
  const user = await oidc.getUser()
  if (ending) return null
  if (catalogueAccount.value === account) updateCatalogueAccount(user)
  if (!user?.expired || !user.refresh_token || !navigator.onLine) return user
  const renewed = await renew()
  if (!renewed && redirectOnExpiry) await login()
  return renewed
}

export async function getAccessToken() {
  const user = await getUser()
  if (user && !user.expired) return user.access_token
}

export async function recoverRejectedToken(authorization) {
  if (ending || !authorization) return
  await renewal?.catch(() => {})
  const user = await oidc.getUser()
  if (user && authorization === 'Bearer ' + user.access_token) {
    await oidc.removeUser()
    await login()
  }
}
