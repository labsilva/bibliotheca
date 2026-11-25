<template>
  <button class="icon" type="button" title="Add" aria-label="Add book" @click="onClick"><font-awesome-icon icon="fa-solid fa-plus" size="2xs" /></button>
</template>

<script setup>
const emit = defineEmits(['click'])

function onClick() {
  emit('click')
}
</script>

<style scoped>
.icon {
  display: inline-grid;
  place-items: center;
  width: 44px;
  height: 44px;
  padding: 0;
  border: 1px solid #34739d;
  border-radius: 10px;
  background: #142c42;
  color: #a6ecff;
  font-size: 1.25rem;
  box-shadow: 0 0 12px rgb(47 179 255 / 22%);
  cursor: pointer;
}

.icon:hover,
.icon:focus-visible {
  background: #1d405c;
}
</style>
