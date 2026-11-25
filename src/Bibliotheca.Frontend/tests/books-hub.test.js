import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { useBooksHub } from '@/composables/useBooksHub'
import { getAccessToken } from '@/auth/oidc'

const { builder, connection } = vi.hoisted(() => ({
  builder: { configureLogging: vi.fn(), withUrl: vi.fn(), withAutomaticReconnect: vi.fn(), build: vi.fn() },
  connection: { on: vi.fn(), onreconnected: vi.fn(), off: vi.fn(), start: vi.fn(), stop: vi.fn() },
}))

vi.mock('@microsoft/signalr', () => ({
  LogLevel: { None: 6 },
  HubConnectionBuilder: class { constructor() { return builder } },
}))
vi.mock('@/auth/oidc', () => ({ getAccessToken: vi.fn() }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

beforeEach(() => {
  builder.configureLogging.mockReturnValue(builder)
  builder.withUrl.mockReturnValue(builder)
  builder.withAutomaticReconnect.mockReturnValue(builder)
  builder.build.mockReturnValue(connection)
  connection.start.mockReset().mockResolvedValue()
  connection.stop.mockReset().mockResolvedValue()
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

function mountHub(callback = vi.fn(), onConnected) {
  return mount({
    setup() {
      useBooksHub(callback, onConnected)
      return () => null
    },
  })
}

it('gets the current token for each connection and forwards import events', async () => {
  const imported = vi.fn()
  getAccessToken.mockResolvedValueOnce('original-token').mockResolvedValueOnce('renewed-token').mockResolvedValueOnce(undefined)
  const connected = vi.fn()
  const wrapper = mountHub(imported, connected)
  await flushPromises()
  expect(connected).toHaveBeenCalledTimes(1)
  const reconnected = connection.onreconnected.mock.lastCall[0]
  reconnected()
  expect(connected).toHaveBeenCalledTimes(2)
  expect(builder.withUrl).toHaveBeenCalledWith('/backend/api/bibliotheca-hub', expect.any(Object))
  const { accessTokenFactory } = builder.withUrl.mock.calls[0][1]
  expect(await accessTokenFactory()).toBe('original-token')
  expect(await accessTokenFactory()).toBe('renewed-token')
  expect(await accessTokenFactory()).toBe('')
  expect(builder.withAutomaticReconnect).toHaveBeenCalledTimes(1)
  expect(builder.configureLogging).toHaveBeenCalledWith(6)

  const [event, receive] = connection.on.mock.calls[0]
  receive({ bookId: 42 })
  expect(imported).toHaveBeenCalledExactlyOnceWith({ bookId: 42 })
  wrapper.unmount()
  await flushPromises()
  receive({ bookId: 43 })
  expect(imported).toHaveBeenCalledTimes(1)
  expect(connection.off).toHaveBeenCalledExactlyOnceWith(event, receive)
  expect(connection.stop).toHaveBeenCalledTimes(1)
})

it('recovers from an initial failure and cancels retries on unmount', async () => {
  vi.useFakeTimers()
  connection.start.mockRejectedValueOnce(Error('Offline'))
  const first = mountHub()
  await vi.advanceTimersByTimeAsync(1000)
  expect(connection.start).toHaveBeenCalledTimes(2)
  first.unmount()

  connection.start.mockRejectedValueOnce(Error('Offline again'))
  const second = mountHub()
  await vi.advanceTimersByTimeAsync(0)
  second.unmount()
  await vi.advanceTimersByTimeAsync(10000)
  expect(connection.start).toHaveBeenCalledTimes(3)
})

it('stops retrying without logging sensitive transport errors', async () => {
  vi.useFakeTimers()
  const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
  const error = Error('Offline')
  connection.start.mockRejectedValue(error)
  mountHub()

  await vi.advanceTimersByTimeAsync(14000)

  expect(connection.start).toHaveBeenCalledTimes(4)
  expect(warn).not.toHaveBeenCalled()
})
