import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import AddBookDialog from '@/components/AddBookDialog.vue'
import BarcodePhotoModal from '@/components/BarcodePhotoModal.vue'
import { importBook } from '@/composables/useBooks'

vi.mock('@/composables/useBooks', () => ({ importBook: vi.fn() }))
enableAutoUnmount(afterEach)

beforeEach(() => {
  if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function () { this.open = true }
    HTMLDialogElement.prototype.close = function () { this.open = false }
  }
})

afterEach(() => vi.unstubAllGlobals())

async function renderDialog({ mobile = false } = {}) {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: mobile })))
  const Host = defineComponent({
    components: { AddBookDialog },
    setup: () => ({ open: ref(false) }),
    template: '<button data-opener @click="open = true">Open</button><AddBookDialog v-model:open="open" />',
  })
  const wrapper = mount(Host, {
    attachTo: document.body,
    global: { stubs: ['font-awesome-icon'] },
  })
  const opener = wrapper.get('[data-opener]')
  opener.element.focus()
  await opener.trigger('click')
  await flushPromises()
  return wrapper
}

describe('adding a book by ISBN', () => {
  it('disables submission for blank input and queues a trimmed ISBN', async () => {
    importBook.mockResolvedValueOnce()
    const wrapper = await renderDialog()
    const input = wrapper.get('#add-book-isbn')
    const submit = wrapper.get('button[type="submit"]')

    await input.setValue('   ')
    expect(submit.element.disabled).toBe(true)
    await wrapper.get('form').trigger('submit')
    expect(importBook).not.toHaveBeenCalled()

    await input.setValue(' 9780140328721 ')
    expect(submit.element.disabled).toBe(false)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(importBook).toHaveBeenCalledExactlyOnceWith('9780140328721')
    expect(wrapper.get('dialog').element.open).toBe(false)
    expect(wrapper.get('[role="status"]').text()).toContain('Book queued for import')
  })

  it('keeps a decoded ISBN after a failed request and supports retry', async () => {
    importBook.mockRejectedValueOnce(new Error('Network error')).mockResolvedValueOnce()
    const wrapper = await renderDialog()

    await wrapper.get('.scan-button').trigger('click')
    const scanner = wrapper.getComponent(BarcodePhotoModal)
    scanner.vm.$emit('decoded', '9780140328721')
    scanner.vm.$emit('update:open', false)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('Check your connection')
    expect(wrapper.get('dialog').element.open).toBe(true)
    expect(wrapper.get('#add-book-isbn').element.value).toBe('9780140328721')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(importBook.mock.calls).toEqual([['9780140328721'], ['9780140328721']])
    expect(wrapper.get('dialog').element.open).toBe(false)
  })

  it('focuses the desktop input and restores focus when cancelling', async () => {
    const wrapper = await renderDialog()
    expect(document.activeElement).toBe(wrapper.get('#add-book-isbn').element)

    const cancel = wrapper.get('.cancel-button')
    expect(cancel.text()).toBe('Cancel')
    expect(cancel.get('font-awesome-icon-stub').attributes('icon')).toBe('fa-solid fa-arrow-left')
    await cancel.trigger('click')
    expect(wrapper.get('dialog').element.open).toBe(false)
    expect(document.activeElement).toBe(wrapper.get('[data-opener]').element)
  })

  it('avoids focusing the ISBN on mobile and keeps it when the photo picker is cancelled', async () => {
    const wrapper = await renderDialog({ mobile: true })
    const input = wrapper.get('#add-book-isbn')
    expect(document.activeElement).not.toBe(input.element)
    await input.setValue('9780140328721')
    await wrapper.get('.scan-button').trigger('click')
    const picker = wrapper.get('input[type="file"]')
    expect(picker.attributes('capture')).toBe('environment')
    await picker.trigger('cancel')
    expect(wrapper.get('[role="status"]').text()).toContain('No photo selected')

    await wrapper.get('.footer button').trigger('click')
    expect(wrapper.find('input[type="file"]').exists()).toBe(false)
    expect(input.element.value).toBe('9780140328721')
    expect(document.activeElement).toBe(wrapper.get('.scan-button').element)
  })

  it('prevents repeat submission while the request is pending', async () => {
    let resolveImport
    importBook.mockReturnValueOnce(new Promise(resolve => { resolveImport = resolve }))
    const wrapper = await renderDialog()
    await wrapper.get('#add-book-isbn').setValue('9780140328721')
    const form = wrapper.get('form')
    await form.trigger('submit')
    expect(wrapper.get('button[type="submit"]').text()).toBe('Adding…')
    await form.trigger('submit')
    expect(importBook).toHaveBeenCalledTimes(1)
    resolveImport()
    await flushPromises()
    expect(wrapper.get('dialog').element.open).toBe(false)
  })

  it('uses the visible viewport on mobile and closes the scanner before the form on Escape', async () => {
    const viewport = Object.assign(new EventTarget(), { height: 650, offsetTop: 0 })
    vi.stubGlobal('visualViewport', viewport)
    const wrapper = await renderDialog({ mobile: true })
    const dialog = wrapper.get('dialog')
    viewport.height = 320
    viewport.dispatchEvent(new Event('resize'))
    expect(dialog.element.style.getPropertyValue('--visible-height')).toBe('320px')

    await wrapper.get('.scan-button').trigger('click')
    await dialog.trigger('cancel')
    expect(wrapper.find('input[type="file"]').exists()).toBe(false)
    expect(dialog.element.open).toBe(true)

    await dialog.trigger('cancel')
    expect(dialog.element.open).toBe(false)
  })
})
