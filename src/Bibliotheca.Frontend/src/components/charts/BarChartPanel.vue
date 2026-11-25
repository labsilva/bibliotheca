<template>
  <div class="chart-panel">
    <div class="chart-header" v-if="config.title || config.subtitle">
      <div class="chart-title" v-if="config.title">
        {{ config.title }}
      </div>
      <div class="chart-subtitle" v-if="config.subtitle">
        {{ config.subtitle }}
      </div>
    </div>

    <VChart :option="option" autoresize class="chart" />
  </div>
</template>

<script setup>
import { computed } from "vue"
import VChart from "vue-echarts"

import { use } from "echarts/core"
import { BarChart } from "echarts/charts"
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
} from "echarts/components"
import { CanvasRenderer } from "echarts/renderers"

use([BarChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer])

const props = defineProps({
  config: {
    type: Object,
    required: true,
  },
})

function parseFormatter(fnString) {
  if (!fnString || typeof fnString !== "string") return (v) => v

  try {
    return new Function(`return ${fnString}`)()
  } catch (err) {
    console.warn("Invalid formatter function:", fnString, err)
    return (v) => v
  }
}

const option = computed(() => {
  const cfg = props.config || {}
  const seriesCfg = cfg.data?.series || {}
  const points = seriesCfg.points || []

  const categories = points.map((p) => p.x)
  const values = points.map((p) => Number(p.y))

  const xFormatter = parseFormatter(cfg.xLabelFormatter)

  return {
    backgroundColor: "transparent",
    tooltip: { trigger: "axis" },
    grid: {
      left: "6%",
      right: "4%",
      bottom: "10%",
      top: 20,
      containLabel: true,
    },
    xAxis: {
      type: "category",
      data: categories,
      axisLabel: {
        color: "#ccc",
        rotate: 30,
        formatter: (value) => xFormatter(value),
      },
      axisLine: { lineStyle: { color: "var(--brand)" } },
    },
    yAxis: {
      type: "value",
      axisLabel: { color: "#ccc" },
      splitLine: { lineStyle: { color: "rgba(255, 255, 255, 0.08)" } },
    },
    series: [
      {
        name: seriesCfg.name || "",
        type: "bar",
        data: values,
        barMaxWidth: 40,
        itemStyle: { color: seriesCfg.color || "var(--brand)" },
      },
    ],
  }
})
</script>

<style scoped>
.chart-panel {
  border: 1px solid var(--brand);
  border-radius: 12px;
  background: transparent;
  padding: 0.75rem 0.75rem 0.75rem;
  box-shadow: 0 0 12px rgba(0, 200, 255, 0.25);
  height: 100%;
  display: flex;
  flex-direction: column;
}

.chart-header {
  margin-bottom: 0.5rem;
  text-align: center;
}

.chart-title {
  font-size: 0.9rem;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--brand);
}

.chart-subtitle {
  font-size: 0.75rem;
  color: #aaa;
  margin-top: 0.1rem;
}

.chart {
  width: 100%;
  height: 260px;
}
</style>
