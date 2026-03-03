<template>
  <header>
    <h1>🚀 Media Downloader</h1>
    <!-- Desktop Sign Out Button -->
    <button v-if="userKey" @click="signOut" class="signout-btn desktop-signout" title="Sign Out">
      Sign Out
    </button>
    <!-- Mobile Menu Toggle -->
    <button v-if="userKey" @click="toggleMobileMenu" class="menu-toggle mobile-only" title="Menu">
      ≡
    </button>
  </header>
  <!-- Mobile Right Sidebar Panel -->
  <div v-if="userKey && mobileMenuOpen" class="mobile-panel-overlay" @click="toggleMobileMenu"></div>
  <div v-if="userKey && mobileMenuOpen" class="mobile-panel">
    <button class="close-panel-btn" @click="toggleMobileMenu">✕</button>
    <button @click="signOut" class="signout-btn mobile-signout">Sign Out</button>
  </div>
  <div class="container">
    <Login v-if="!userKey" @logged-in="onLoggedIn" />
    <div v-else>
      <div class="form" :class="{ edge_form: Is_Browser_Edge }">
        <div class="inner-container">
          <p class="Video_Title" :style="{ display: Title ? 'block' : 'none' }">
            {{ Title }}
          </p>
          <img v-show="thumbnailLink" class="thumbnail" :src="thumbnailLink" alt="Thumbnail" />
          <div class="rows">
            <div class="row">
              <input v-model="url" @keydown.enter.prevent="getFormats" @paste="onPaste" type="text"
                placeholder="Paste video URL..." autofocus required :class="{ edge_formInput: Is_Browser_Edge }" />
              <button :disabled="loadingFormats || loadingDownload" @click="getFormats" class="btn" :class="[
                { readjust_btn: loadingFormats },
                { 'error-btn': Is_Format_Error },
              ]">
                <div v-if="!loadingFormats">
                  <!-- Icon -->
                  <svg width="64" height="32" viewBox="0 0 32 24" fill="none" xmlns="http://www.w3.org/2000/svg"
                    stroke="white" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" class="arrow-icon">
                    <path d="M3 12h24M21 6l8 6-8 6" />
                  </svg>
                </div>
                <div v-else>
                  <div class="loader"></div>
                </div>
              </button>
            </div>
            <div class="row" v-show="videoOptions.length || audioOptions.length">
              <select v-if="videoOptions.length" v-model="selectedVideo">
                <option value="">Select Video Format</option>
                <option v-for="opt in videoOptions" :key="opt.id" :value="opt.id">
                  {{ opt.label }}
                </option>
              </select>
              <select v-if="audioOptions.length" v-model="selectedAudio">
                <option value="">Select Audio Format</option>
                <option v-for="opt in audioOptions" :key="opt.id" :value="opt.id">
                  {{ opt.label }}
                </option>
              </select>
            </div>
            <div class="row">
              <button id="downloadBtn" class="btn" :class="[
                { readjust_downloadBtn: loadingDownload },
                { 'error-btn': Is_Downlaod_Error },
              ]" v-show="formats.length" :disabled="loadingDownload" @click="startDownload">
                <div v-if="!loadingDownload">
                  <!-- Download Icon -->
                  <svg width="32" height="48" viewBox="0 0 24 32" fill="none" xmlns="http://www.w3.org/2000/svg"
                    stroke="white" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" class="arrow-icon">
                    <path d="M12 3v24M6 21l6 8 6-8" />
                  </svg>
                </div>
                <div v-else>
                  <div class="loader"></div>
                </div>
              </button>
            </div>
          </div>
        </div>
      </div>
      <section>
        <Download />
      </section>
      <transition name="slide">
        <div v-if="visible" class="info-modal">
          <button class="close-btn" @click="toggle">✕</button>
          <ul class="info-list">
            <li v-if="AutoDelete">
              🔒 Completed downloads stay available for <strong>2 hours</strong>.
            </li>
            <li v-if="AutoDelete">
              ⏳ Incomplete downloads expire after <strong>6 hours</strong>.
            </li>
            <li>
              ⚠️ Some media resolutions may show <strong>0 MB</strong> size, but they are
              still downloadable.
            </li>
            <li>
              ⚙️ Fetching resolution, restarting or fixing a broken download may take
              <strong>10–30s</strong>.
            </li>
            <li>
              🌐 Some video qualities may be geo‑restricted. If a download fails, retry—or
              enable a VPN and try again.
            </li>
          </ul>
        </div>
      </transition>
      <!-- Edge Icon to Reopen -->
      <button v-if="!visible" class="open-btn" @click="toggle">ℹ️</button>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onBeforeUnmount, watch, onUnmounted } from "vue";
import Download from "./components/Downloads.vue";
import Login from "./components/Login.vue";

let ServerUrl = "http://localhost:5050/Downloader";
// let ServerUrl = "https://mediadownloader.duckdns.org/Downloader";
const url = ref("");
let Title = ref("");
const formats = ref([]);
const videoOptions = ref([]);
const audioOptions = ref([]);
const selectedVideo = ref("");
const selectedAudio = ref("");
const thumbnailLink = ref("");
const jobs = ref([]);
let Is_Format_Error = ref(false);
let Is_Downlaod_Error = ref(false);
const loadingFormats = ref(false);
const loadingDownload = ref(false);

let downloadSpinnerTimeout = null;
let downloadStarted = false;
let lastDownloadId = null;
const jobUrls = reactive({});
const userKey = ref(null);
let Is_Browser_Edge = ref(false);
const mobileMenuOpen = ref(false);

const visible = ref(false);
let AutoDelete = ref(false);

function toggle() {
  visible.value = !visible.value;
}

function toggleMobileMenu() {
  mobileMenuOpen.value = !mobileMenuOpen.value;
}


function onLoggedIn(key) {
  userKey.value = key;
  console.log("key ", key);
  localStorage.setItem("MediaDownloaderUserKey", key);
}

function onPaste() {
  setTimeout(() => {
    if (/^https?:\/\/.+\..+/.test(url.value.trim())) {
      getFormats();
    }
  }, 10);
}

async function getFormats() {
  if (loadingFormats.value) {
    console.log("Already loading formats, please wait.");
    return;
  }

  if (isOffline.value) {
    Is_Format_Error.value = true;
    setTimeout(() => {
      Is_Format_Error.value = false;
      alert("Network is offline, Cant get media info.");
    }, 600);
    return;
  }

  if (url.value.trim() === "" || !/^https?:\/\/.+\..+/.test(url.value.trim())) {
    Is_Format_Error.value = true;
    setTimeout(() => {
      Is_Format_Error.value = false;
      alert("Please enter a valid URL.");
    }, 600);
    return;
  }
  loadingFormats.value = true;
  thumbnailLink.value = "";
  Title.value = "";
  formats.value = [];
  videoOptions.value = [];
  audioOptions.value = [];
  try {
    const res = await fetch(`${ServerUrl}/formats`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ url: url.value }),
    });
    if (!res.ok) throw new Error(await res.text());

    const data = await res.json();

    if (data.loginRequired) {
      Is_Format_Error.value = true;
      setTimeout(() => {
        Is_Format_Error.value = false;
        window.alert(data.message || "Incorrect URL / Login required");
        window.open(data.url, "_blank");
      }, 600);
      return;
    }
    console.log("Received formats:", data);

    if (data.length === 0) {
      Is_Format_Error.value = true;
      setTimeout(() => {
        Is_Format_Error.value = false;
        alert("No Media Format Found.");
      }, 600);
      return;
    }

    selectedVideo.value = "";
    selectedAudio.value = "";

    formats.value = data;
    // process formats
    data.forEach((f) => {
      const label = f.label || "";
      if (f.thumbnail) {
        thumbnailLink.value = f.thumbnail;
      }
      if (f.title) {
        Title.value = f.title;
      }
      const hasResolution = /\b\d{2,4}x\d{2,4}\b/.test(label);
      const isAudio = f.isAudioOnly || label.toLowerCase().includes("audio only");
      const isVideo = f.isVideoOnly || /\b\d{3,4}p\b/.test(label) || hasResolution;
      if (isVideo && !isAudio) videoOptions.value.push(f);
      else if (isAudio && !isVideo) audioOptions.value.push(f);
      else videoOptions.value.push(f);
    });
  } catch (e) {
    console.error(e);
    Is_Format_Error.value = true;
    handleNetworkError(e, "getFormats", "F");
  } finally {
    loadingFormats.value = false;
  }
}

function generateDownloadId() {
  return `dl_${Date.now()}_${Math.floor(Math.random() * 1e6)}`;
}

async function startDownload() {
  if (loadingDownload.value) {
    console.log("Download already in progress, please wait.");
    return;
  }

  if (isOffline.value) {
    Is_Downlaod_Error.value = true;
    setTimeout(() => {
      Is_Downlaod_Error.value = false;
      alert("Network is offline, Cant Download media.");
    }, 600);
    return;
  }


  if (url.value.trim() === "" || !/^https?:\/\/.+\..+/.test(url.value.trim())) {
    Is_Downlaod_Error.value = true;
    setTimeout(() => {
      Is_Downlaod_Error.value = false;
      alert("Please enter a valid URL.");
    }, 600);
    return;
  }

  downloadStarted = false;
  lastDownloadId = generateDownloadId();
  try {
    let format = "";

    if (selectedVideo.value && selectedAudio.value) {
      format = `${selectedVideo.value}+${selectedAudio.value}`;
    } else if (selectedVideo.value) {
      format = selectedVideo.value;
    } else if (selectedAudio.value) {
      format = selectedAudio.value;
    }

    if (!format) {
      Is_Downlaod_Error.value = true;
      setTimeout(() => {
        Is_Downlaod_Error.value = false;
        alert("Please select format.");
      }, 600);
      return;
    }

    loadingDownload.value = true;

    let obj = {
      url: url.value,
      format,
      DownloadId: lastDownloadId,
      Thumbnail: thumbnailLink.value,
      Key: userKey.value,
    };

    console.log("sending to BE ", obj);

    const res = await fetch(`${ServerUrl}/download`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(obj),
    });
    if (!res.ok) throw new Error(await res.text());
    if (downloadSpinnerTimeout) clearTimeout(downloadSpinnerTimeout);
    downloadSpinnerTimeout = setTimeout(() => {
      if (!downloadStarted) loadingDownload.value = false;
    }, 10000);
  } catch (e) {
    console.error(e);
    Is_Downlaod_Error.value = true;
    handleNetworkError(e, "startDownload", "D");
    loadingDownload.value = false;
  }
}

let lastNetworkErrorTime = 0;
const NETWORK_ERROR_PAUSE_MS = 1000; // 30 seconds

function handleNetworkError(error, context = "action", method) {
  let message = '';

  if (!isOffline.value) {
    const Unable_To_Downloader = error.message.includes('Unable to download webpage');
    const Coeection_Timeout = error.message.includes('Retrying');
    const isNetworkError = error.message.includes("fetch");
    const UnSupported_URL = error.message.includes("Unsupported URL");
    const Permanent_Error = error.message.includes("MEDIA_PERMANENT_ERROR");
    message = isNetworkError ? "🚨 Server unreachable. Try again later." :
      UnSupported_URL ? 'URL is not supported by this app.' :
        Permanent_Error ? 'This media is private, unavailable, or age / geo restricted.' :
          Unable_To_Downloader ? 'Unable to download the media' :
            Coeection_Timeout ? 'Please Check Your Internet, Connection Timeout' : error.message || "Unknown error occurred.";
  }
  else {
    message = 'Network Not Available Disconnected'
  }

  const now = Date.now();
  if (!lastNetworkErrorTime || now - lastNetworkErrorTime > NETWORK_ERROR_PAUSE_MS) {
    setTimeout(() => {
      if (method == "D") Is_Downlaod_Error.value = false;
      else if (method == "F") Is_Format_Error.value = false;
      alert(message);
    }, 600);
    lastNetworkErrorTime = now;
  }
  console.error(`Error during ${context}:`, error.message);
}

// Reactive flag for network status
const isOffline = ref(false);


async function pauseAllJobs() {
  try {
    const response = await fetch(`${ServerUrl}/Pause_All_Tasks`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      }
    })

    if (!response.ok) {
      throw new Error(`Server returned ${response.status}`)
    }

    const data = await response?.json()
    console.log('pause all job result: ' + data.jobs);
  } catch (err) {
    console.log('error in network off pause all method: ' + err);
  }
}


// Method to update network status
function updateNetworkStatus() {
  isOffline.value = !navigator.onLine
  if (isOffline.value) {
    pauseAllJobs();
  }
}

function esc_close_event(e) {
  if (e.key === "Escape") {
    visible.value = false;
    mobileMenuOpen.value = false;
  }
}


// Lifecycle hooks to attach/remove event listeners
onMounted(() => {

  // auto-show when app loads
  setTimeout(() => {
    visible.value = true;
  }, 1000);

  window.addEventListener("keydown", esc_close_event);


  userKey.value = localStorage.getItem("MediaDownloaderUserKey");

  if (navigator.userAgentData?.brands?.some((b) => b.brand === "Microsoft Edge")) {
    Is_Browser_Edge.value = true;
  }
  // Initial check
  updateNetworkStatus()

  // Listen for browser online/offline events
  window.addEventListener('online', updateNetworkStatus)
  window.addEventListener('offline', updateNetworkStatus)
});


onUnmounted(() => {
  window.removeEventListener('online', updateNetworkStatus)
  window.removeEventListener('offline', updateNetworkStatus)
  window.removeEventListener("keydown", esc_close_event);
});


function signOut() {
  mobileMenuOpen.value = false;
  localStorage.removeItem("MediaDownloaderUserKey");
  userKey.value = null;
  url.value = "";
  Title.value = "";
  formats.value = [];
  videoOptions.value = [];
  audioOptions.value = [];
  selectedVideo.value = "";
  selectedAudio.value = "";
  thumbnailLink.value = "";
  jobs.value = [];
  window.location.reload();
}
</script>
