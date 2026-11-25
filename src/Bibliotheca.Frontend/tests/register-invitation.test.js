import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, expect, it } from 'vitest'

const script = readFileSync(resolve(process.cwd(), '../Bibliotheca.Backend/wwwroot/js/register-invitation.js'), 'utf8')
afterEach(() => {
  document.body.innerHTML = ''
  window.history.replaceState(null, '', '/')
})

it('fills a fragment invitation and clears its secret from history while preserving query routing', () => {
  window.history.replaceState({ fixture: true }, '', '/account/register?returnUrl=%2Faccount%2Flogin#inviteCode=secret%2Bcode%26value')
  document.body.innerHTML = '<input id="invitecode" name="InviteCode" required>'
  window.eval(script)
  expect(document.getElementById('invitecode').value).toBe('secret+code&value')
  expect(window.location.hash).toBe('')
  expect(window.location.pathname + window.location.search).toBe('/account/register?returnUrl=%2Faccount%2Flogin')
  expect(window.history.state).toEqual({ fixture: true })
})

it('preserves server-filled legacy invitations and allows manual entry without a fragment', () => {
  window.history.replaceState(null, '', '/account/register?inviteCode=legacy')
  document.body.innerHTML = '<input id="invitecode" name="InviteCode" value="legacy" required>'
  window.eval(script)
  expect(document.getElementById('invitecode').value).toBe('legacy')
  expect(window.location.search).toBe('?inviteCode=legacy')
  document.getElementById('invitecode').value = 'manually-entered'
  expect(document.getElementById('invitecode').value).toBe('manually-entered')
})
