// Service Worker for Voxta Prompt Editor PWA
// Minimal version - no caching to avoid stale data issues
const CACHE_NAME = 'prompt-editor-v2';

// Install: skip waiting immediately
self.addEventListener('install', (event) => {
  self.skipWaiting();
});

// Activate: clear all old caches and claim clients
self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames.map((name) => caches.delete(name))
      );
    }).then(() => {
      return self.clients.claim();
    })
  );
});

// Fetch: always go to network, no caching
self.addEventListener('fetch', (event) => {
  event.respondWith(fetch(event.request));
});

// Handle messages
self.addEventListener('message', (event) => {
  if (event.data === 'skipWaiting') {
    self.skipWaiting();
  }
});
