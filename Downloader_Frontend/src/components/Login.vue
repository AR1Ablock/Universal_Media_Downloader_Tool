<template>
  <transition name="fade" mode="out-in">
    <div class="login-page" v-if="!showModal" key="login">
      <h2 class="login-title">🔒 Guest Login</h2>
      <input
        ref="userInput"
        v-model="username"
        @keydown.enter.prevent="focusPassword"
        placeholder="Username"
        class="login-input"
      />
      <input
        ref="passInput"
        v-model="password"
        type="password"
        placeholder="Password"
        @keydown.enter.prevent="prepareLogin"
        class="login-input"
      />
      <button @click="prepareLogin" :disabled="loggingIn" class="btn login-btn">
        <span v-if="!loggingIn">OK</span>
        <span v-else class="loader"></span>
      </button>
    </div>
  </transition>

  <!-- Info Modal -->
  <transition name="fade-scale">
    <div class="modal-overlay" v-if="showModal" key="modal">
      <div class="modal">
        <h3 class="modal-title">Session Information</h3>
        <p>
          Remember your username and password to view your downloads on any device with
          same login info.
        </p>
        <ul class="modal-list">
          <li v-if="AutoDelete">
            🔒 Completed downloads expire in <strong>2 hours</strong>.
          </li>
          <li v-if="AutoDelete">
            ⏳ Incomplete downloads expire in <strong>6 hours</strong>.
          </li>
          <li>
            ⚙️ Getting resolution, restarting or fix borken link download may take
            <strong>10–30s</strong>.
          </li>
          <li>
            🌐 Some video qualities may be area-restricted. If getting video resolution or
            download fails, just retry—or enable a VPN and try again.
          </li>
        </ul>
        <button @click="confirmLogin" class="btn modal-btn">Understood</button>
      </div>
    </div>
  </transition>
</template>

<script setup>
import { ref, onMounted } from "vue";
const emit = defineEmits(["logged-in"]);
let tempKey = "";
const username = ref("");
const password = ref("");
const loggingIn = ref(false);
const showModal = ref(false);
const userInput = ref(null);
const passInput = ref(null);
let AutoDelete = ref(false);

// focus the username on mount
onMounted(() => {
  userInput.value && userInput.value.focus();
});

async function sha256(input) {
  const data = new TextEncoder().encode(input);
  const hashBuffer = await crypto.subtle.digest("SHA-256", data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map((b) => b.toString(16).padStart(2, "0")).join("");
}

function focusPassword() {
  passInput.value && passInput.value.focus();
}

async function prepareLogin() {
  if (!username.value || !password.value) {
    alert("Please enter both username and password.");
    return;
  }
  loggingIn.value = true;
  try {
    tempKey = await sha256(`${username.value}:${password.value}`);
    showModal.value = true;
  } catch (err) {
    console.error(err);
    alert("Login failed; please try again.");
  } finally {
    loggingIn.value = false;
  }
}

function confirmLogin() {
  showModal.value = false;
  emit("logged-in", tempKey);
}
</script>

<style scoped>
/* Fade transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.3s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

/* Modal scale + fade */
.fade-scale-enter-active {
  transition: all 0.3s ease-out;
}
.fade-scale-leave-active {
  transition: all 0.2s ease-in;
}
.fade-scale-enter-from {
  opacity: 0;
  transform: scale(0.8);
}
.fade-scale-leave-to {
  opacity: 0;
  transform: scale(0.8);
}

.login-page {
  margin: 100px auto;
  padding: 2rem;
  background: #fff;
  border-radius: 12px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 1rem;
  width: 90%;
}
.login-title {
  font-size: clamp(1.3rem, 2.5vw, 2rem);
  text-align: center;
  color: #333;
  font-weight: bolder;
  margin: 0 0 0.5rem 0;
}
.login-input {
  padding: 0.75rem 1rem;
  font-size: 1rem;
  border: 1px solid #ccc;
  border-radius: 8px;
  transition: border-color 0.2s;
}
.login-input:focus {
  border-color: #007bff;
  outline: none;
}
.login-btn {
  padding: 0.75rem;
  font-size: 1rem;
  background: #007bff;
  color: #fff;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.2s;
}
.login-btn:hover:not(:disabled) {
  background: #0095ff;
}
.login-btn:disabled {
  background: #a0cfff;
  cursor: not-allowed;
}

/* Loader inside OK button */
.loader {
  width: 1rem;
  height: 1rem;
  border: 2px solid #fff;
  border-top: 2px solid transparent;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  margin: 0 auto;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

/* Modal Styles */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}
.modal {
  background: #5b62ca;
  color: #ffffff;
  font-family: sans-serif;
  font-size: clamp(0.8rem, 1.5vw, 1.5rem);
  letter-spacing: 1px;
  padding: 1rem;
  border-radius: 12px;
  width: clamp(15rem, 80vw, 50rem);
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.1);
  text-align: center;
}
.modal-title {
  margin-top: 0;
  font-size: clamp(1rem, 2vw, 2rem);
  color: #f5f5f5;
  font-family: sans-serif;
  font-weight: bolder;
}
.modal-list {
  text-align: left;
  margin: 1rem 0;
  padding-left: 1.2em;
}
.modal-list li {
  margin-bottom: 0.5rem;
}
.modal-btn {
  padding: 0.75rem 1.5rem;
  font-size: 1rem;
  background: #28a745;
  color: #fff;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.2s;
}
.modal-btn:hover {
  background: #2bcd50;
}
</style>
