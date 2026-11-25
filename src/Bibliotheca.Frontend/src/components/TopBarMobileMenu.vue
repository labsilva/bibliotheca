    <template>
    <div class="topbarmenu" ref="topbarmenuWrap">
    <button
      class="topbarmenu-btn"
      @click="isOpen = !isOpen"
      :aria-expanded="isOpen ? 'true' : 'false'"
      aria-haspopup="menu"
      title="Menu"
    >
      <span class="menu" aria-hidden="true"><font-awesome-icon icon="fa-solid fa-ellipsis-vertical" /></span>
    </button>

    <transition name="fade">
      <div
        v-if="isOpen"
        class="topbarmenu-menu"
        role="menu"
        @click="onMenuClick"
        @keydown.esc="isOpen = false"
      >
        <div class="topbarmenu-item name" role="menuitem" tabindex="-1">
          {{ userName || 'Utilizador' }}
        </div>
        <slot name="buttons" />
        <button
  class="topbarmenu-item logout"
  :class="{ 'is-hover': isLogoutHover }"
  role="menuitem"
  @click="doLogout"
  @mouseenter="isLogoutHover = true"
  @mouseleave="isLogoutHover = false"
>
  Logout
</button>
      </div>
    </transition>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { getUser, logout } from '@/auth/oidc'

const route = useRoute()
const isOpen = ref(false)
watch(() => route.fullPath, () => { isOpen.value = false })
function onMenuClick(event) {
  if (event.target.closest('a[href]')) isOpen.value = false
}
const topbarmenuWrap = ref(null)
const userName = ref(null)
const isLogoutHover = ref(false)


async function loadUserName() {
    const currentUser = await getUser()
    userName.value = currentUser?.profile?.name || ''
}

async function doLogout() {
  await logout();
}

function onClickOutside(e) {
  if (!topbarmenuWrap.value) return
  if (!topbarmenuWrap.value.contains(e.target)) {
    isOpen.value = false
  }
}

onMounted(async () => {
  await loadUserName()
  document.addEventListener('click', onClickOutside, { capture: true })
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onClickOutside, { capture: true })
})
</script>


<style scoped>

.topbarmenu {
  position: relative;
}

.topbarmenu-btn {
  display: inline-flex;
  align-items: center;
  gap: .5rem;
      /* border: 1px solid rgba(0,0,0,.1); */
  /* background: #fff; */
  padding: .4rem .6rem;
  border-radius: .5rem;
  cursor: pointer;
}

.topbarmenu-btn:hover {
  color: var(--accent-color, #007bff);
}

.topbarmenu-btn:focus {
  outline: 2px solid #6aa0ff;
  outline-offset: 2px;
}

.menu {
  display: inline-flex;
  width: 1.2rem;
  height: 1.2rem;
  justify-content: center;
  align-items: center;
}

.topbarmenu-menu {
  position: absolute;
  right: 0;
  top: calc(100% + .4rem);
  min-width: 200px;
  border:1px solid var(--line);
  background:var(--panel);
  color:var(--brand);
  border-radius: .5rem;
  box-shadow: 0 6px 20px rgba(0,0,0,.12);
  padding: .25rem;
  z-index: 1000;
  display: flex;
  flex-direction: column;
}

.topbarmenu-item {
  width: 100%;
  text-align: left;
  padding: .6rem .8rem;
  border-radius: .4rem;
  font-size: .95rem;
}

.topbarmenu-item.name {
  font-weight: 600;
  cursor: default;
  color: #333;
}

.topbarmenu-item.logout {
  background: transparent;
  border: 0;
  cursor: pointer;
}

.topbarmenu-item.logout:hover {
  color: var(--brand-glow);
  background: rgba(255, 0, 0, 0.1);
}

.fade-enter-active, .fade-leave-active {
  transition: opacity .12s ease;
}
.fade-enter-from, .fade-leave-to {
  opacity: 0;
}

</style>