import { defineComponent, nextTick } from "vue";
import { mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import { usePolling } from "@/composables/usePolling";

describe("usePolling", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("does not overlap a slow refresh", async () => {
    vi.useFakeTimers();
    let resolveFirst!: () => void;
    const first = new Promise<void>(resolve => { resolveFirst = resolve; });
    const refresh = vi.fn()
      .mockImplementationOnce(() => first)
      .mockResolvedValue(undefined);
    const component = defineComponent({
      setup() {
        usePolling(refresh, 100);
        return () => null;
      }
    });
    const wrapper = mount(component);
    await nextTick();

    expect(refresh).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(400);
    expect(refresh).toHaveBeenCalledTimes(1);

    resolveFirst();
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(100);
    expect(refresh).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });
});
