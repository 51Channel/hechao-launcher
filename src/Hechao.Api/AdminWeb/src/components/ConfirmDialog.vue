<script setup lang="ts">
import { nextTick, ref, watch } from "vue";
import AppIcon from "./AppIcon.vue";

const props = withDefaults(defineProps<{
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
  busy?: boolean;
  requireReason?: boolean;
  reasonLabel?: string;
  reasonPlaceholder?: string;
  confirmationText?: string;
  confirmationLabel?: string;
  error?: string;
}>(), {
  confirmLabel: "确认",
  danger: false,
  busy: false,
  requireReason: false,
  reasonLabel: "操作原因",
  reasonPlaceholder: "至少 4 个字符，将写入审计日志",
  confirmationText: "",
  confirmationLabel: "二次确认",
  error: ""
});

const emit = defineEmits<{
  close: [];
  confirm: [payload: { reason: string; confirmation: string }];
}>();

const dialog = ref<HTMLDialogElement | null>(null);
const reason = ref("");
const confirmation = ref("");
const validationError = ref("");

watch(() => props.open, async value => {
  if (value) {
    reason.value = "";
    confirmation.value = "";
    validationError.value = "";
    await nextTick();
    if (!dialog.value?.open) dialog.value?.showModal();
  } else if (dialog.value?.open) {
    dialog.value.close();
  }
});

function submit(): void {
  if (props.requireReason && reason.value.trim().length < 4) {
    validationError.value = "操作原因至少需要 4 个字符。";
    return;
  }
  if (props.confirmationText && confirmation.value.trim() !== props.confirmationText) {
    validationError.value = `二次确认内容不匹配，请完整输入“${props.confirmationText}”。`;
    return;
  }
  validationError.value = "";
  emit("confirm", { reason: reason.value.trim(), confirmation: confirmation.value.trim() });
}

function close(): void {
  if (!props.busy) emit("close");
}
</script>

<template>
  <dialog ref="dialog" class="confirm-dialog vue-dialog" @cancel.prevent="close">
    <form @submit.prevent="submit">
      <div class="confirm-icon"><AppIcon :name="danger ? 'circle-alert' : 'check'" :size="22" /></div>
      <h2>{{ title }}</h2>
      <p>{{ message }}</p>
      <slot></slot>
      <div v-if="error || validationError" class="inline-alert" role="alert">
        <AppIcon name="circle-alert" /><span>{{ error || validationError }}</span>
      </div>
      <label v-if="requireReason" class="security-action-field">
        {{ reasonLabel }}
        <textarea v-model="reason" minlength="4" maxlength="500" rows="3" required :placeholder="reasonPlaceholder" @input="validationError = ''"></textarea>
      </label>
      <label v-if="confirmationText" class="security-action-field">
        {{ confirmationLabel }}
        <input v-model="confirmation" type="text" maxlength="80" autocomplete="off" required @input="validationError = ''">
        <span>请输入：{{ confirmationText }}</span>
      </label>
      <div class="confirm-actions">
        <button class="button button-secondary" type="button" :disabled="busy" @click="close">取消</button>
        <button class="button" :class="danger ? 'button-danger' : 'button-primary'" type="submit" :disabled="busy">
          {{ busy ? "处理中…" : confirmLabel }}
        </button>
      </div>
    </form>
  </dialog>
</template>
