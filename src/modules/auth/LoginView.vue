<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../../stores/auth'

const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
async function submit() {
  loading.value = true; error.value = ''
  try {
    await auth.login(username.value.trim(), password.value)
    await router.replace(String(route.query.redirect ?? '/monitor'))
  } catch (value) { error.value = value instanceof Error ? value.message : '登录失败' }
  finally { loading.value = false }
}
</script>
<template>
  <div class="login-page">
    <form class="login-card" @submit.prevent="submit">
      <div class="brand login-brand"><span class="brand-mark">矿</span><div><b>矿车载运安全监测系统</b><small>中心查询与报警平台</small></div></div>
      <label>用户名<input v-model="username" autocomplete="username" required /></label>
      <label>密码<input v-model="password" type="password" autocomplete="current-password" required /></label>
      <p v-if="error" class="error-text">{{ error }}</p>
      <button class="primary" :disabled="loading">{{ loading ? '正在登录…' : '登录' }}</button>
    </form>
  </div>
</template>

