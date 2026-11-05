<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { queryDevices } from '../../api/truck'
import type { Device } from '../../api/types'

const devices = ref<Device[]>([])
const error = ref('')
let timer: number | undefined
const time = (value?: string) => value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '从未上报'
async function load() { try { devices.value = await queryDevices() } catch (value) { error.value = value instanceof Error ? value.message : '加载失败' } }
onMounted(() => { load(); timer = window.setInterval(load, 10_000) })
onUnmounted(() => window.clearInterval(timer))
</script>
<template>
  <section><header class="page-header"><div><h1>设备与同步状态</h1><p>每10秒刷新终端心跳、磁盘和待上传数量</p></div></header>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <div class="device-grid"><article v-for="device in devices" :key="device.deviceId" class="panel device-card"><div class="panel-title"><h2>{{ device.deviceId }}</h2><span :class="['online-state', device.online ? 'online' : 'offline']">{{ device.online ? '在线' : '离线' }}</span></div><dl><dt>最后心跳</dt><dd>{{ time(device.lastSeenAt) }}</dd><dt>磁盘使用</dt><dd>{{ device.diskUsagePercent?.toFixed(1) ?? '—' }}%</dd><dt>待同步事件</dt><dd>{{ device.pendingSyncCount ?? '—' }}</dd><dt>设备状态</dt><dd>{{ device.enabled ? '已启用' : '已停用' }}</dd></dl></article><p v-if="!devices.length" class="empty">暂无已注册终端</p></div>
  </section>
</template>

