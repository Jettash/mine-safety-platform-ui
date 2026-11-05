<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useLiveEventsStore } from '../stores/liveEvents'

const auth = useAuthStore()
const live = useLiveEventsStore()
const router = useRouter()
onMounted(() => live.connect())
onBeforeUnmount(() => live.disconnect())
async function logout() { await auth.logout(); await router.push('/login') }
</script>

<template>
  <div class="shell">
    <aside class="sidebar">
      <div class="brand"><span class="brand-mark">矿</span><div><b>载运安全监测</b><small>魏家峁矿</small></div></div>
      <nav>
        <RouterLink to="/monitor">综合监控</RouterLink>
        <RouterLink to="/events">车辆记录</RouterLink>
        <RouterLink to="/alarms">报警处置</RouterLink>
        <RouterLink to="/reviews">待复核事件</RouterLink>
        <RouterLink to="/devices">设备与同步</RouterLink>
      </nav>
      <div class="account"><span>{{ auth.username }}</span><button class="link-button" @click="logout">退出</button></div>
    </aside>
    <main class="main"><RouterView /></main>
  </div>
</template>

