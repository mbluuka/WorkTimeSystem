export function register(){
  if ('serviceWorker' in navigator){
    navigator.serviceWorker.register('/src/service-worker.js')
      .then(reg => console.log('Service worker registered', reg))
      .catch(err => console.warn('Service worker reg failed', err));

    navigator.serviceWorker.addEventListener('message', (evt) => {
      if (evt.data && evt.data.type === 'SYNC_OUTBOX'){
        // Optionally trigger client sync
        console.log('Service worker requested outbox sync');
      }
    });
  }
}
