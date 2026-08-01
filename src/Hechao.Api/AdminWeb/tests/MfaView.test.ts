import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as apiClient from "@/api/client";
import type { AdminSession } from "@/api/types";
import MfaView from "@/components/MfaView.vue";
import { toast } from "@/composables/useToast";

const configuredSession: AdminSession = {
  player: {
    userId: "11111111-1111-1111-1111-111111111111",
    minecraftUuid: "22222222-2222-2222-2222-222222222222",
    minecraftName: "HechaoAdmin",
    luckPermsPrimaryGroup: "administrator",
    accessTier: "Administrator",
    luckPermsSyncedAt: "2026-08-02T03:00:00Z"
  },
  mfaConfigured: true,
  mfaVerified: false,
  expiresAt: "2026-08-02T08:00:00Z"
};

afterEach(() => {
  vi.restoreAllMocks();
  toast.visible = false;
  toast.message = "";
  toast.error = false;
});

describe("MfaView", () => {
  it("enters the console when MFA succeeds even if trusting the device fails", async () => {
    vi.spyOn(apiClient, "api")
      .mockResolvedValueOnce({
        verified: true,
        verifiedAt: "2026-08-02T04:00:00Z",
        recoveryCodeUsed: false
      } as never)
      .mockRejectedValueOnce(new Error("可信设备写入失败"));
    const wrapper = mount(MfaView, {
      props: { session: configuredSession }
    });

    await wrapper.get("input[autocomplete='one-time-code']").setValue("123456");
    await wrapper.get("input[type='checkbox']").setValue(true);
    await wrapper.get("form").trigger("submit");
    await flushPromises();

    expect(wrapper.emitted("authenticated")).toHaveLength(1);
    expect(toast.error).toBe(true);
    expect(toast.message).toContain("已完成验证，但本机信任设置失败");
    wrapper.unmount();
  });

  it("waits until recovery codes are saved before trusting the device", async () => {
    const api = vi.spyOn(apiClient, "api")
      .mockResolvedValueOnce({
        secretKey: "ABC123",
        qrCodeDataUri: "data:image/png;base64,AA==",
        expiresAt: "2026-08-02T04:10:00Z"
      } as never)
      .mockResolvedValueOnce({
        verified: true,
        verifiedAt: "2026-08-02T04:00:00Z",
        recoveryCodes: ["first-code", "second-code"]
      } as never)
      .mockResolvedValueOnce({ expiresAt: "2026-09-01T04:00:00Z" } as never);
    const wrapper = mount(MfaView, {
      props: {
        session: { ...configuredSession, mfaConfigured: false }
      }
    });

    await wrapper.get("button.button-primary").trigger("click");
    await flushPromises();
    await wrapper.get("input[autocomplete='one-time-code']").setValue("123456");
    await wrapper.get("input[type='checkbox']").setValue(true);
    await wrapper.get("form").trigger("submit");
    await flushPromises();

    expect(api).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).toContain("first-code");
    expect(wrapper.emitted("authenticated")).toBeUndefined();

    await wrapper.get("button.button-primary").trigger("click");
    await flushPromises();
    expect(api).toHaveBeenCalledTimes(3);
    expect(api.mock.calls[2][0]).toBe("/v1/admin-auth/trusted-device");
    expect(wrapper.emitted("authenticated")).toHaveLength(1);
    wrapper.unmount();
  });
});
