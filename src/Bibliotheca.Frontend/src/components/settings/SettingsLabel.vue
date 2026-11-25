<template>
  <label class="settings-label">
    <div class="settings-lable__text">
      <span class="settings-label__label">{{ label }}</span>
      <span v-if="description" class="settings-label__description">
        {{ description }}
      </span>
    </div>
  </label>
</template>

<script setup>
const props = defineProps({
  label: { type: String, required: true },
  description: { type: String, default: '' }
})
</script>

<style scoped>
.settings-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
}

.settings-label__text {
  display: flex;
  flex-direction: column;
}

.settings-label__label {
  font-size: 15px;
  font-weight: 500;
  color: #e5f3ff;
}

.settings-label__description {
  margin-top: 2px;
  font-size: 13px;
  color: #8fa0bf;
}
</style>