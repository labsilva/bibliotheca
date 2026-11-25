import { afterEach, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import BarcodeBatch from '@/components/BarcodeBatch.vue'
import api from '@/lib/http.js'

const { decodeFromStream } = vi.hoisted(() => ({ decodeFromStream: vi.fn() }))
vi.mock('@zxing/browser', () => ({
  BrowserMultiFormatReader: class {
    decodeFromStream = decodeFromStream
  },
}))
vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), put: vi.fn() } }))
enableAutoUnmount(afterEach)

afterEach(() => vi.restoreAllMocks())

it('queues distinct valid ISBNs on one shelf without closing the camera', async () => {
  const track = { stop: vi.fn() }
  const controls = { stop: vi.fn() }
  const camera = { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [track] }) }
  const vibration = vi.fn().mockReturnValue(true)
  Object.defineProperties(navigator, {
    mediaDevices: { configurable: true, value: camera },
    vibrate: { configurable: true, value: vibration },
  })
  decodeFromStream.mockResolvedValue(controls)
  api.get.mockResolvedValue({ data: { shelves: [{ shelveId: 3, shelve: 'Fiction' }] } })
  api.put.mockResolvedValue({ data: { id: 'accepted' } })
  const wrapper = mount(BarcodeBatch)
  await flushPromises()
  await wrapper.get('select').setValue('3')
  await wrapper.get('button').trigger('click')
  await flushPromises()

  const decode = decodeFromStream.mock.lastCall[2]
  for (const isbn of ['9780140328721', '9780140328721', '9780140328738', '9780140328739']) {
    decode({ getText: () => isbn })
  }
  await flushPromises()
  const [batchId] = wrapper.emitted('batch-started')[0]
  expect(api.put.mock.calls).toEqual([
    ['/api/fetch/isbn', { isbns: ['9780140328721'], shelfId: 3, batchId }],
    ['/api/fetch/isbn', { isbns: ['9780140328738'], shelfId: 3, batchId }],
  ])
  expect(wrapper.text()).toContain('2 queued for import')
  expect(vibration).toHaveBeenCalledTimes(2)
  expect(camera.getUserMedia).toHaveBeenCalledTimes(1)
  expect(controls.stop).not.toHaveBeenCalled()
  wrapper.unmount()
  expect(controls.stop).toHaveBeenCalledOnce()
  expect(track.stop).toHaveBeenCalledOnce()
  delete navigator.mediaDevices
  delete navigator.vibrate
})
