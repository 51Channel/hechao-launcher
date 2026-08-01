import { nextTick } from "vue";
import { mount } from "@vue/test-utils";
import { describe, expect, it } from "vitest";
import ConfirmDialog from "@/components/ConfirmDialog.vue";

describe("ConfirmDialog", () => {
  it("requires both an audit reason and exact confirmation text", async () => {
    const wrapper = mount(ConfirmDialog, {
      attachTo: document.body,
      props: {
        open: true,
        title: "停止服务器",
        message: "测试确认边界",
        requireReason: true,
        confirmationText: "activity"
      }
    });
    await nextTick();

    await wrapper.find("textarea").setValue("abc");
    await wrapper.find("input").setValue("wrong");
    await wrapper.find("form").trigger("submit");
    expect(wrapper.emitted("confirm")).toBeUndefined();
    expect(wrapper.text()).toContain("操作原因至少需要 4 个字符");

    await wrapper.find("textarea").setValue("planned maintenance");
    await wrapper.find("form").trigger("submit");
    expect(wrapper.emitted("confirm")).toBeUndefined();
    expect(wrapper.text()).toContain("二次确认内容不匹配");

    await wrapper.find("input").setValue("activity");
    await wrapper.find("form").trigger("submit");
    expect(wrapper.emitted("confirm")).toEqual([[
      { reason: "planned maintenance", confirmation: "activity" }
    ]]);
    wrapper.unmount();
  });

  it("cannot be dismissed while a confirmed operation is still running", async () => {
    const wrapper = mount(ConfirmDialog, {
      attachTo: document.body,
      props: {
        open: true,
        title: "重启服务器",
        message: "正在提交操作",
        busy: true
      }
    });
    await nextTick();

    await wrapper.find("dialog").trigger("cancel");
    expect(wrapper.emitted("close")).toBeUndefined();
    wrapper.unmount();
  });
});
