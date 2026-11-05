<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { queryEvents } from '../../api/truck'
import type { DetectionResult, Page, TruckEvent } from '../../api/types'
import EventTable from '../../components/EventTable.vue'

const page = ref<Page<TruckEvent>>({ content: [], totalElements: 0, totalPages: 0, number: 0, size: 20 })
const plateNo = ref('')
const result = ref<DetectionResult | ''>('')
const loading = ref(false)
const error = ref('')
async function load(number = 0) {
  loading.value = true; error.value = ''
  try { page.value = await queryEvents({ page: number, plateNo: plateNo.value.trim(), result: result.value || undefined }) }
  catch (value) { error.value = value instanceof Error ? value.message : '加载失败' }
  finally { loading.value = false }
}
onMounted(() => load())
</script>
<template>
  <section>
    <header class="page-header"><div><h1>车辆历史记录</h1><p>按车牌和判定结果查询服务器数据</p></div></header>
    <div class="filters">
      <input v-model="plateNo" placeholder="输入车牌号" @keyup.enter="load()" />
      <select v-model="result"><option value="">全部结果</option><option value="NORMAL">正常</option><option value="ALARM">报警</option><option value="REVIEW_REQUIRED">待复核</option></select>
      <button class="primary compact" @click="load()">查询</button>
    </div>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <div class="panel"><EventTable :events="page.content" /><div class="pager"><span>共 {{ page.totalElements }} 条</span><button :disabled="page.number === 0 || loading" @click="load(page.number - 1)">上一页</button><span>{{ page.number + 1 }} / {{ Math.max(page.totalPages, 1) }}</span><button :disabled="page.number + 1 >= page.totalPages || loading" @click="load(page.number + 1)">下一页</button></div></div>
  </section>
</template>

