self.addEventListener('activate', event => {
  event.waitUntil(Promise.all([
    caches.delete('api'),
    caches.delete('images'),
    caches.delete('bibliotheca-public-covers-v1'),
    caches.delete('bibliotheca-public-covers-v2'),
  ]))
})
