import { afterEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, mount, RouterLinkStub } from '@vue/test-utils'
import ItemList from '@/components/ItemList.vue'

enableAutoUnmount(afterEach)

function renderList(props = {}) {
  const push = vi.fn()
  const wrapper = mount(ItemList, {
    props: {
      header: 'Authors',
      routeName: 'author',
      backTo: { name: 'dashboard' },
      items: [{ id: 1, name: 'Roald Dahl' }, { id: 2, name: 'Ursula Le Guin' }],
      ...props,
    },
    global: { mocks: { $router: { push } }, stubs: { RouterLink: RouterLinkStub } },
  })
  return { wrapper, push }
}

describe('catalogue lists', () => {
  it('filters rows by name and displays the empty state when nothing matches', async () => {
    const { wrapper } = renderList()

    await wrapper.get('input').setValue('  le guin  ')
    expect(wrapper.findAll('article')).toHaveLength(1)
    expect(wrapper.get('article').text()).toContain('Ursula Le Guin')

    await wrapper.get('input').setValue('missing')
    expect(wrapper.findAll('article')).toHaveLength(0)
    expect(wrapper.text()).toContain('No items found.')
  })

  it('shows loading or error instead of the empty state', async () => {
    const { wrapper } = renderList({ items: [], loading: true })
    expect(wrapper.text()).toContain('Loading...')
    expect(wrapper.text()).not.toContain('No items found.')

    await wrapper.setProps({ loading: false, loadingError: new Error('Could not fetch authors') })
    expect(wrapper.get('[role="alert"]').text()).toBe('Could not fetch authors')
    expect(wrapper.text()).not.toContain('No items found.')
  })

  it('uses the destination supplied by the page for Go back', async () => {
    const { wrapper, push } = renderList({ backTo: { name: 'user-library' } })

    await wrapper.get('header button').trigger('click')

    expect(push).toHaveBeenCalledWith({ name: 'user-library' })
  })
})
