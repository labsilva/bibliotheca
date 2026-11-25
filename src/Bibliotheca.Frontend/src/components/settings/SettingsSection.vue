<template>
  <section class="settings-section">
    <header class="settings-section__header">
      <div class="settings-section__title-wrap">
        <slot name="icon">
          <span v-if="icon" class="settings-section__icon">
            <component :is="icon" />
          </span>
        </slot>
        <h2 class="settings-section__title">{{ title }}</h2>
      </div>

      <p v-if="subtitle" class="settings-section__subtitle">
        {{ subtitle }}
      </p>
    </header>

    <div class="settings-section__body">
      <slot />
    </div>
  </section>
</template>

<script setup>
defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: '' },
  // Ex.: 'IconSettings' ou componente importado
  icon: { type: [String, Object, Function], default: null }
})
</script>

<style scoped>
.settings-section {
  box-sizing: border-box;
  min-width: 0;
  background: radial-gradient(circle at top left, #102040 0, #060914 55%, #03040a 100%);
  border: 1px solid rgba(0, 255, 255, 0.12);
  border-radius: 16px;
  padding: 20px 24px;
  margin-bottom: 24px;
  box-shadow: 0 0 24px rgba(0, 200, 255, 0.12);
}

.settings-section__header {
  margin-bottom: 16px;
}

.settings-section__title-wrap {
  display: flex;
  align-items: center;
  gap: 10px;
}

.settings-section__icon {
  width: 28px;
  height: 28px;
  border-radius: 999px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 229, 255, 0.1);
  color: #00e5ff;
  box-shadow: 0 0 8px rgba(0, 229, 255, 0.6);
}

.settings-section__title {
  font-size: 18px;
  font-weight: 600;
  color: #e5f3ff;
}

.settings-section__subtitle {
  margin-top: 4px;
  font-size: 13px;
  color: #93a4c4;
}

.settings-section__body {
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
@media (max-width: 640px) {
  .settings-section { padding: 14px 12px; }
  .settings-section__body { min-width: 0; }
}
</style>
