<script setup lang="ts">
import { computed, onScopeDispose, reactive, ref, watch } from "vue";
import FullCalendar from "@fullcalendar/vue3";
import dayGridPlugin from "@fullcalendar/daygrid";
import interactionPlugin, {
  type DateClickArg,
  type EventResizeDoneArg
} from "@fullcalendar/interaction";
import zhCnLocale from "@fullcalendar/core/locales/zh-cn";
import type {
  CalendarOptions,
  DateSelectArg,
  EventClickArg,
  EventDropArg,
  EventInput
} from "@fullcalendar/core";
import { api } from "@/api/client";
import type {
  AccessTier,
  ActivityPackage,
  ActivityPlan,
  ActivityPlanOverview,
  ActivityPlanStatus,
  ControlQueueResult
} from "@/api/types";
import AppIcon from "@/components/AppIcon.vue";
import ConfirmDialog from "@/components/ConfirmDialog.vue";
import PageHeading from "@/components/PageHeading.vue";
import ResourceState from "@/components/ResourceState.vue";
import { registerPageRefresh } from "@/composables/usePageRefresh";
import { usePolling } from "@/composables/usePolling";
import { useResource } from "@/composables/useResource";
import { showToast } from "@/composables/useToast";
import {
  activityPlanStartTimeLabel,
  moveActivityPlanDates,
  resizeActivityPlanDates,
  toActivityPlanCalendarRange
} from "@/activityPlanCalendarDates";
import {
  formatDateTime,
  fromLocalDateTimeInput,
  tierText,
  toLocalDateTimeInput
} from "@/utils";

type EditorMode = "idle" | "create" | "edit";
type PlanFilter = "active" | "all" | "archived";
type PlanAction = "publish" | "withdraw" | "archive" | "restore" | "deploy";

interface PlanDraft {
  title: string;
  announcement: string;
  opensAt: string;
  closesAt: string;
  maximumPlayers: number;
  minimumTier: AccessTier;
  packageImportId: string;
}

const overview = useResource(signal =>
  api<ActivityPlanOverview>("/v1/admin/activity-plans", { signal })
);
const calendar = ref<InstanceType<typeof FullCalendar> | null>(null);
const calendarTitle = ref("");
const editorMode = ref<EditorMode>("idle");
const selectedPlanId = ref("");
const planFilter = ref<PlanFilter>("active");
const editorBaseline = ref("");
const editorBusy = ref(false);
const editorError = ref("");
const scheduleBusyPlanId = ref("");
const pendingAction = ref<PlanAction | null>(null);
const actionBusy = ref(false);
const actionError = ref("");
const draft = reactive<PlanDraft>({
  title: "",
  announcement: "",
  opensAt: "",
  closesAt: "",
  maximumPlayers: 30,
  minimumTier: "Participant",
  packageImportId: ""
});

const plans = computed(() => overview.data.value?.plans ?? []);
const packages = computed(() => overview.data.value?.packages ?? []);
const slot = computed(() => overview.data.value?.slot ?? null);
const selectedPlan = computed(() =>
  plans.value.find(plan => plan.id === selectedPlanId.value) ?? null
);
const selectedPackage = computed(() =>
  packages.value.find(item => item.importId === draft.packageImportId) ?? null
);
const publishedCount = computed(() =>
  plans.value.filter(plan => plan.status === "Published").length
);
const draftCount = computed(() =>
  plans.value.filter(plan => plan.status === "Draft").length
);
const upcomingCount = computed(() => {
  const now = Date.now();
  return plans.value.filter(plan =>
    plan.status === "Published" && new Date(plan.opensAt).getTime() > now
  ).length;
});
const visiblePlans = computed(() => plans.value.filter(plan => {
  if (planFilter.value === "archived") return plan.status === "Archived";
  if (planFilter.value === "active") return plan.status !== "Archived";
  return true;
}));
const editorDirty = computed(() =>
  editorMode.value !== "idle" && serializeDraft() !== editorBaseline.value
);
const draftStart = computed(() => fromLocalDateTimeInput(draft.opensAt));
const draftEnd = computed(() => fromLocalDateTimeInput(draft.closesAt));
const overlappingPublishedPlan = computed(() => {
  if (!draftStart.value || !draftEnd.value) return null;
  const start = new Date(draftStart.value).getTime();
  const end = new Date(draftEnd.value).getTime();
  if (!Number.isFinite(start) || !Number.isFinite(end) || start >= end) return null;
  return plans.value.find(plan =>
    plan.status === "Published" &&
    plan.id !== selectedPlanId.value &&
    start < new Date(plan.closesAt).getTime() &&
    new Date(plan.opensAt).getTime() < end
  ) ?? null;
});
const formValid = computed(() =>
  draft.title.trim().length >= 2 &&
  Boolean(draft.packageImportId) &&
  Boolean(draftStart.value) &&
  Boolean(draftEnd.value) &&
  new Date(draftStart.value!).getTime() < new Date(draftEnd.value!).getTime() &&
  draft.maximumPlayers >= 1 &&
  draft.maximumPlayers <= 1000
);
const saveDisabled = computed(() =>
  editorBusy.value ||
  !formValid.value ||
  (editorMode.value === "edit" && !editorDirty.value) ||
  (selectedPlan.value?.status === "Published" && Boolean(overlappingPublishedPlan.value))
);
const selectedPackageArchived = computed(() => selectedPackage.value?.profileArchived ?? false);
const canPublish = computed(() =>
  selectedPlan.value?.status === "Draft" &&
  !editorDirty.value &&
  Boolean(selectedPlan.value.productionReady) &&
  !selectedPackageArchived.value &&
  !overlappingPublishedPlan.value &&
  !editorBusy.value
);
const canDeploy = computed(() =>
  Boolean(selectedPlan.value) &&
  selectedPlan.value?.status !== "Archived" &&
  !editorDirty.value &&
  !selectedPlan.value?.deploymentMatches &&
  !selectedPackageArchived.value &&
  Boolean(slot.value?.configured) &&
  Boolean(slot.value?.agentConnected) &&
  !slot.value?.online &&
  !slot.value?.activeOperation &&
  !editorBusy.value
);

const calendarEvents = computed<EventInput[]>(() => visiblePlans.value.flatMap(plan => {
  const range = toActivityPlanCalendarRange(plan.opensAt, plan.closesAt);
  if (!range) return [];
  const editable = plan.status !== "Archived" &&
    scheduleBusyPlanId.value !== plan.id &&
    !(selectedPlanId.value === plan.id && editorDirty.value);
  const title = plan.status === "Published"
    ? plan.title
    : `${planStatusText(plan.status)} · ${plan.title}`;
  return [{
    id: plan.id,
    title: `${activityPlanStartTimeLabel(plan.opensAt)} ${title}`,
    start: range.start,
    end: range.end,
    allDay: true,
    editable,
    durationEditable: editable,
    startEditable: editable,
    classNames: [
      "activity-calendar-event",
      `activity-calendar-event-${plan.status.toLowerCase()}`,
      plan.deploymentMatches ? "deployment-matched" : ""
    ].filter(Boolean),
    extendedProps: { status: plan.status }
  }];
}));

const calendarOptions = computed<CalendarOptions>(() => ({
  plugins: [dayGridPlugin, interactionPlugin],
  initialView: "dayGridMonth",
  locale: zhCnLocale,
  firstDay: 1,
  fixedWeekCount: true,
  showNonCurrentDates: true,
  selectable: true,
  selectMirror: true,
  editable: true,
  eventResizableFromStart: true,
  eventDurationEditable: true,
  dayMaxEvents: 3,
  height: "auto",
  headerToolbar: false,
  displayEventTime: false,
  eventTimeFormat: {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  },
  events: calendarEvents.value,
  dateClick: startCreateFromDate,
  select: startCreateFromSelection,
  eventClick: selectCalendarEvent,
  eventDrop: handleEventDrop,
  eventResize: handleEventResize,
  datesSet: info => { calendarTitle.value = info.view.title; },
  eventDidMount: info => {
    const plan = plans.value.find(candidate => candidate.id === info.event.id);
    if (plan) {
      info.el.title = `${plan.title}，${formatDateTime(plan.opensAt)} 至 ${formatDateTime(plan.closesAt)}`;
    }
  }
}));

const actionDialog = computed(() => {
  const plan = selectedPlan.value;
  const action = pendingAction.value;
  if (!plan || !action) {
    return {
      title: "确认企划操作",
      message: "",
      confirmLabel: "确认",
      danger: false,
      requireReason: false,
      confirmationText: ""
    };
  }
  if (action === "publish") {
    return {
      title: "发布活动企划",
      message: `发布后《${plan.title}》会进入官网和启动器活动日历。系统会再次确认它不与任何已发布活动重叠。`,
      confirmLabel: "确认发布",
      danger: false,
      requireReason: false,
      confirmationText: ""
    };
  }
  if (action === "withdraw") {
    return {
      title: "撤回活动企划",
      message: `撤回《${plan.title}》后，它会立即退出玩家可见日历并回到草稿。`,
      confirmLabel: "确认撤回",
      danger: true,
      requireReason: false,
      confirmationText: ""
    };
  }
  if (action === "archive") {
    return {
      title: "归档活动企划",
      message: `归档《${plan.title}》会释放它占用的发布排期，但不会删除整合包、部署记录或审计。`,
      confirmLabel: "确认归档",
      danger: true,
      requireReason: true,
      confirmationText: ""
    };
  }
  if (action === "restore") {
    return {
      title: "恢复活动企划",
      message: `恢复《${plan.title}》后，它会以草稿状态返回，不会自动发布或部署。`,
      confirmLabel: "恢复为草稿",
      danger: false,
      requireReason: false,
      confirmationText: ""
    };
  }
  return {
    title: "部署企划整合包",
    message: `把《${plan.title}》绑定的 ${plan.profileDisplayName} ${plan.version} 部署到 owl5 活动槽。完成后仍保持停服。`,
    confirmLabel: "确认部署",
    danger: true,
    requireReason: true,
    confirmationText: `DEPLOY ${plan.id}`
  };
});

function serializeDraft(): string {
  return JSON.stringify({
    title: draft.title,
    announcement: draft.announcement,
    opensAt: draft.opensAt,
    closesAt: draft.closesAt,
    maximumPlayers: draft.maximumPlayers,
    minimumTier: draft.minimumTier,
    packageImportId: draft.packageImportId
  });
}

function resetDraft(values: PlanDraft): void {
  Object.assign(draft, values);
  editorBaseline.value = serializeDraft();
  editorError.value = "";
}

function syncPlanToEditor(plan: ActivityPlan): void {
  editorMode.value = "edit";
  selectedPlanId.value = plan.id;
  resetDraft({
    title: plan.title,
    announcement: plan.announcement,
    opensAt: toLocalDateTimeInput(plan.opensAt),
    closesAt: toLocalDateTimeInput(plan.closesAt),
    maximumPlayers: plan.maximumPlayers,
    minimumTier: plan.minimumTier,
    packageImportId: plan.packageImportId
  });
}

function localDateTime(date: Date): string {
  return toLocalDateTimeInput(date.toISOString());
}

function preferredPackage(): ActivityPackage | null {
  return packages.value.find(item => !item.profileArchived && item.productionReady) ??
    packages.value.find(item => !item.profileArchived) ??
    packages.value[0] ??
    null;
}

function beginCreate(opensAt: Date, closesAt: Date): void {
  if (editorDirty.value && !window.confirm("当前编辑内容尚未保存，确定放弃并创建新企划吗？")) {
    return;
  }
  const packageItem = preferredPackage();
  editorMode.value = "create";
  selectedPlanId.value = "";
  resetDraft({
    title: packageItem?.profileDisplayName || "新活动企划",
    announcement: "",
    opensAt: localDateTime(opensAt),
    closesAt: localDateTime(closesAt),
    maximumPlayers: packageItem?.maximumPlayers ?? 30,
    minimumTier: "Participant",
    packageImportId: packageItem?.importId ?? ""
  });
}

function startCreateFromDate(info: DateClickArg): void {
  const opensAt = new Date(info.date);
  opensAt.setHours(19, 0, 0, 0);
  const closesAt = new Date(opensAt);
  closesAt.setHours(22, 0, 0, 0);
  beginCreate(opensAt, closesAt);
}

function startCreateToday(): void {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  startCreateFromDate({ date: today } as DateClickArg);
}

function moveCalendar(direction: "prev" | "next" | "today"): void {
  calendar.value?.getApi()[direction]();
}

function startCreateFromSelection(info: DateSelectArg): void {
  const opensAt = new Date(info.start);
  opensAt.setHours(19, 0, 0, 0);
  const closesAt = new Date(info.end);
  closesAt.setDate(closesAt.getDate() - 1);
  closesAt.setHours(22, 0, 0, 0);
  if (closesAt <= opensAt) closesAt.setTime(opensAt.getTime() + 3 * 60 * 60 * 1000);
  beginCreate(opensAt, closesAt);
  info.view.calendar.unselect();
}

function selectCalendarEvent(info: EventClickArg): void {
  const plan = plans.value.find(candidate => candidate.id === info.event.id);
  if (!plan) return;
  if (editorDirty.value && selectedPlanId.value !== plan.id &&
      !window.confirm("当前编辑内容尚未保存，确定放弃并打开其他企划吗？")) {
    return;
  }
  syncPlanToEditor(plan);
}

function selectPlan(plan: ActivityPlan): void {
  if (editorDirty.value && selectedPlanId.value !== plan.id &&
      !window.confirm("当前编辑内容尚未保存，确定放弃并打开其他企划吗？")) {
    return;
  }
  syncPlanToEditor(plan);
}

function closeEditor(): void {
  if (editorDirty.value && !window.confirm("当前编辑内容尚未保存，确定放弃吗？")) return;
  editorMode.value = "idle";
  selectedPlanId.value = "";
  editorError.value = "";
}

function planStatusText(status: ActivityPlanStatus): string {
  return { Draft: "草稿", Published: "已发布", Archived: "已归档" }[status];
}

function slotStateText(): string {
  if (!slot.value?.configured) return "未配置";
  if (!slot.value.agentConnected) return "代理离线";
  if (slot.value.activeOperation) return "操作执行中";
  return slot.value.online ? "运行中" : slot.value.serverFilesPresent ? "已停止" : "待部署";
}

function packageOptionText(item: ActivityPackage): string {
  const state = item.profileArchived ? "档案已归档" : item.productionReady ? "可发布" : "仅测试通道";
  return `${item.profileDisplayName} ${item.version} · ${state}`;
}

function updatePackageDefaults(): void {
  const packageItem = selectedPackage.value;
  if (editorMode.value === "create" && packageItem) {
    draft.maximumPlayers = packageItem.maximumPlayers;
  }
  editorError.value = "";
}

function requestBody(expectedRevision?: number): Record<string, unknown> {
  return {
    title: draft.title.trim(),
    announcement: draft.announcement.trim(),
    opensAt: draftStart.value,
    closesAt: draftEnd.value,
    maximumPlayers: Number(draft.maximumPlayers),
    minimumTier: draft.minimumTier,
    packageImportId: draft.packageImportId,
    ...(expectedRevision ? { expectedRevision } : {})
  };
}

function replacePlan(nextPlan: ActivityPlan): void {
  const current = overview.data.value;
  if (!current) return;
  const exists = current.plans.some(plan => plan.id === nextPlan.id);
  overview.data.value = {
    ...current,
    plans: exists
      ? current.plans.map(plan => plan.id === nextPlan.id ? nextPlan : plan)
      : [...current.plans, nextPlan]
  };
}

async function savePlan(): Promise<void> {
  if (saveDisabled.value) return;
  const wasCreate = editorMode.value === "create";
  editorBusy.value = true;
  editorError.value = "";
  try {
    const plan = editorMode.value === "create"
      ? await api<ActivityPlan>("/v1/admin/activity-plans", {
          method: "POST",
          body: requestBody()
        })
      : await api<ActivityPlan>(
          `/v1/admin/activity-plans/${encodeURIComponent(selectedPlanId.value)}`,
          {
            method: "PUT",
            body: requestBody(selectedPlan.value!.revision)
          }
        );
    replacePlan(plan);
    syncPlanToEditor(plan);
    showToast(wasCreate ? "企划草稿已创建" : "企划更改已保存");
  } catch (reason) {
    editorError.value = reason instanceof Error ? reason.message : "企划保存失败。";
    if (editorError.value.includes("其他管理员修改")) await refreshPlans();
  } finally {
    editorBusy.value = false;
  }
}

async function reschedulePlan(
  plan: ActivityPlan,
  start: Date | null,
  end: Date | null,
  revert: () => void
): Promise<void> {
  if (!start || !end || (selectedPlanId.value === plan.id && editorDirty.value)) {
    revert();
    showToast("请先保存或放弃右侧未提交更改，再调整日历排期。", true);
    return;
  }
  scheduleBusyPlanId.value = plan.id;
  try {
    const updated = await api<ActivityPlan>(
      `/v1/admin/activity-plans/${encodeURIComponent(plan.id)}`,
      {
        method: "PUT",
        body: {
          title: plan.title,
          announcement: plan.announcement,
          opensAt: start.toISOString(),
          closesAt: end.toISOString(),
          maximumPlayers: plan.maximumPlayers,
          minimumTier: plan.minimumTier,
          packageImportId: plan.packageImportId,
          expectedRevision: plan.revision
        }
      }
    );
    replacePlan(updated);
    if (selectedPlanId.value === updated.id) syncPlanToEditor(updated);
    showToast("企划排期已更新");
  } catch (reason) {
    revert();
    showToast(reason instanceof Error ? reason.message : "排期调整失败，已恢复原位置。", true);
    await refreshPlans();
  } finally {
    scheduleBusyPlanId.value = "";
  }
}

function handleEventDrop(info: EventDropArg): void {
  const plan = plans.value.find(candidate => candidate.id === info.event.id);
  const dates = plan
    ? moveActivityPlanDates(plan.opensAt, plan.closesAt, info.delta)
    : null;
  if (!plan || !dates) {
    info.revert();
    return;
  }
  void reschedulePlan(plan, dates.opensAt, dates.closesAt, info.revert);
}

function handleEventResize(info: EventResizeDoneArg): void {
  const plan = plans.value.find(candidate => candidate.id === info.event.id);
  const dates = plan
    ? resizeActivityPlanDates(
        plan.opensAt,
        plan.closesAt,
        info.startDelta,
        info.endDelta
      )
    : null;
  if (!plan || !dates) {
    info.revert();
    return;
  }
  void reschedulePlan(plan, dates.opensAt, dates.closesAt, info.revert);
}

function openAction(action: PlanAction): void {
  if (!selectedPlan.value || editorDirty.value) {
    showToast("请先保存或放弃当前更改。", true);
    return;
  }
  if (action === "publish" && !canPublish.value) {
    showToast(overlappingPublishedPlan.value
      ? `排期与《${overlappingPublishedPlan.value.title}》重叠，不能发布。`
      : "整合包尚未进入 Production 通道，不能发布。", true);
    return;
  }
  if (action === "deploy" && !canDeploy.value) {
    showToast(slot.value?.online
      ? "活动服仍在运行，请先到服控面板正常停止。"
      : "活动槽当前不满足部署条件。", true);
    return;
  }
  pendingAction.value = action;
  actionError.value = "";
}

async function submitAction(payload: { reason: string; confirmation: string }): Promise<void> {
  const action = pendingAction.value;
  const plan = selectedPlan.value;
  if (!action || !plan) return;
  actionBusy.value = true;
  actionError.value = "";
  try {
    if (action === "deploy") {
      await api<ControlQueueResult>(
        `/v1/admin/activity-plans/${encodeURIComponent(plan.id)}/deploy`,
        {
          method: "POST",
          body: {
            expectedRevision: plan.revision,
            confirmation: payload.confirmation,
            reason: payload.reason
          }
        }
      );
      showToast("活动槽部署已排队，完成后仍保持停服");
    } else {
      const body = action === "archive"
        ? { expectedRevision: plan.revision, reason: payload.reason }
        : { expectedRevision: plan.revision };
      const updated = await api<ActivityPlan>(
        `/v1/admin/activity-plans/${encodeURIComponent(plan.id)}/${action}`,
        { method: "POST", body }
      );
      replacePlan(updated);
      syncPlanToEditor(updated);
      showToast({
        publish: "企划已发布到玩家日历",
        withdraw: "企划已撤回为草稿",
        archive: "企划已归档",
        restore: "企划已恢复为草稿"
      }[action]);
    }
    pendingAction.value = null;
    await refreshPlans();
  } catch (reason) {
    actionError.value = reason instanceof Error ? reason.message : "企划操作失败。";
  } finally {
    actionBusy.value = false;
  }
}

async function refreshPlans(): Promise<void> {
  const result = await overview.refresh();
  if (!result) return;
  if (selectedPlanId.value && !result.plans.some(plan => plan.id === selectedPlanId.value)) {
    editorMode.value = "idle";
    selectedPlanId.value = "";
    return;
  }
  const latest = result.plans.find(plan => plan.id === selectedPlanId.value);
  if (latest && editorMode.value === "edit" && !editorDirty.value) syncPlanToEditor(latest);
}

const unregister = registerPageRefresh(refreshPlans);
onScopeDispose(unregister);
usePolling(refreshPlans, 5_000);

watch(selectedPlan, plan => {
  if (plan && editorMode.value === "edit" && !editorDirty.value) syncPlanToEditor(plan);
});
</script>

<template>
  <section class="view-section activity-plans-view">
    <PageHeading
      title="活动企划"
      description="统一安排玩家可见日期、客户端整合包和 owl5 活动槽部署。"
      :updated-at="overview.lastUpdatedAt.value"
      :stale="Boolean(overview.error.value)"
    >
      <template #actions>
        <button class="button button-primary" type="button" @click="startCreateToday">
          <AppIcon name="plus" />新建企划
        </button>
      </template>
    </PageHeading>

    <div v-if="overview.error.value && overview.data.value" class="inline-alert compact-alert" role="status">
      <AppIcon name="circle-alert" />
      <span>自动刷新失败，当前显示上次成功数据：{{ overview.error.value }}</span>
    </div>

    <ResourceState
      :loading="overview.loading.value && !overview.data.value"
      :error="overview.data.value ? '' : overview.error.value"
      @retry="refreshPlans"
    >
      <div class="summary-strip activity-plan-summary" aria-label="活动企划摘要">
        <div><span>已发布</span><strong>{{ publishedCount }}</strong></div>
        <div><span>草稿</span><strong>{{ draftCount }}</strong></div>
        <div><span>待开放</span><strong>{{ upcomingCount }}</strong></div>
        <div><span>活动槽</span><strong class="activity-slot-summary">{{ slotStateText() }}</strong></div>
      </div>

      <div class="activity-single-slot-rule" role="note">
        <AppIcon name="shield-check" />
        <div>
          <strong>同一时间只开放一个活动</strong>
          <span>已发布企划使用半开区间 [开始, 结束)，前一场结束时可由下一场无缝接档；草稿可以重叠，但冲突排期不能发布。</span>
        </div>
      </div>

      <div class="activity-plan-toolbar">
        <div class="segmented-control" aria-label="企划筛选">
          <button type="button" :class="{ active: planFilter === 'active' }" @click="planFilter = 'active'">当前企划</button>
          <button type="button" :class="{ active: planFilter === 'all' }" @click="planFilter = 'all'">全部</button>
          <button type="button" :class="{ active: planFilter === 'archived' }" @click="planFilter = 'archived'">已归档</button>
        </div>
        <div class="activity-calendar-legend" aria-label="日历状态图例">
          <span class="published">已发布</span>
          <span class="draft">草稿</span>
          <span class="archived">已归档</span>
        </div>
      </div>

      <div class="activity-plan-layout">
        <section class="activity-calendar-panel" aria-label="活动企划月历">
          <header class="activity-calendar-toolbar">
            <div>
              <button class="icon-button" type="button" title="上个月" aria-label="上个月" @click="moveCalendar('prev')"><AppIcon name="chevron-left" /></button>
              <button class="icon-button" type="button" title="下个月" aria-label="下个月" @click="moveCalendar('next')"><AppIcon name="chevron-right" /></button>
            </div>
            <h2>{{ calendarTitle }}</h2>
            <button class="button button-secondary" type="button" @click="moveCalendar('today')"><AppIcon name="clock" />今天</button>
          </header>
          <div class="activity-calendar-body"><FullCalendar ref="calendar" :options="calendarOptions" /></div>
        </section>

        <aside class="activity-plan-inspector" aria-label="企划检查器">
          <div v-if="editorMode === 'idle'" class="activity-inspector-empty">
            <AppIcon name="activity" :size="24" />
            <strong>选择一个企划</strong>
            <span>也可以在月历中选择日期范围，建立新的企划草稿。</span>
            <div v-if="slot" class="activity-slot-identity">
              <span>当前活动槽</span>
              <strong>{{ slotStateText() }}</strong>
              <small v-if="slot.deployedPackage">
                {{ slot.deployedPackage.profileId }} · {{ slot.deployedPackage.version }}
              </small>
              <small v-else>尚未部署企划整合包</small>
            </div>
          </div>

          <template v-else>
            <header class="activity-inspector-header">
              <div>
                <span>{{ editorMode === "create" ? "新企划草稿" : selectedPlan?.id }}</span>
                <h2>{{ editorMode === "create" ? "创建活动企划" : selectedPlan?.title }}</h2>
              </div>
              <button class="icon-button" type="button" title="关闭检查器" aria-label="关闭检查器" @click="closeEditor">
                <AppIcon name="x" />
              </button>
            </header>

            <div v-if="selectedPlan" class="activity-plan-statebar">
              <span class="status-badge" :class="selectedPlan.status === 'Published' ? 'status-online' : selectedPlan.status === 'Draft' ? 'status-maintenance' : 'status-archived'">
                {{ planStatusText(selectedPlan.status) }}
              </span>
              <span :class="{ ready: selectedPlan.productionReady }">
                {{ selectedPlan.productionReady ? "Production 已就绪" : "仅 Test / Gray" }}
              </span>
              <span :class="{ ready: selectedPlan.deploymentMatches }">
                {{ selectedPlan.deploymentMatches ? "活动槽已匹配" : "活动槽未匹配" }}
              </span>
            </div>

            <form class="activity-plan-form" @submit.prevent="savePlan">
              <label>
                企划名称
                <input v-model="draft.title" type="text" minlength="2" maxlength="80" required @input="editorError = ''">
              </label>
              <label>
                玩家公告
                <textarea v-model="draft.announcement" maxlength="280" rows="3" placeholder="显示在官网、启动器和活动目录中的简短说明" @input="editorError = ''"></textarea>
              </label>
              <div class="activity-time-grid">
                <label>
                  开放时间
                  <input v-model="draft.opensAt" type="datetime-local" required @input="editorError = ''">
                </label>
                <label>
                  结束时间
                  <input v-model="draft.closesAt" type="datetime-local" required @input="editorError = ''">
                </label>
              </div>
              <label>
                绑定整合包
                <select v-model="draft.packageImportId" required @change="updatePackageDefaults">
                  <option value="" disabled>请选择已完成的整合包</option>
                  <option v-for="item in packages" :key="item.importId" :value="item.importId" :disabled="item.profileArchived">
                    {{ packageOptionText(item) }}
                  </option>
                </select>
              </label>
              <div v-if="selectedPackage" class="activity-package-facts">
                <span>{{ selectedPackage.minecraftVersion }} · {{ selectedPackage.loader }} {{ selectedPackage.loaderVersion }}</span>
                <strong>{{ selectedPackage.profileId }}</strong>
              </div>
              <div class="activity-time-grid">
                <label>
                  人数上限
                  <input v-model.number="draft.maximumPlayers" type="number" min="1" max="1000" required @input="editorError = ''">
                </label>
                <label>
                  最低称号
                  <select v-model="draft.minimumTier" required @change="editorError = ''">
                    <option value="Member">{{ tierText("Member") }}</option>
                    <option value="Participant">{{ tierText("Participant") }}</option>
                    <option value="Collaborator">{{ tierText("Collaborator") }}</option>
                  </select>
                </label>
              </div>

              <div v-if="overlappingPublishedPlan" class="inline-alert activity-conflict-alert" role="alert">
                <AppIcon name="circle-alert" />
                <span>
                  与已发布企划《{{ overlappingPublishedPlan.title }}》重叠。
                  {{ selectedPlan?.status === "Published" ? "当前调整无法保存。" : "草稿可以保存，但不能发布。" }}
                </span>
              </div>
              <div v-if="editorError" class="inline-alert activity-editor-error" role="alert">
                <AppIcon name="circle-alert" /><span>{{ editorError }}</span>
              </div>

              <div class="activity-editor-save">
                <span v-if="editorDirty">有未保存更改</span>
                <span v-else-if="editorMode === 'edit'">修订 r{{ selectedPlan?.revision }}</span>
                <span v-else>保存后进入草稿状态</span>
                <button class="button button-secondary" type="submit" :disabled="saveDisabled">
                  <AppIcon name="save" />{{ editorBusy ? "保存中" : editorMode === "create" ? "创建草稿" : "保存更改" }}
                </button>
              </div>
            </form>

            <section v-if="selectedPlan" class="activity-plan-actions" aria-label="企划操作">
              <div class="activity-action-heading">
                <h3>发布与部署</h3>
                <span>部署不会自动启动 Minecraft 服务端。</span>
              </div>
              <div class="activity-action-buttons">
                <button v-if="selectedPlan.status === 'Draft'" class="button button-primary" type="button" :disabled="!canPublish" @click="openAction('publish')">
                  <AppIcon name="check" />发布企划
                </button>
                <button v-if="selectedPlan.status === 'Published'" class="button button-secondary" type="button" :disabled="editorDirty" @click="openAction('withdraw')">
                  <AppIcon name="rotate-ccw" />撤回为草稿
                </button>
                <button v-if="selectedPlan.status !== 'Archived'" class="button button-secondary" type="button" :disabled="!canDeploy" @click="openAction('deploy')">
                  <AppIcon name="package" />{{ selectedPlan.deploymentMatches ? "已部署" : "部署到活动槽" }}
                </button>
                <button v-if="selectedPlan.status !== 'Archived'" class="button button-danger" type="button" :disabled="editorDirty" @click="openAction('archive')">
                  <AppIcon name="archive" />归档
                </button>
                <button v-else class="button button-secondary" type="button" @click="openAction('restore')">
                  <AppIcon name="rotate-ccw" />恢复为草稿
                </button>
              </div>
              <p v-if="selectedPlan.status === 'Draft' && !selectedPlan.productionReady" class="activity-action-note">
                发布前需要先在“客户端档案”中把该整合包版本推进到 Production 通道。
              </p>
              <p v-if="slot?.online" class="activity-action-note warning">
                活动槽正在运行。部署前必须先到服控面板正常停止服务器。
              </p>
              <p v-if="slot?.activeOperation" class="activity-action-note warning">
                当前有 {{ slot.activeOperation.action }} 操作正在执行，请等待完成。
              </p>
            </section>
          </template>
        </aside>
      </div>

      <section v-if="visiblePlans.length" class="activity-plan-index" aria-labelledby="activity-plan-index-title">
        <div class="activity-index-heading">
          <div><h2 id="activity-plan-index-title">企划索引</h2><span>{{ visiblePlans.length }} 条记录</span></div>
        </div>
        <button v-for="plan in visiblePlans" :key="plan.id" class="activity-index-row" :class="{ active: plan.id === selectedPlanId }" type="button" @click="selectPlan(plan)">
          <span class="status-badge" :class="plan.status === 'Published' ? 'status-online' : plan.status === 'Draft' ? 'status-maintenance' : 'status-archived'">{{ planStatusText(plan.status) }}</span>
          <strong>{{ plan.title }}</strong>
          <span>{{ formatDateTime(plan.opensAt) }} 至 {{ formatDateTime(plan.closesAt) }}</span>
          <small>{{ plan.profileDisplayName }} · {{ plan.version }}</small>
        </button>
      </section>
    </ResourceState>

    <ConfirmDialog
      :open="Boolean(pendingAction)"
      :title="actionDialog.title"
      :message="actionDialog.message"
      :confirm-label="actionDialog.confirmLabel"
      :danger="actionDialog.danger"
      :busy="actionBusy"
      :require-reason="actionDialog.requireReason"
      :confirmation-text="actionDialog.confirmationText"
      :error="actionError"
      @close="pendingAction = null; actionError = ''"
      @confirm="submitAction"
    />
  </section>
</template>
