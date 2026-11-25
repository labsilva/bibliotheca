import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import SearchBox from '@/components/SearchBox.vue'
import TagPicker from '@/components/TagPicker.vue'
import api from '@/lib/http.js'
import { useFileUpload } from '@/composables/useFileUpload.js'

const { push } = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/lib/http.js', () => ({ default: { get: vi.fn(), post: vi.fn() } }))
vi.mock('@/lib/getApiBase', () => ({ getApiBase: () => '/backend' }))
enableAutoUnmount(afterEach)

describe('global search', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
  })

  it('debounces typing, encodes the query and opens a keyboard-selected book', async () => {
    api.get.mockResolvedValueOnce({ data: [
      { area: 'Book', id: '42', title: 'Matilda & friends', imagePath: '/api/books/42/cover' },
      { area: 'Author', id: '3', title: 'Roald Dahl', imagePath: '/author.png' },
    ] })
    const wrapper = mount(SearchBox, { global: { stubs: ['font-awesome-icon'] } })
    const input = wrapper.get('input')

    await input.setValue('Mat')
    await vi.advanceTimersByTimeAsync(150)
    await input.setValue('Matilda & friends')
    await vi.advanceTimersByTimeAsync(249)
    expect(api.get).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)

    expect(api.get).toHaveBeenCalledExactlyOnceWith('/api/search', expect.objectContaining({
      params: { text: 'Matilda & friends' },
      signal: expect.any(AbortSignal),
    }))
    expect(wrapper.findAll('[role="option"]')).toHaveLength(2)
    expect(wrapper.get('img').attributes('src')).toBe('/backend/api/books/42/cover')
    await input.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(wrapper.get('[role="option"].active').text()).toContain('Matilda & friends')
    await input.trigger('keydown', { key: 'Enter' })

    expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'book', params: { id: '42' } })
    expect(input.element.value).toBe('')
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    await vi.advanceTimersByTimeAsync(250)
    expect(api.get).toHaveBeenCalledTimes(1)
  })

  it('ignores stale responses without hiding the current loading state', async () => {
    let resolveOld
    let resolveNew
    api.get.mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve }))
    api.get.mockReturnValueOnce(new Promise(resolve => { resolveNew = resolve }))
    const wrapper = mount(SearchBox, { global: { stubs: ['font-awesome-icon'] } })
    const input = wrapper.get('[role="combobox"]')

    await input.setValue('Older')
    await vi.advanceTimersByTimeAsync(250)
    const olderRequest = api.get.mock.calls[0][1].signal
    await input.setValue('Newer')
    expect(olderRequest.aborted).toBe(true)
    await vi.advanceTimersByTimeAsync(250)
    resolveOld({ data: [{ id: 1, area: 'Book', title: 'Older result' }] })
    await vi.advanceTimersByTimeAsync(0)
    expect(wrapper.find('[aria-label="A procurar…"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('Older result')

    resolveNew({ data: [{ id: 2, area: 'Book', title: 'Newer result' }] })
    await vi.advanceTimersByTimeAsync(0)
    expect(wrapper.get('[role="option"]').text()).toContain('Newer result')
    expect(wrapper.find('[aria-label="A procurar…"]').exists()).toBe(false)
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe(wrapper.get('[aria-selected="true"]').attributes('id'))
  })

  it('cancels pending work when clearing and unmounting the search box', async () => {
    api.get.mockImplementation(() => new Promise(() => {}))
    const wrapper = mount(SearchBox, { global: { stubs: ['font-awesome-icon'] } })
    const input = wrapper.get('input')
    await input.setValue('First')
    await input.setValue('')
    await vi.advanceTimersByTimeAsync(250)
    expect(api.get).not.toHaveBeenCalled()

    await input.setValue('Second')
    await vi.advanceTimersByTimeAsync(250)
    const signal = api.get.mock.calls[0][1].signal
    wrapper.unmount()
    expect(signal.aborted).toBe(true)
  })
})

describe('tag selection', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
  })

  it('uses an existing suggestion and clears the field', async () => {
    api.get.mockResolvedValueOnce({ status: 200, data: [{ tagId: 7, tag: 'Fantasy' }] })
    const wrapper = mount(TagPicker)
    const input = wrapper.get('input')
    await input.setValue('Fant')
    await vi.advanceTimersByTimeAsync(250)
    expect(wrapper.get('[role="option"]').text()).toBe('Fantasy')

    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('add')).toEqual([[{ tagId: 7, tag: 'Fantasy' }]])
    expect(input.element.value).toBe('')
  })

  it('rejects attached tags case-insensitively and emits a trimmed new name', async () => {
    const wrapper = mount(TagPicker, { props: { tags: [{ tagId: 7, tag: 'Fantasy' }] } })
    const input = wrapper.get('input')
    await input.setValue(' fantasy ')
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.text()).toContain('Already added')
    expect(wrapper.emitted('add')).toBeUndefined()

    api.get.mockResolvedValueOnce({ status: 200, data: [] })
    await input.setValue('  Classic  ')
    await vi.advanceTimersByTimeAsync(250)
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('add')).toEqual([[{ tag: 'Classic' }]])
  })

  it('accepts Enter only once the current query has selectable results', async () => {
    let resolveSearch
    api.get.mockResolvedValueOnce({ data: [{ tagId: 7, tag: 'Fantasy' }] })
    api.get.mockReturnValueOnce(new Promise(resolve => { resolveSearch = resolve }))
    const wrapper = mount(TagPicker)
    const input = wrapper.get('input')
    await input.setValue('Fant')
    await vi.advanceTimersByTimeAsync(250)
    expect(wrapper.text()).toContain('Fantasy')

    await input.setValue('Hist')
    expect(wrapper.find('[role="option"]').exists()).toBe(false)
    await input.trigger('keydown', { key: 'Enter' })
    await vi.advanceTimersByTimeAsync(250)
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('add')).toBeUndefined()

    resolveSearch({ data: [{ tagId: 8, tag: 'History' }] })
    await vi.advanceTimersByTimeAsync(0)
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('add')).toEqual([[{ tagId: 8, tag: 'History' }]])
  })

  it('allows Tab to move focus without selecting the highlighted tag', async () => {
    api.get.mockResolvedValueOnce({ data: [{ tagId: 7, tag: 'Fantasy' }] })
    const wrapper = mount(TagPicker)
    const input = wrapper.get('input')
    await input.setValue('Fant')
    await vi.advanceTimersByTimeAsync(250)
    expect(wrapper.get('[role="option"].highlighted').text()).toBe('Fantasy')

    const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    input.element.dispatchEvent(tab)
    expect(tab.defaultPrevented).toBe(false)
    expect(wrapper.emitted('add')).toBeUndefined()
  })

  it.each(['Escape', 'blur', 'unmount'])('ignores a late response after %s', async action => {
    let resolveSearch
    api.get.mockReturnValueOnce(new Promise(resolve => { resolveSearch = resolve }))
    const wrapper = mount(TagPicker)
    const input = wrapper.get('input')
    await input.setValue('Fant')
    await vi.advanceTimersByTimeAsync(250)
    const { signal } = api.get.mock.lastCall[1]

    if (action === 'Escape') await input.trigger('keydown', { key: 'Escape' })
    else if (action === 'blur') await input.trigger('blur')
    else wrapper.unmount()
    expect(signal.aborted).toBe(true)
    resolveSearch({ data: [{ tagId: 7, tag: 'Fantasy' }] })
    await vi.advanceTimersByTimeAsync(0)

    if (action !== 'unmount') {
      expect(input.attributes('aria-expanded')).toBe('false')
      expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
      await input.trigger('keydown', { key: 'Enter' })
      expect(wrapper.emitted('add')).toBeUndefined()
    }
  })
})

describe('ISBN file upload', () => {
  const file = new File(['9780140328721'], 'books.txt', { type: 'text/plain' })

  it('submits the file as multipart and exposes the result', async () => {
    const data = { queued: 1 }
    api.post.mockResolvedValueOnce({ status: 200, data })
    const upload = useFileUpload('/api/library/upload')

    const pending = upload.uploadFile(file)
    expect(upload.loading.value).toBe(true)
    expect(api.post.mock.calls[0][0]).toBe('/api/library/upload')
    expect(api.post.mock.calls[0][1].get('file').name).toBe('books.txt')
    expect(await pending).toEqual(data)
    expect(upload.success.value).toBe(true)
    expect(upload.loading.value).toBe(false)
  })

  it.each([
    [{ response: { status: 400, data: { title: 'Invalid file', detail: 'Check the ISBN', errors: { file: ['Missing ISBN'] } } } },
      'Invalid file — Check the ISBN — file: Missing ISBN'],
    [{ response: { status: 503, data: '' } }, 'Erro 503'],
    [{ request: {} }, 'Não foi possível contactar'],
    [new Error('Unknown error'), 'Ocorreu um erro inesperado'],
  ])('reports upload failures and clears the loading state', async (failure, message) => {
    api.post.mockRejectedValueOnce(failure)
    const upload = useFileUpload('/api/library/upload')

    await expect(upload.uploadFile(file)).rejects.toBe(failure)
    expect(upload.errorMessage.value).toContain(message)
    expect(upload.success.value).toBe(false)
    expect(upload.loading.value).toBe(false)
  })
})
