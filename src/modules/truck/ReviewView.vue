<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { queryEvents, reviewEvent } from '../../api/truck'
import type { DetectionResult, TruckEvent } from '../../api/types'
import EventTable from '../../components/EventTable.vue'

const events = ref<TruckEvent[]>([])
const error = ref('')
async function load() { try { events.value = (await queryEvents({ result: 'REVIEW_REQUIRED', size: 100 })).content } catch (value) { error.value = value instanceof Error ? value.message : '加载失败' } }
async function review(event: TruckEvent) {
  const plateNo = window.prompt('确认或修正车牌号', event.plateNo)
  if (!plateNo) return
  const result = window.prompt('最终结果：NORMAL 或 ALARM', 'NORMAL') as DetectionResult | null
  if (result !== 'NORMAL' && result !== 'ALARM') return
  const reason = window.prompt('复核原因（必填）', '')
  if (!reason) return
  try { await reviewEvent(event.eventId, { plateNo, result, reason }); await load() }
  catch (value) { error.value = value instanceof Error ? value.message : '复核失败' }
}
onMounted(load)
</script>
<template>
  <section><header class="page-header"><div><h1>待复核事件</h1><p>UNKNOWN、证据缺失或数据质量不足的车辆</p></div></header>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <div class="panel"><EventTable :events="events" /><div class="review-actions" v-for="event in events" :key="event.eventId"><span>{{ event.plateNo }} · {{ event.qualityReason || '需要人工确认' }}</span><button class="primary compact" @click="review(event)">人工复核</button></div></div>
  </section>
</template>

