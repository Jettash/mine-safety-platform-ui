import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import AppLayout from '../layouts/AppLayout.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: () => import('../modules/auth/LoginView.vue'), meta: { public: true } },
    {
      path: '/', component: AppLayout,
      children: [
        { path: '', redirect: '/monitor' },
        { path: 'monitor', component: () => import('../modules/truck/MonitorView.vue') },
        { path: 'events', component: () => import('../modules/truck/EventListView.vue') },
        { path: 'events/:eventId', component: () => import('../modules/truck/EventDetailView.vue') },
        { path: 'alarms', component: () => import('../modules/alarm/AlarmView.vue') },
        { path: 'reviews', component: () => import('../modules/truck/ReviewView.vue') },
        { path: 'devices', component: () => import('../modules/device/DeviceView.vue') },
      ],
    },
  ],
})

router.beforeEach(async (to) => {
  if (to.meta.public) return true
  const auth = useAuthStore()
  return await auth.check() ? true : { path: '/login', query: { redirect: to.fullPath } }
})

window.addEventListener('session-expired', () => {
  const auth = useAuthStore()
  auth.username = ''
  auth.checked = true
  router.push('/login')
})
export default router
