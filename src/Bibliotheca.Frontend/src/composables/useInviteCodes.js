import { ref } from 'vue'
import api from '@/lib/http.js'

export function getInviteCode() {
  const inviteCode = ref("");
  const loading = ref(false);
  const error = ref(null);

  async function fetchInviteCode() {
    loading.value = true;
    error.value = null;

    try {
      const res = await api.post(`/api/account/invite-code`);
      if (res.status !== 200) {
        throw new Error(`Erro ao obter invite code: ${res.status}`);
      }
      inviteCode.value = res.data

    } catch (err) {
      inviteCode.value = ''
      error.value = 'Unable to create an invitation.';

    } finally {
      loading.value = false;
    }
  }

  return { inviteCode, loading, error, fetchInviteCode };
}
