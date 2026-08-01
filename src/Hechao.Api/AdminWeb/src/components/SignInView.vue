<script setup lang="ts">
import AppIcon from "./AppIcon.vue";
import { brandMarkUrl } from "@/utils";
defineProps<{ message?: string }>();
defineEmits<{ retry: [] }>();

function closePage(): void {
  window.close();
}
</script>

<template>
  <main class="center-view auth-view">
    <section class="auth-panel">
      <img class="auth-brand" :src="brandMarkUrl" alt="赫朝">
      <span class="auth-kicker">管理控制台</span>
      <h1>需要管理员身份</h1>
      <p>请从赫朝启动器的管理员入口重新打开本页面。</p>
      <div v-if="message" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ message }}</span></div>
      <div class="form-actions auth-actions">
        <button class="button button-secondary" type="button" @click="closePage">关闭页面</button>
        <button class="button button-primary" type="button" @click="$emit('retry')"><AppIcon name="refresh-cw" />重新检查</button>
      </div>
    </section>
  </main>
</template>
