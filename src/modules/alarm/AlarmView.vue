<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { acknowledgeAlarm, queryAlarms } from '../../api/truck'
import type { Alarm, AlarmStatus, Page } from '../../api/types'
import StatusBadge from '../../components/StatusBadge.vue'

const page = ref<Page<Alarm>>({ content: [], totalElements: 0, totalPages: 0, number: 0, size: 20 })
const status = ref<AlarmStatus | ''>('OPEN')
const error = ref('')
const time = (value?: string) => value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
async function load(number = 0) { try { page.value = await queryAlarms(status.value || undefined, number) } catch (value) { error.value = value instanceof Error ? value.message : '加载失败' } }
async function acknowledge(alarm: Alarm) {
  const note = window.prompt('请输入处置意见（可留空）', '')
  if (note === null) return
  try { await acknowledgeAlarm(alarm.alarmId, note); await load(page.value.number) }
  catch (value) { error.value = value instanceof Error ? value.message : '确认失败' }
}
onMounted(() => load())
</script>
<template>
  <section>
    <header class="page-header"><div><h1>报警处置</h1><p>报警确认会记录操作人、时间和处置意见</p></div></header>
    <div class="filters"><select v-model="status" @change="load()"><option value="">全部状态</option><option value="OPEN">未处置</option><option value="ACKNOWLEDGED">已确认</option></select></div>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <div class="panel table-wrap"><table><thead><tr><th>报警时间</th><th>车牌</th><th>类型</th><th>状态</th><th>处置</th></tr></thead><tbody>
      <tr v-for="alarm in page.content" :key="alarm.alarmId"><td>{{ time(alarm.createdAt) }}</td><td class="plate">{{ alarm.plateNo }}</td><td>{{ alarm.alarmTypes.join('、') || '几何报警' }}</td><td><StatusBadge :value="alarm.status" /></td><td><button v-if="alarm.status === 'OPEN'" class="primary compact" @click="acknowledge(alarm)">确认处置</button><span v-else>{{ alarm.acknowledgedBy }} · {{ alarm.note || '无意见' }}</span></td></tr>
      <tr v-if="!page.content.length"><td colspan="5" class="empty">暂无报警</td></tr>
    </tbody></table></div>
  </section>
</template>

