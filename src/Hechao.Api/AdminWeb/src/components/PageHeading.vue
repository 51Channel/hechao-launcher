<script setup lang="ts">
defineProps<{ title: string; description?: string; updatedAt?: Date | null; stale?: boolean }>();
</script>

<template>
  <div class="section-heading page-heading">
    <div>
      <h1>{{ title }}</h1>
      <p v-if="description">{{ description }}</p>
    </div>
    <div class="page-heading-meta">
      <slot name="actions"></slot>
      <span v-if="updatedAt" class="freshness" :class="{ stale }">
        {{ stale ? "数据可能已过期" : `更新于 ${updatedAt.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit", second: "2-digit" })}` }}
      </span>
    </div>
  </div>
</template>
