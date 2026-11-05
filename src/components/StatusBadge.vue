<script setup lang="ts">
defineProps<{ value?: string }>()
const label: Record<string, string> = {
  NORMAL: '正常', ALARM: '报警', REVIEW_REQUIRED: '待复核',
  OPEN: '未处置', ACKNOWLEDGED: '已确认', GOOD: '良好', PARTIAL: '部分有效',
}
</script>
<template><span class="badge" :class="`badge-${value?.toLowerCase()}`">{{ label[value ?? ''] ?? value ?? '—' }}</span></template>

