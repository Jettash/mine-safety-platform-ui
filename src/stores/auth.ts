import { defineStore } from 'pinia'
import { api, ensureCsrf } from '../api/http'

export const useAuthStore = defineStore('auth', {
  state: () => ({ username: '' as string, checked: false }),
  getters: { authenticated: (state) => Boolean(state.username) },
  actions: {
    async check() {
      if (this.checked) return this.authenticated
      try {
        this.username = (await api<{ username: string }>('/api/v1/auth/me')).username
      } catch {
        this.username = ''
      } finally {
        this.checked = true
      }
      return this.authenticated
    },
    async login(username: string, password: string) {
      const token = await ensureCsrf(true)
      const response = await fetch('/api/v1/auth/login', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', [token.headerName]: token.token },
        body: new URLSearchParams({ username, password }),
      })
      if (!response.ok) throw new Error('用户名或密码错误')
      this.username = (await response.json()).username
      this.checked = true
    },
    async logout() {
      await api<void>('/api/v1/auth/logout', { method: 'POST' })
      this.username = ''
      this.checked = true
    },
  },
})

