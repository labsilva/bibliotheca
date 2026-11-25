<template><div>Logging in...</div></template>
<script>
import { onMounted } from 'vue'
import { handleSigninCallback } from '@/auth/oidc'
import { useRouter } from 'vue-router'
export default {
  setup() {
    const router = useRouter()
    onMounted(async () => {
      try {
        const target = await handleSigninCallback()
        router.replace(target || '/')
      } catch (e) {
        router.replace('/')
      }
    })
  }
}
</script>
