<template>
    <div class="profile" ref="profileWrap">
    <button
      class="profile-btn"
      @click="isOpen = !isOpen"
      :aria-expanded="isOpen ? 'true' : 'false'"
      aria-haspopup="menu"
      title="Profile"
    >
      <span class="avatar" aria-hidden="true"><font-awesome-icon icon="fa-regular fa-id-badge" size="l" /></span>
    </button>

    <transition name="fade">
      <div
        v-if="isOpen"
        class="profile-menu"
        role="menu"
        @keydown.esc="isOpen = false"
      >
        <div class="profile-item name" role="menuitem" tabindex="-1">
          {{ userName || 'Utilizador' }}
        </div>
        <a class="profile-item" :href="scalarUrl" target="_blank">Scalar</a>
        <button
          class="profile-item logout"
          role="menuitem"
          @click="doLogout"
        >
          Logout
        </button>
      </div>
    </transition>
  </div>
</template>

<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { getUser, logout } from '@/auth/oidc'
import { getApiBase } from '@/lib/getApiBase'

const isOpen = ref(false)
const profileWrap = ref(null)
const userName = ref(null)

const scalarUrl = ref(getApiBase() + "/scalar");

async function loadUserName() {
    const currentUser = await getUser()
    userName.value = currentUser?.profile?.name || ''
}

async function doLogout() {
  await logout();
}

function onClickOutside(e) {
  if (!profileWrap.value) return
  if (!profileWrap.value.contains(e.target)) {
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
.topbar-actions {
  display: flex;
  align-items: center;
  gap: .5rem;
}

.profile {
  position: relative;
}

.profile-btn {
  display: inline-flex;
  align-items: center;
  gap: .5rem;
      /* border: 1px solid rgba(0,0,0,.1); */
  /* background: #fff; */
  padding: .4rem .6rem;
  border-radius: .5rem;
  cursor: pointer;
}

.profile-btn:hover {
  color: var(--accent-color, #007bff);
}

.profile-btn:focus {
  outline: 2px solid #6aa0ff;
  outline-offset: 2px;
}

.avatar {
  display: inline-flex;
  width: 1.2rem;
  height: 1.2rem;
  justify-content: center;
  align-items: center;
}

/* .icon{border:1px solid var(--line); border-radius:10px; padding:8px; width:38px; height:38px; display:inline-flex; align-items:center; justify-content:center}
.icon.on{outline:2px solid #374151}
.icon.brand{outline-color: var(--brand)} */

.profile-menu {
  position: absolute;
  right: 0;
  top: calc(100% + .4rem);
  min-width: 200px;
  border:1px solid var(--line);
  background:var(--panel);
  color:var(--text);
  /* background: #fff; */
  /* border: 1px solid rgba(0,0,0,.1); */

  border-radius: .5rem;
  box-shadow: 0 6px 20px rgba(0,0,0,.12);
  padding: .25rem;
  z-index: 1000;
    display: flex;
  flex-direction: column;
}

.profile-item {
  /* width: 100%; */
  text-align: left;
  padding: .6rem .8rem;
  border-radius: .4rem;
  font-size: .95rem;
}

a.profile-item {
  border:1px solid var(--line);
  color:var(--text);
  align-items:center;
  cursor:pointer;
}

.profile-item.name {
  font-weight: 600;
  cursor: default;
  color: #333;
}

.profile-item.logout {
  background: transparent;
  border: 0;
  cursor: pointer;
}

.profile-item.logout:hover {
  outline:2px solid #374151
}

.fade-enter-active, .fade-leave-active {
  transition: opacity .12s ease;
}
.fade-enter-from, .fade-leave-to {
  opacity: 0;
}
</style>