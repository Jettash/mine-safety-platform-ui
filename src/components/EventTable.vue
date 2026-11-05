<script setup lang="ts">
import type { TruckEvent } from '../api/types'
import StatusBadge from './StatusBadge.vue'
defineProps<{ events: TruckEvent[] }>()
const time = (value?: string) => value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
const number = (value?: number, unit = '') => value == null ? '—' : `${value.toFixed(2)}${unit}`
</script>
<template>
  <div class="table-wrap">
    <table>
      <thead><tr><th>通过时间</th><th>车牌号</th><th>体积估算</th><th>最大突出</th><th>结果</th><th>数据质量</th><th></th></tr></thead>
      <tbody>
        <tr v-for="event in events" :key="`${event.eventId}-${event.eventVersion}`">
          <td>{{ time(event.passEndTime) }}</td><td class="plate">{{ event.plateNo }}</td>
          <td>{{ number(event.loadVolumeM3, ' m³') }}</td><td>{{ number(event.maxHeightMm, ' mm') }}</td>
          <td><StatusBadge :value="event.result" /></td><td><StatusBadge :value="event.quality" /></td>
          <td><RouterLink :to="`/events/${event.eventId}`">详情</RouterLink></td>
        </tr>
        <tr v-if="!events.length"><td colspan="7" class="empty">暂无数据</td></tr>
      </tbody>
    </table>
  </div>
</template>
