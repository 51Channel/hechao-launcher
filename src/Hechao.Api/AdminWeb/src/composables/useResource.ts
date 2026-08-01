import { onScopeDispose, ref, shallowRef, type Ref, type ShallowRef } from "vue";
import { ApiError } from "@/api/client";

export interface Resource<T> {
  data: ShallowRef<T | null>;
  loading: Ref<boolean>;
  error: Ref<string>;
  lastUpdatedAt: Ref<Date | null>;
  refresh: () => Promise<T | null>;
  cancel: () => void;
}

export function useResource<T>(loader: (signal: AbortSignal) => Promise<T>): Resource<T> {
  const data = shallowRef<T | null>(null);
  const loading = ref(false);
  const error = ref("");
  const lastUpdatedAt = ref<Date | null>(null);
  let generation = 0;
  let controller: AbortController | null = null;

  const cancel = () => {
    generation += 1;
    controller?.abort();
    controller = null;
  };

  const refresh = async (): Promise<T | null> => {
    const current = ++generation;
    controller?.abort();
    controller = new AbortController();
    loading.value = true;
    error.value = "";
    try {
      const result = await loader(controller.signal);
      if (current !== generation) return null;
      data.value = result;
      lastUpdatedAt.value = new Date();
      return result;
    } catch (reason) {
      if (current !== generation) return null;
      if (reason instanceof ApiError && reason.status === 0 && reason.message === "请求已取消。") return null;
      error.value = reason instanceof Error ? reason.message : "加载失败。";
      return null;
    } finally {
      if (current === generation) loading.value = false;
    }
  };

  onScopeDispose(cancel);
  return { data, loading, error, lastUpdatedAt, refresh, cancel };
}
