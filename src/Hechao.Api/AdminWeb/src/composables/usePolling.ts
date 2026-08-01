import { onMounted, onScopeDispose } from "vue";

export function usePolling(refresh: () => Promise<unknown>, intervalMs: number, immediate = true): void {
  let timer: number | null = null;
  let running = false;
  const tick = async () => {
    if (document.hidden || running) return;
    running = true;
    try {
      await refresh();
    } finally {
      running = false;
    }
  };
  const onVisibilityChange = () => {
    if (!document.hidden) void tick();
  };

  onMounted(() => {
    if (immediate) void tick();
    timer = window.setInterval(() => { void tick(); }, intervalMs);
    document.addEventListener("visibilitychange", onVisibilityChange);
  });
  onScopeDispose(() => {
    if (timer !== null) window.clearInterval(timer);
    document.removeEventListener("visibilitychange", onVisibilityChange);
  });
}
