import { reactive } from "vue";

export const toast = reactive({
  visible: false,
  message: "",
  error: false
});

let timer: number | null = null;

export function showToast(message: string, error = false): void {
  if (timer !== null) window.clearTimeout(timer);
  toast.message = message;
  toast.error = error;
  toast.visible = true;
  timer = window.setTimeout(() => { toast.visible = false; }, error ? 6500 : 4200);
}
