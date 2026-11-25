<template>
  <article class="card">
    <div class="cover">
      <img :src="cover" :alt="title" loading="lazy" />
    </div>
    <div class="info">
      <h4 class="title" :title="title">{{ title }}</h4>
      <span class="subtitle" :title="subtitle">{{ subtitle }}</span>
      <span class="author" :title="author">{{ author }}</span>
      <div class="meta">
        <span class="badge" v-if="status==='reading'">▶</span>
        <span class="badge" v-else-if="status==='new'">NEW</span>
        <button class="dots" @click="$emit('menu')">⋯</button>
      </div>
    </div>
    <div class="progress" v-if="typeof progress==='number'">
      <i class="bar" :style="{width: progress+'%'}"></i>
    </div>
  </article>
</template>

<script setup>
const props = defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: '' },
  author: { type: String, default: '' },
  cover: { type: String, required: true },
  progress: { type: Number, default: null },
  status: { type: String, default: '' } // 'reading' | 'new'
})
</script>

<style scoped>
.card{width:164px; background:var(--panel); border:1px solid var(--line); border-radius:14px; overflow:hidden; display:flex; flex-direction:column}
.cover{aspect-ratio: 3/4; background:#0c0e12}
.cover img{width:100%; height:100%; object-fit:cover; display:block}
.info{padding:10px; display:grid; gap:4px}
.title{font-size:14px; line-height:1.1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis}
.subtitle{font-size:10px; line-height:1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis}
.author{font-size:12px; color:var(--sub); white-space:nowrap; overflow:hidden; text-overflow:ellipsis}
.meta{display:flex; align-items:center}
.badge{font-size:11px; background:#1b2330; border:1px solid var(--line); padding:2px 6px; border-radius:6px; margin-right:auto}
.dots{margin-left:auto; border:0; background:transparent; color:var(--sub); font-size:18px; line-height:1}
.progress{height:4px; background:#0d1117}
.bar{display:block; height:100%; background:var(--accent)}
</style>