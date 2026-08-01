<script setup lang="ts">
import AppIcon from "./AppIcon.vue";

defineProps<{
  loading: boolean;
  error: string;
  empty?: boolean;
  emptyTitle?: string;
  emptyMessage?: string;
}>();
defineEmits<{ retry: [] }>();
</script>

<template>
  <div v-if="loading" class="resource-state resource-loading" role="status" aria-live="polite">
    <div class="skeleton-line skeleton-wide"></div>
    <div class="skeleton-line"></div>
    <span>正在读取最新数据</span>
  </div>
  <div v-else-if="error" class="resource-state resource-error" role="alert">
    <AppIcon name="circle-alert" />
    <strong>数据暂时不可用</strong>
    <span>{{ error }}</span>
    <button class="button button-secondary" type="button" @click="$emit('retry')">重新加载</button>
  </div>
  <div v-else-if="empty" class="resource-state resource-empty">
    <AppIcon name="database" :size="24" />
    <strong>{{ emptyTitle || "暂无数据" }}</strong>
    <span v-if="emptyMessage">{{ emptyMessage }}</span>
  </div>
  <slot v-else />
</template>
