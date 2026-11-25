import axios from 'axios'
import { getApiBase } from '@/lib/getApiBase'
import { getAccessToken, recoverRejectedToken } from '@/auth/oidc'

const api = axios.create({ baseURL: '/api' })

api.interceptors.request.use(async config => {
  config.baseURL = getApiBase()

  // exemplo: lê o token de onde o guardas (localStorage, pinia, etc.)
  if (!config.headers.has('Authorization')) {
    const token = await getAccessToken()
    if (token) config.headers.set('Authorization', `Bearer ${token}`)
  }


  return config
})

api.interceptors.response.use(undefined, async error => {
  if (error.response?.status === 401)
    await recoverRejectedToken(error.config?.headers?.get('Authorization'))
  throw error
})

export default api
