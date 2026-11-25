import { expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import Sidebar from '@/components/Sidebar.vue'

it('closes on a mobile library link but not on an unrelated action', async () => {
  vi.stubGlobal('innerWidth', 390)
  const wrapper = mount(Sidebar, { global: { stubs: {
    RouterLink: { template: '<a href="/books"><slot /></a>' },
    UserLibraries: { template: '<a href="/library"><span>Library</span></a>' },
    UserShelves: { template: '<button>Add shelf</button>' }, 'font-awesome-icon': true,
  } } })
  await wrapper.get('button').trigger('click')
  expect(wrapper.emitted('navigate')).toBeUndefined()
  await wrapper.get('a[href="/library"] span').trigger('click')
  expect(wrapper.emitted('navigate')).toHaveLength(1)
  vi.unstubAllGlobals()
})
