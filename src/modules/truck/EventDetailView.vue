<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { getEvent } from '../../api/truck'
import type { TruckEventDetail } from '../../api/types'
import StatusBadge from '../../components/StatusBadge.vue'

const route = useRoute()
const detail = ref<TruckEventDetail>()
const error = ref('')
const media = computed(() => Object.fromEntries((detail.value?.media ?? []).map((item) => [item.fileType, item])))
const time = (value?: string) => value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
onMounted(async () => { try { detail.value = await getEvent(String(route.params.eventId)) } catch (value) { error.value = value instanceof Error ? value.message : '加载失败' } })
</script>
<template>
  <section>
    <header class="page-header"><div><h1>事件详情</h1><p class="mono">{{ route.params.eventId }}</p></div><RouterLink class="button" to="/events">返回列表</RouterLink></header>
    <p v-if="error" class="error-banner">{{ error }}</p>
    <template v-if="detail">
      <div class="detail-grid panel">
        <div><span>车牌号</span><strong class="plate">{{ detail.event.plateNo }}</strong></div><div><span>判定结果</span><StatusBadge :value="detail.event.result" /></div>
        <div><span>通过时间</span><strong>{{ time(detail.event.passEndTime) }}</strong></div><div><span>数据质量</span><StatusBadge :value="detail.event.quality" /></div>
        <div><span>体积估算</span><strong>{{ detail.event.loadVolumeM3?.toFixed(2) ?? '—' }} m³</strong></div><div><span>最大突出高度</span><strong>{{ detail.event.maxHeightMm?.toFixed(1) ?? '—' }} mm</strong></div>
        <div><span>左右体积比</span><strong>{{ detail.event.leftRightRatio?.toFixed(3) ?? '—' }}</strong></div><div><span>横向质心偏移</span><strong>{{ detail.event.centerOffsetMm?.toFixed(1) ?? '—' }} mm</strong></div>
        <div><span>算法版本</span><strong>{{ detail.event.algorithmVersion }}</strong></div><div><span>标定版本</span><strong>{{ detail.event.calibrationId }}</strong></div>
        <div class="wide"><span>质量说明</span><strong>{{ detail.event.qualityReason || '无' }}</strong></div>
      </div>
      <div class="image-grid">
        <div class="panel"><h2>车牌抓拍</h2><img v-if="media.PLATE_IMAGE" :src="`/api/v1/media/${media.PLATE_IMAGE.fileId}`" alt="车牌抓拍" /><p v-else class="empty">无车牌图片</p></div>
        <div class="panel"><h2>车顶抓拍</h2><img v-if="media.TOP_IMAGE" :src="`/api/v1/media/${media.TOP_IMAGE.fileId}`" alt="车顶抓拍" /><p v-else class="empty">无车顶图片</p></div>
      </div>
    </template>
  </section>
</template>
