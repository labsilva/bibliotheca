import { ref, onMounted, watchEffect, watch } from 'vue'
import api from '@/lib/http.js'

export function getDashboardData() { 
    const data = ref(null)
    const loading = ref(false)
    const error = ref(null)

    let controller = null

    async function fetchDashboardData() {
        loading.value = true
        error.value = null

        // cancela pedido anterior (se ainda estiver em voo)
        if (controller) controller.abort()
        controller = new AbortController()

        try {
            const res = await api.get(`/api/dashboard`, {
                signal: controller.signal,
                headers: { 'Accept': 'application/json' }
            })
            data.value = res.data
            return res.data
        } 
        catch (e) {
            if (e.name !== 'AbortError') 
                error.value = e
        } 
        finally {
            loading.value = false
        }
    }
    
    onMounted(fetchDashboardData)
    
    return { data, loading, error, fetchDashboardData }
}