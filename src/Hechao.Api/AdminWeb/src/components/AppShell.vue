<script setup lang="ts">
import { computed, ref } from "vue";
import { api } from "@/api/client";
import type { AdminSession } from "@/api/types";
import { refreshCurrentPage } from "@/composables/usePageRefresh";
import { showToast, toast } from "@/composables/useToast";
import { brandMarkUrl, tierText } from "@/utils";
import AppIcon from "./AppIcon.vue";

const props = defineProps<{ session: AdminSession }>();
const emit = defineEmits<{ logout: [] }>();
const refreshing = ref(false);
const initial = computed(() => props.session.player.minecraftName.slice(0, 1).toUpperCase());
const nav = [
  ["servers", "server", "服务器目录"], ["users", "users", "玩家与权限"],
  ["profiles", "package", "客户端档案"], ["telemetry", "activity", "运行数据"],
  ["package-imports", "package", "整合包导入"],
  ["activity-plans", "activity", "活动企划"],
  ["runtime", "monitor", "服务状态"], ["control", "server", "服控面板"],
  ["alerts", "circle-alert", "告警中心"], ["diagnostics", "activity", "诊断包"],
  ["audit", "scroll-text", "审计记录"]
] as const;

async function refresh(): Promise<void> {
  refreshing.value = true;
  try { await refreshCurrentPage(); } finally { refreshing.value = false; }
}

async function trustDevice(): Promise<void> {
  try {
    const result = await api<{ expiresAt: string }>("/v1/admin-auth/trusted-device", { method: "POST", body: {} });
    showToast(`此电脑已受信任至 ${new Date(result.expiresAt).toLocaleDateString("zh-CN")}`);
  } catch (reason) { showToast(reason instanceof Error ? reason.message : "信任设置失败。", true); }
}
</script>

<template>
  <div class="console-shell vue-console-shell">
    <aside class="sidebar">
      <div class="sidebar-brand"><img class="brand-mark" :src="brandMarkUrl" alt="赫朝"><div><strong>赫朝</strong><span>管理控制台</span></div></div>
      <nav class="primary-nav" aria-label="管理模块">
        <RouterLink v-for="item in nav" :key="item[0]" class="nav-item" :to="{ name: item[0] }" :aria-label="item[2]">
          <AppIcon :name="item[1]" /><span>{{ item[2] }}</span>
        </RouterLink>
      </nav>
      <div class="sidebar-boundary"><AppIcon name="shield-check" /><div><strong>最小权限服控</strong><span>仅允许受管服务器与 Minecraft 命令</span></div></div>
      <div class="sidebar-account">
        <div class="account-avatar">{{ initial }}</div>
        <div class="account-copy"><strong>{{ session.player.minecraftName }}</strong><span>{{ tierText(session.player.accessTier) }} · {{ session.player.luckPermsPrimaryGroup }}</span></div>
        <button class="icon-button" type="button" title="信任这台电脑" aria-label="信任这台电脑" @click="trustDevice"><AppIcon name="shield-check" /></button>
        <button class="icon-button" type="button" title="退出管理后台" aria-label="退出管理后台" @click="$emit('logout')"><AppIcon name="log-out" /></button>
      </div>
    </aside>
    <section class="workspace">
      <header class="topbar">
        <span class="breadcrumb">赫朝管理控制台</span>
        <div class="topbar-actions"><button class="button button-secondary" type="button" :disabled="refreshing" @click="refresh"><AppIcon name="refresh-cw" />{{ refreshing ? "刷新中" : "刷新" }}</button></div>
      </header>
      <main class="content"><RouterView /></main>
    </section>
    <div v-if="toast.visible" class="toast" :class="{ error: toast.error }" :role="toast.error ? 'alert' : 'status'" aria-live="polite">
      <AppIcon :name="toast.error ? 'circle-alert' : 'check'" /><span>{{ toast.message }}</span>
    </div>
  </div>
</template>
