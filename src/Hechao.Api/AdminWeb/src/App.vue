<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from "vue";
import { api, resetCsrfToken } from "@/api/client";
import type { AdminSession } from "@/api/types";
import { takeInitialAdminTicket } from "@/auth/initialTicket";
import AppShell from "@/components/AppShell.vue";
import MfaView from "@/components/MfaView.vue";
import SignInView from "@/components/SignInView.vue";
import { brandMarkUrl } from "@/utils";

type Phase = "loading" | "signin" | "mfa" | "console";
const phase = ref<Phase>("loading");
const session = ref<AdminSession | null>(null);
const signInMessage = ref("");
let initializationGeneration = 0;
let signInPollTimer: number | null = null;
let recoveryPromise: Promise<void> | null = null;

const signInPollIntervalMs = 5_000;

async function initialize(quiet = false): Promise<void> {
  const generation = ++initializationGeneration;
  const keepSignInVisible = quiet && phase.value === "signin";
  if (!keepSignInVisible) {
    phase.value = "loading";
    signInMessage.value = "";
  }
  const ticket = takeInitialAdminTicket();
  if (ticket) {
    try {
      await api("/v1/admin-auth/redeem", { method: "POST", body: { ticket }, csrf: false });
    } catch (reason) {
      if (generation !== initializationGeneration) return;
      phase.value = "signin";
      signInMessage.value = reason instanceof Error ? reason.message : "后台登录票据无效。";
      return;
    }
  }
  try {
    const result = await api<AdminSession>("/v1/admin-auth/session", { csrf: false });
    if (generation !== initializationGeneration) return;
    session.value = result;
    phase.value = result.mfaVerified ? "console" : "mfa";
  } catch (reason) {
    if (generation !== initializationGeneration) return;
    session.value = null;
    phase.value = "signin";
    if (!keepSignInVisible) {
      signInMessage.value = reason instanceof Error ? reason.message : "需要管理员身份。";
    }
  }
}

function recoverSession(): void {
  if (phase.value !== "signin" || document.visibilityState === "hidden" || recoveryPromise) {
    return;
  }

  recoveryPromise = initialize(true).finally(() => {
    recoveryPromise = null;
  });
}

function updateSignInPolling(nextPhase: Phase): void {
  if (signInPollTimer !== null) {
    window.clearInterval(signInPollTimer);
    signInPollTimer = null;
  }

  if (nextPhase === "signin") {
    signInPollTimer = window.setInterval(recoverSession, signInPollIntervalMs);
  }
}

function onVisibilityChange(): void {
  if (document.visibilityState === "visible") recoverSession();
}

async function authenticated(): Promise<void> {
  await initialize();
}

async function logout(): Promise<void> {
  try { await api("/v1/admin-auth/logout", { method: "POST", body: {} }); } catch { /* cookie is cleared when possible */ }
  resetCsrfToken();
  session.value = null;
  phase.value = "signin";
}

const onSessionExpired = () => {
  resetCsrfToken();
  void initialize();
};
onMounted(() => {
  window.addEventListener("hechao:admin-session-expired", onSessionExpired);
  window.addEventListener("focus", recoverSession);
  window.addEventListener("pageshow", recoverSession);
  document.addEventListener("visibilitychange", onVisibilityChange);
  void initialize();
});
watch(phase, updateSignInPolling);
onBeforeUnmount(() => {
  window.removeEventListener("hechao:admin-session-expired", onSessionExpired);
  window.removeEventListener("focus", recoverSession);
  window.removeEventListener("pageshow", recoverSession);
  document.removeEventListener("visibilitychange", onVisibilityChange);
  if (signInPollTimer !== null) window.clearInterval(signInPollTimer);
});
</script>

<template>
  <main v-if="phase === 'loading'" class="center-view" aria-live="polite">
    <img class="loading-mark" :src="brandMarkUrl" alt="赫朝">
    <strong>正在核验管理员会话</strong>
  </main>
  <SignInView v-else-if="phase === 'signin'" :message="signInMessage" @retry="initialize" />
  <MfaView v-else-if="phase === 'mfa' && session" :session="session" @authenticated="authenticated" @logout="logout" />
  <AppShell v-else-if="session" :session="session" @logout="logout" />
</template>
