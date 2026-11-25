import { createApp } from 'vue'
import { createPinia } from 'pinia'
import './assets/theme.css'
import './assets/icons/icons.css'  

import App from './App.vue'
import router from './router'
import { installAuthGuard } from './auth/guard'
import moment from 'moment';
import api from './lib/http.js';
import { getApiBase } from './lib/getApiBase.js'

export const API_URL = getApiBase()

installAuthGuard(router)

/* Font Awesome */
import { library } from '@fortawesome/fontawesome-svg-core'
import { fas } from '@fortawesome/free-solid-svg-icons'
import { far } from '@fortawesome/free-regular-svg-icons'
import { fab } from '@fortawesome/free-brands-svg-icons'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
library.add(fas, far, fab)

const app = createApp(App)
app.use(createPinia())
app.use(router)

app.config.globalProperties.$formatDate = (dateString, format = 'YYYY-MM-DD') => {
  return moment(dateString).format(format);
};
app.config.globalProperties.$http = api;
app.component('font-awesome-icon', FontAwesomeIcon)
app.mount('#app')
