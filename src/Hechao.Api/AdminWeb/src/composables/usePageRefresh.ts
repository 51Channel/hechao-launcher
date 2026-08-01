import { shallowRef } from "vue";

type RefreshHandler = () => Promise<unknown>;
const handler = shallowRef<RefreshHandler | null>(null);

export function registerPageRefresh(refresh: RefreshHandler): () => void {
  handler.value = refresh;
  return () => {
    if (handler.value === refresh) handler.value = null;
  };
}

export async function refreshCurrentPage(): Promise<void> {
  await handler.value?.();
}
