import { defineStore } from 'pinia'
import { reactive, ref, onScopeDispose, watch } from 'vue'
import { defaultSettings } from '@/config/defaultSettings.js'
import { getUser, oidc } from '@/auth/oidc'
import api from '@/lib/http.js'

export const useSettingsStore = defineStore('settings', () => {
  const settings = reactive(structuredClone(defaultSettings))
  const loaded = ref(false)
  const syncing = ref(false)
  const error = ref(null)
  const dirty = ref(false)
  const library = settings.library

  let session
  let timer
  let initialRead
  let sessionVersion = 0
  let applying = false
  let previous = { ...library }

  function apply(values, pending) {
    applying = true
    Object.assign(library, values, pending)
    previous = { ...library }
    applying = false
  }

  function readCached(account) {
    try {
      return JSON.parse(localStorage.getItem(account.key)) ?? {}
    } catch {
      return {}
    }
  }

  function persist(account) {
    try {
      localStorage.setItem(account.key, JSON.stringify({ library, pending: account.pending }))
    } catch {
      // Storage can be unavailable, for example in private browsing.
    }
  }

  function requestOptions(account) {
    return {
      signal: account.controller.signal,
      headers: { Authorization: 'Bearer ' + account.token },
    }
  }

  function scheduleSave() {
    clearTimeout(timer)
    if (loaded.value && dirty.value) timer = setTimeout(saveToBackend, 800)
  }

  async function loadServer(account) {
    const { data } = await api.get('/api/account/settings', requestOptions(account))
    if (session !== account) return
    const server = typeof data === 'string' && data ? JSON.parse(data) : data
    apply(server?.library, account.pending)
    account.serverLoaded = true
    persist(account)
  }

  async function saveToBackend() {
    const account = session
    if (!loaded.value || !account?.token || !navigator.onLine || syncing.value || !dirty.value) return

    syncing.value = true
    error.value = null
    let saved = false

    try {
      if (!account.serverLoaded) await loadServer(account)
      if (session !== account || !account.token) return
      const sent = { ...account.pending }
      const payload = { library: { ...library } }
      const result = await api.put('/api/account/settings', payload, requestOptions(account))
      if (session !== account) return
      if (result.status !== 200) throw Error('Settings update was rejected')
      for (const [key, value] of Object.entries(sent)) {
        if (Object.is(account.pending[key], value)) delete account.pending[key]
      }
      dirty.value = Object.keys(account.pending).length > 0
      persist(account)
      saved = true
    } catch {
      if (session === account) error.value = 'Não foi possível sincronizar as definições com o servidor.'
    } finally {
      if (session === account) {
        syncing.value = false
        if (saved || account.retryOnCompletion) scheduleSave()
        account.retryOnCompletion = false
      }
    }
  }

  async function setSession(user) {
    const subject = user?.profile?.sub
    const issuer = user?.profile?.iss ?? oidc.settings.authority
    const key = subject && 'bibliotheca.settings.v2:' + encodeURIComponent(issuer) + ':' + encodeURIComponent(subject)
    const token = user?.expired ? null : user?.access_token
    if (key && session?.key === key) {
      session.token = token
      scheduleSave()
      return
    }

    clearTimeout(timer)
    session?.controller.abort()
    session = undefined
    loaded.value = false
    syncing.value = false
    dirty.value = false
    error.value = null
    apply(defaultSettings.library)

    if (!key) {
      loaded.value = true
      return
    }

    const account = { key, token, controller: new AbortController(), pending: {}, serverLoaded: false }
    session = account
    const cached = readCached(account)
    account.pending = cached.pending ?? {}
    dirty.value = Object.keys(account.pending).length > 0
    apply(cached.library, account.pending)

    if (account.token && navigator.onLine) {
      try {
        await loadServer(account)
        if (session !== account) return
      } catch {
        if (session !== account) return
        error.value = 'Não foi possível carregar as definições do servidor.'
      }
    }

    loaded.value = true
    void saveToBackend()
  }

  function onSessionChanged(user) {
    sessionVersion++
    void setSession(user)
  }

  function initialize() {
    if (!initialRead) {
      const version = sessionVersion
      initialRead = getUser().then(user => {
        if (sessionVersion === version) return setSession(user)
      }).catch(() => {
        if (sessionVersion !== version) return
        loaded.value = true
        error.value = 'Não foi possível identificar a conta.'
      })
    }
    return initialRead
  }

  function onOnline() {
    if (!session) return
    session.serverLoaded = false
    if (syncing.value) session.retryOnCompletion = true
    else void saveToBackend()
  }

  watch(library, values => {
    if (!session || applying) return
    for (const [key, value] of Object.entries(values)) {
      if (!Object.is(value, previous[key])) session.pending[key] = value
    }
    previous = { ...values }
    dirty.value = Object.keys(session.pending).length > 0
    persist(session)
    scheduleSave()
  }, { flush: 'sync' })

  window.addEventListener('online', onOnline)
  const unsubscribeLoaded = oidc.events.addUserLoaded(onSessionChanged)
  const unsubscribeUnloaded = oidc.events.addUserUnloaded(() => onSessionChanged(null))
  onScopeDispose(() => {
    sessionVersion++
    session?.controller.abort()
    session = undefined
    clearTimeout(timer)
    window.removeEventListener('online', onOnline)
    unsubscribeLoaded()
    unsubscribeUnloaded()
  })

  return { settings, library, loaded, syncing, error, dirty, initialize, saveToBackend }
})
