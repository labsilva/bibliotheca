<template>
  <main class="not-found" aria-labelledby="not-found-title">
    <h1 id="not-found-title">404 — Page not found</h1>
    <RouterLink :to="{ name: 'dashboard' }">Go to dashboard</RouterLink>
  </main>
</template>

<style scoped>
.not-found {
  max-width: 36rem;
  margin: clamp(4rem, 20vh, 12rem) auto 0;
  padding-inline: 1.5rem;
}

.not-found a {
  color: var(--brand-glow);
  text-decoration: underline;
  text-underline-offset: 0.2em;
}
</style>
