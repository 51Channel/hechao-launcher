<script setup lang="ts">
import { computed } from "vue";
import { iconUrl } from "@/utils";

const props = withDefaults(defineProps<{ name: string; label?: string; size?: number }>(), { label: "", size: 18 });
const source = computed(() => iconUrl(props.name));
</script>

<template>
  <img class="app-icon" :src="source" :alt="label" :width="size" :height="size">
</template>
