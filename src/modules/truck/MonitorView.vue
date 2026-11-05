<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { queryAlarms, queryDevices, queryEvents } from '../../api/truck'
import type { Device, TruckEvent } from '../../api/types'
import EventTable from '../../components/EventTable.vue'
import { useLiveEventsStore } from '../../stores/liveEvents'

const live = useLiveEventsStore()
const initial = ref<TruckEvent[]>([])
const devices = ref<Device[]>([])
const openAlarms = ref(0)
const error = ref('')
const events = computed(() => {
  const merged = [...live.events, ...initial.value]
  return merged.filter((item, index) => merged.findIndex((other) => other.eventId === item.eventId) === index).slice(0, 10)
})
onMounted(async () => {
  try {
    const [eventPage, alarmPage, deviceRows] = await Promise.all([
      queryEvents({ size: 10 }), queryAlarms('OPEN', 0, 1), queryDevices(),
    ])
    initial.value = eventPage.content; openAlarms.value = alarmPage.totalElements; devices.value = deviceRows
  } catch (value) { error.value = value instanceof Error ? value.message : '加载失败' }
})
</script>
<template>
  <section>
    <header class="page-header"><div><h1>综合监控</h1><p>只展示已在服务器完整提交的车辆事件</p></div></header>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <div class="metrics">
      <div class="metric"><span>最新车辆</span><strong>{{ events.length ? events[0].plateNo : '—' }}</strong></div>
      <div class="metric danger"><span>未处置报警</span><strong>{{ openAlarms }}</strong></div>
      <div class="metric"><span>在线终端</span><strong>{{ devices.filter((item) => item.online).length }}/{{ devices.length }}</strong></div>
      <div class="metric"><span>待同步总数</span><strong>{{ devices.reduce((sum, item) => sum + (item.pendingSyncCount ?? 0), 0) }}</strong></div>
    </div>
    <div class="panel"><div class="panel-title"><h2>最新过车</h2><span class="live-dot">{{ live.connected ? '实时连接' : '正在重连' }}</span></div><EventTable :events="events" /></div>
  </section>
</template>
