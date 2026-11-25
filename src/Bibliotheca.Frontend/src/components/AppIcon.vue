<template>
  <span
    class="app-icon"
    :class="`app-icon--${icon}`"
    :style="sizeStyle"
    :aria-label="label || icon"
    role="img"
  ></span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  icon: {
    type: String,
    required: true
  },
  size: {
    type: [Number, String],
    default: 1.2   // em rem
  },
  label: {
    type: String,
    default: ''
  }
})

const sizeStyle = computed(() => {
  const v = typeof props.size === 'number' ? `${props.size}rem` : props.size
  return {
    width: v,
    height: v
  }
})
</script>
