<template>
  <transition name="toast-fade">
    <div v-if="toast.visible" class="toast" :class="toast.type">
      <div class="toast-text">
        <template v-for="(line, i) in lines" :key="i">
          <span>{{ line }}</span>
          <br v-if="i < lines.length - 1" />
        </template>
      </div>
      <button class="toast-close" type="button" @click="close">✕</button>
    </div>
  </transition>
</template>

<script context="module">
import { reactive } from 'vue';

// module-scoped reactive state (shared; can be imported together with component)
export const toast = reactive({
  visible: false,
  message: '',
  type: 'info',
  duration: 5000,
  timerId: null,
});

// exported function you can import elsewhere:
// import Toast, { notify } from './components/Toast.vue'
export function notify(message, type = 'info', duration = 5000) {
  // set values
  toast.message = message ?? '';
  toast.type = type ?? 'info';
  toast.duration = duration ?? 5000;
  toast.visible = true;

  // start/clear timer
  if (toast.timerId) {
    clearTimeout(toast.timerId);
    toast.timerId = null;
  }
  if (toast.duration > 0) {
    toast.timerId = setTimeout(() => {
      toast.visible = false;
      toast.timerId = null;
    }, toast.duration);
  }
}
</script>

<script setup>
import { computed, onBeforeUnmount } from 'vue';

// `toast` is defined in the module script above and accessible here
// (module script runs in same module scope; SFC tooling merges them)
const lines = computed(() => (toast.message ? toast.message.split('\n') : []));

function clearTimer() {
  if (toast.timerId) {
    clearTimeout(toast.timerId);
    toast.timerId = null;
  }
}

function close() {
  toast.visible = false;
  clearTimer();
}

onBeforeUnmount(() => {
  clearTimer();
});
</script>

<style scoped>
.toast {
  position: fixed;
  top: 1rem;
  left: 50%;
  transform: translateX(-50%);
  min-width: 280px;
  max-width: min(92vw, 640px);
  padding: 0.9rem 1rem;
  border-radius: 12px;
  border: 1px solid rgba(108, 92, 231, 0.22);
  box-shadow: 0 18px 40px rgba(0, 0, 0, 0.28);
  background: rgba(28, 30, 50, 0.95);
  color: var(--text);
  display: flex;
  align-items: center;
  gap: 0.75rem;
  z-index: 1200;
}

.toast.info { border-left: 4px solid var(--accent); }
.toast.success { border-left: 4px solid var(--success); }
.toast.error { border-left: 4px solid var(--error); background: rgb(114, 11, 0); }

.toast-text { flex: 1; font-size: .95rem; line-height: 1.4; }

.toast-close {
  background: transparent; border: none; color: var(--text);
  cursor: pointer; font-size: 1rem; width: 2rem; height: 2rem; border-radius: .4rem;
}
.toast-close:hover { background: rgba(255,255,255,0.08); }

.toast-fade-enter-from,
.toast-fade-leave-to {
  opacity: 0;
  transform: translateY(-12px) translateX(-50%) scale(.98);
}
.toast-fade-enter-to,
.toast-fade-leave-from {
  opacity: 1;
  transform: translateY(0) translateX(-50%) scale(1);
}
.toast-fade-enter-active,
.toast-fade-leave-active { transition: all 220ms ease; }
</style>