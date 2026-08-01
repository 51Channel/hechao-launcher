<script setup lang="ts">
import { ref } from "vue";
import { api } from "@/api/client";
import type { AdminSession } from "@/api/types";
import { showToast } from "@/composables/useToast";
import { brandMarkUrl, formatDateTime } from "@/utils";
import AppIcon from "./AppIcon.vue";

const props = defineProps<{ session: AdminSession }>();
const emit = defineEmits<{ authenticated: []; logout: [] }>();

const code = ref("");
const trustDevice = ref(false);
const busy = ref(false);
const error = ref("");
const enrollment = ref<{ secretKey: string; qrCodeDataUri: string; expiresAt: string } | null>(null);
const recoveryCodes = ref<string[]>([]);

interface MfaVerification {
  verified: boolean;
  verifiedAt: string;
  recoveryCodes?: string[];
  recoveryCodeUsed?: boolean;
}

async function beginEnrollment(): Promise<void> {
  busy.value = true; error.value = "";
  try {
    enrollment.value = await api<{ secretKey: string; qrCodeDataUri: string; expiresAt: string }>(
      "/v1/admin-auth/mfa/enrollment",
      { method: "POST", body: {} }
    );
  } catch (reason) { error.value = reason instanceof Error ? reason.message : "无法开始设置。"; }
  finally { busy.value = false; }
}

async function verify(): Promise<void> {
  busy.value = true; error.value = "";
  try {
    const configured = props.session.mfaConfigured;
    const path = props.session.mfaConfigured
      ? "/v1/admin-auth/mfa/verify"
      : "/v1/admin-auth/mfa/enrollment/confirm";
    const result = await api<MfaVerification>(path, { method: "POST", body: { code: code.value } });
    recoveryCodes.value = result.recoveryCodes ?? [];
    code.value = "";
    if (!configured && recoveryCodes.value.length > 0) return;
    if (result.recoveryCodeUsed) showToast("恢复码已使用，请及时补充新的恢复方案");
    await finishAuthentication();
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : "验证失败。";
    code.value = "";
  } finally { busy.value = false; }
}

async function tryTrustSelectedDevice(): Promise<string> {
  if (!trustDevice.value) return "";
  try {
    await api("/v1/admin-auth/trusted-device", { method: "POST", body: {} });
    return "";
  } catch (reason) {
    return reason instanceof Error ? reason.message : "本机信任设置失败。";
  }
}

async function finishAuthentication(): Promise<void> {
  const trustWarning = await tryTrustSelectedDevice();
  emit("authenticated");
  if (trustWarning) showToast(`已完成验证，但本机信任设置失败：${trustWarning}`, true);
}

async function finishEnrollment(): Promise<void> {
  busy.value = true;
  try {
    await finishAuthentication();
    recoveryCodes.value = [];
  } finally {
    busy.value = false;
  }
}

async function copyRecoveryCodes(): Promise<void> {
  try {
    await navigator.clipboard.writeText(recoveryCodes.value.join("\n"));
    showToast("恢复码已复制");
  } catch {
    showToast("无法访问剪贴板，请使用下载文本保存恢复码。", true);
  }
}

async function copySecretKey(): Promise<void> {
  if (!enrollment.value) return;
  try {
    await navigator.clipboard.writeText(enrollment.value.secretKey);
    showToast("验证器密钥已复制");
  } catch {
    showToast("无法访问剪贴板，请手动选择并复制密钥。", true);
  }
}

function downloadRecoveryCodes(): void {
  const blob = new Blob([recoveryCodes.value.join("\r\n")], { type: "text/plain;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url; anchor.download = `hechao-admin-recovery-${new Date().toISOString().slice(0, 10)}.txt`;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}
</script>

<template>
  <main class="center-view auth-view">
    <section class="auth-panel auth-panel-wide">
      <img class="auth-brand" :src="brandMarkUrl" alt="赫朝">
      <span class="auth-kicker">{{ session.player.minecraftName }}</span>
      <h1>{{ session.mfaConfigured ? "完成双重验证" : "设置双重验证" }}</h1>
      <p>{{ session.mfaConfigured ? "输入验证器动态码或一枚恢复码。" : "使用验证器扫描二维码，然后输入动态码。" }}</p>

      <div v-if="error" class="inline-alert" role="alert"><AppIcon name="circle-alert" /><span>{{ error }}</span></div>

      <template v-if="!session.mfaConfigured && !enrollment">
        <button class="button button-primary" type="button" :disabled="busy" @click="beginEnrollment">开始设置</button>
      </template>
      <template v-else-if="recoveryCodes.length === 0">
        <div v-if="enrollment" class="enrollment-layout">
          <img class="qr-code" :src="enrollment.qrCodeDataUri" alt="验证器二维码">
          <div>
            <label>手动密钥<div class="copy-field"><input :value="enrollment.secretKey" readonly><button class="icon-button" type="button" title="复制密钥" aria-label="复制验证器密钥" @click="copySecretKey"><AppIcon name="copy" /></button></div></label>
            <span class="field-hint">{{ formatDateTime(enrollment.expiresAt) }} 前有效</span>
          </div>
        </div>
        <form class="mfa-form" @submit.prevent="verify">
          <label>动态码或恢复码<input v-model="code" autocomplete="one-time-code" minlength="6" maxlength="32" required autofocus></label>
          <label class="checkbox-row trusted-device-option"><input v-model="trustDevice" type="checkbox"><span>信任这台电脑 30 天</span></label>
          <div class="form-actions"><button class="button button-secondary" type="button" @click="$emit('logout')">退出</button><button class="button button-primary" type="submit" :disabled="busy">{{ busy ? "验证中…" : "验证并进入" }}</button></div>
        </form>
      </template>
      <template v-else>
        <div class="recovery-code-list"><code v-for="item in recoveryCodes" :key="item">{{ item }}</code></div>
        <p>每枚恢复码只能使用一次，关闭后无法再次查看。</p>
        <div class="form-actions">
          <button class="button button-secondary" type="button" @click="copyRecoveryCodes">复制全部</button>
          <button class="button button-secondary" type="button" @click="downloadRecoveryCodes">下载文本</button>
          <button class="button button-primary" type="button" :disabled="busy" @click="finishEnrollment">{{ busy ? "正在进入…" : "已安全保存" }}</button>
        </div>
      </template>
    </section>
  </main>
</template>
