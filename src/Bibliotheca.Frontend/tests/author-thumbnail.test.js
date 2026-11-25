import { expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import AuthorThumbnail from '@/components/AuthorThumbnail.vue'
it('shows portraits and falls back when unavailable', async () => {
  const wrapper = mount(AuthorThumbnail, { props: { name: 'Ada', url: 'https://example.org/ada.jpg' }, global: { stubs: { 'font-awesome-icon': true } } })
  expect(wrapper.get('img').attributes('alt')).toBe('Portrait of Ada')
  await wrapper.get('img').trigger('error')
  expect(wrapper.get('[aria-label="No author photo available"]').exists()).toBe(true)
  await wrapper.setProps({ url: null })
  expect(wrapper.find('img').exists()).toBe(false)
  await wrapper.setProps({ url: 'https://example.org/other.jpg' })
  expect(wrapper.get('img').attributes('src')).toContain('other.jpg')
})
