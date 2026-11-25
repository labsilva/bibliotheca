// @vitest-environment node
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { expect, it, vi } from 'vitest'
it('does not cache authenticated API responses at runtime', () => {
  const config = readFileSync(new URL('../vite.config.js', import.meta.url), 'utf8')
  expect(config).toContain('runtimeCaching: []')
})

it('removes both legacy runtime caches during service worker activation', async () => {
  const self = { addEventListener: vi.fn() }
  const caches = { delete: vi.fn().mockResolvedValue(true) }
  const script = readFileSync(new URL('../public/remove-legacy-caches.js', import.meta.url), 'utf8')
  runInNewContext(script, { self, caches })
  const [[eventName, activate]] = self.addEventListener.mock.calls
  expect(eventName).toBe('activate')

  const event = { waitUntil: vi.fn() }
  activate(event)
  await event.waitUntil.mock.calls[0][0]
  expect(caches.delete.mock.calls).toEqual([['api'], ['images'], ['bibliotheca-public-covers-v1'], ['bibliotheca-public-covers-v2']])
})
