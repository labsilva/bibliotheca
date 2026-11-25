import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { onMounted, onUnmounted } from 'vue'
import { getAccessToken } from '@/auth/oidc'
import { getApiBase } from '@/lib/getApiBase'

const retryDelays = [1000, 3000, 10000]

export function useBooksHub(onBookImported, onConnected) {
  let connection
  let retry
  let disposed = false

  function handleBookImported(payload) {
    if (!disposed) onBookImported?.(payload)
  }

  async function start(attempt = 0) {
    try {
      await connection.start()
      if (!disposed) onConnected?.()
    } catch (error) {
      if (disposed) return
      const delay = retryDelays[attempt]
      if (delay === undefined) {
        return
      }
      retry = setTimeout(() => start(attempt + 1), delay)
    }
  }

  onMounted(() => {
    connection = new HubConnectionBuilder()
      .withUrl(getApiBase() + '/api/bibliotheca-hub', {
        accessTokenFactory: async () => (await getAccessToken()) ?? '',
      })
      .configureLogging(LogLevel.None)
      .withAutomaticReconnect()
      .build()
    connection.on('BookImported', handleBookImported)
    connection.onreconnected(() => { if (!disposed) onConnected?.() })
    start()
  })

  onUnmounted(async () => {
    disposed = true
    clearTimeout(retry)
    if (!connection) return
    connection.off('BookImported', handleBookImported)
    try {
      await connection.stop()
    } catch (error) {
      // Unmount cleanup must not emit errors containing transport URLs.
    }
  })
}
