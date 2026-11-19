<script setup lang="ts">
// Renders a ```mermaid fence in the browser (mermaid needs the DOM), again when the color scheme changes.
import { onMounted, ref, watch } from "vue";
import { useData } from "vitepress";

const props = defineProps<{ code: string }>();
const { isDark } = useData();
const svg = ref("");
const source = decodeURIComponent(props.code);
let renders = 0;

async function render(): Promise<void> {
  const { default: mermaid } = await import("mermaid");
  mermaid.initialize({ startOnLoad: false, theme: isDark.value ? "dark" : "default", securityLevel: "strict" });
  const id = `mermaid-${Math.random().toString(36).slice(2)}-${renders++}`;
  try {
    svg.value = (await mermaid.render(id, source)).svg;
  } catch (error) {
    console.error("Mermaid diagram failed to render", error);
    svg.value = "";
  }
}

onMounted(render);
watch(isDark, render);
</script>

<template>
  <div v-if="svg" class="mermaid-diagram" v-html="svg" />
  <pre v-else class="mermaid-source"><code>{{ source }}</code></pre>
</template>

<style scoped>
.mermaid-diagram {
  margin: 16px 0;
  overflow-x: auto;
  text-align: center;
}

/* Mermaid measures its labels without the page's paragraph styles; the doc styles would make them taller and clip. */
.mermaid-diagram :deep(foreignObject p),
.mermaid-diagram :deep(foreignObject div) {
  margin: 0;
  line-height: 1.5;
}
</style>
