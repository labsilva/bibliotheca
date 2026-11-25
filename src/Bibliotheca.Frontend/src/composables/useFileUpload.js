// src/composables/useFileUpload.js
import { ref } from 'vue'
import api from '@/lib/http.js'

export function useFileUpload (endpoint) {
  const loading = ref(false)
  const success = ref(false)
  const errorMessage = ref('')

  async function uploadFile (file) {
    loading.value = true
    success.value = false
    errorMessage.value = ''

    const formData = new FormData()
    formData.append('file', file)

    try {
      const res = await api.post(endpoint, formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })

      if (res.status === 200 || res.status === 202) {
        success.value = true
        return res.data
      }
    } catch (err) {
      tratarErroProblemDetails(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  function tratarErroProblemDetails (err) {
    if (err.response) {
      const { status, data } = err.response

      if (data && typeof data === 'object') {
        const title = data.title || 'Erro ao processar ficheiro'
        const detail = data.detail || ''
        const errors = data.errors

        let extra = ''

        if (errors && typeof errors === 'object') {
          extra = Object.entries(errors)
            .map(([key, messages]) => {
              const texto = Array.isArray(messages) ? messages.join(', ') : String(messages)
              return `${key}: ${texto}`
            })
            .join(' | ')
        }

        const partes = [title, detail, extra].filter(Boolean)
        errorMessage.value = partes.join(' — ')
      } else {
        errorMessage.value = `Erro ${status} ao processar o ficheiro.`
      }
    } else if (err.request) {
      errorMessage.value = 'Não foi possível contactar o servidor. Verifica a ligação de rede.'
    } else {
      errorMessage.value = 'Ocorreu um erro inesperado ao preparar o pedido.'
    }
  }

  return {
    uploadFile,
    loading,
    success,
    errorMessage,
  }
}
