<template>
  <div>
    <h2 class="heading">Downloads</h2>
    <div id="downloads">
      <div v-for="job in jobs" :key="job.id" class="download-card">
        <img :src="job.thumbnail" class="thumbnail" />

        <!-- small-screen overlay controls -->
        <div class="right-controls small-overlay">
          <button @click="copyUrl(job.id)" class="icon-button copy" title="Copy URL">
            <i class="fas fa-copy" /> Copy
          </button>
          <button @click="deleteUI(job.id)" class="icon-button icon-delete-ui remove" title="Remove from UI"
            :disabled="jobCooling[job.id]">
            <i class="fas fa-times" />
          </button>
        </div>

        <div class="info">
          <div class="top-controls">
            <div class="title">{{ job.title }}</div>
            <!-- keep the normal controls for desktop -->
            <div class="right-controls desktop-only">
              <button @click="copyUrl(job.id)" class="icon-button copy" title="Copy URL">
                <i class="fas fa-copy" /> Copy
              </button>
              <button @click="deleteUI(job.id)" class="icon-button icon-delete-ui remove" title="Remove from UI"
                :disabled="jobCooling[job.id]">
                <i class="fas fa-times" />
              </button>
            </div>
          </div>

          <div class="progress-bar-container">
            <div class="progress-bar" :style="{ width: (job.progress?.toFixed(2) ?? 0) + '%' }" />
          </div>

          <div class="details">
            <span>{{ job.downloaded }} MB / {{ job.total }} MB</span>
            <span>Speed: {{ job.speed
            }}{{ job.speed !== "N/A" && job.speed !== "0" ? "/s" : "" }}</span>
            <span>Progress: {{ job.progress?.toFixed(2) ?? 0 }}%</span>
            <span>Job: {{ job.status }}</span>
          </div>

          <div class="buttons">
            <button @click="pause(job.id)" class="btn pause"
              :disabled="jobCooling[job.id] || job.status == 'completed'">
              <i class="fas fa-pause" /> Pause
            </button>
            <button @click="resume(job.id)" class="btn resume"
              :disabled="jobCooling[job.id] || job.status == 'completed'">
              <i class="fas fa-play" /> Resume
            </button>
            <button @click="restart(job.id)" class="btn restart"
              :disabled="jobCooling[job.id] || job.status == 'completed'">
              <i class="fas fa-redo" /> Restart
            </button>
            <button @click="newUrl(job.id)" class="btn resume_broken"
              :disabled="jobCooling[job.id] || job.status == 'completed'">
              <i class="fas fa-link" /> Fix URL
            </button>
            <button @click="deleteFile(job.id)" class="btn delete icon-delete-file" :disabled="jobCooling[job.id]">
              <i class="fas fa-trash" /> Delete File
            </button>
            <button v-if="Is_Download_Enabled" @click="downloadFile(job.id)" class="btn download"
              :disabled="job.status !== 'completed'">
              <i class="fas fa-download"></i> Download
            </button>
            <button @click="Open_File_Directory(job.id)" class="btn download" :disabled="job.status !== 'completed'">
              <i class="fas fa-folder-open"></i> Open
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted, watch } from "vue";

const jobs = ref([]);
const jobUrls = reactive({});
let ServerUrl = "http://localhost:5050/Downloader";
// let ServerUrl = "https://mediadownloader.duckdns.org/Downloader";
let userKey = ref("");
let Is_Download_Enabled = ref(false);
let timer;


let eventSource = null;

const connectToSse = () => {
  if (eventSource) eventSource.close(); // clean previous

  eventSource = new EventSource(`${ServerUrl}/progress-sse?Key=${userKey.value}`);

  eventSource.onmessage = (event) => {
    const data = JSON.parse(event.data);

    jobs.value = data.map((job) => {
      if (!jobUrls[job.id]) jobUrls[job.id] = job.url;

      const isCompleted = job.status === "completed";
      const downloaded = isCompleted ? job.total ?? 0 : job.downloaded ?? 0;
      const speed = isCompleted ? "0" : job.speed ?? "N/A";

      return {
        ...job,
        title: job.title || job.url,
        downloaded,
        total: job.total ?? 0,
        speed,
      };
    });
  };

  eventSource.onerror = (err) => {
    console.warn("SSE error (will auto-reconnect)", err);
    // Optional: handleNetworkError if you want
  };
};

// Start SSE when userKey is ready
watch(userKey, (newKey) => {
  if (newKey) connectToSse();
}, { immediate: true });



async function check_OS() {
  try {
    const res = await fetch(`${ServerUrl}/OS_Check`);
    let data = await res.json();
    if (data.is_Download_Enable) {
      Is_Download_Enabled.value = true;
    } else {
      Is_Download_Enabled.value = false;
    }
    console.log("OS -->", data);
  } catch (error) {
    console.log(error.message);
  }
}


onMounted(() => {
  userKey.value = localStorage.getItem("MediaDownloaderUserKey");
  check_OS();
  if (eventSource) eventSource.close();
});


onUnmounted(() => {
  clearInterval(timer)
  if (eventSource) eventSource.close();
});


let action_in_progress = ref(false);

async function postAction(path, payload) {
  if (action_in_progress.value) return;

  action_in_progress.value = true
  try {
    const response = await fetch(`${ServerUrl}/${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    })
    return response;
  } catch (error) {
    handleNetworkError(error, `postAction(${path})`)
  } finally {
    action_in_progress.value = false
  }
}

const pause = async (id) => await postAction("pause", { jobId: id })
const resume = async (id) => await postAction("resume", { jobId: id })
const deleteUI = async (id) => await postAction("delete-ui", { jobId: id })
const deleteFile = async (id) => await postAction("delete-file", { jobId: id })


async function Open_File_Directory(id) {
  try {
    const res = await postAction("open-file", { jobId: id });
    if (!res) return;
    const json = await res.json();

    if (json.message && json.message.includes("not found")) {
      alert(json.message);
    }
  } catch (err) {
    console.error("Failed to open file directory", err);
    alert("Error: could not open folder");
  }
}


async function restart(id) {
  try {
    const prev = jobs.value.find((j) => j.id === id)?.progress ?? 0;
    jobCooling[id] = true;
    watchForProgress(id, prev);
    await postAction("resume-fresh", { jobId: id });
  } catch (error) {
    console.log(error.message);
  }
};



async function newUrl(id) {
  try {
    const url = prompt("Enter new URL to resume the download:");
    if (url?.trim()) {
      const prev = jobs.value.find((j) => j.id === id)?.progress ?? 0;
      jobCooling[id] = true;
      watchForProgress(id, prev);
      await postAction("resume-new-url", { jobId: id, newUrl: url.trim() });
    } else {
      alert("No URL entered. Resume cancelled.");
    }
  } catch (error) {
    console.log(error.message);
  }
}



function copyUrl(id) {
  const text = jobUrls[id];
  if (text) {
    navigator.clipboard
      .writeText(text)
      .then(() => alert("✅ Video URL copied!"))
      .catch(() => alert("❌ Failed to copy"));
  } else {
    alert("⚠️ Original URL not found.");
  }
}


async function downloadFile(jobId) {
  if (!Is_Download_Enabled.value) return;
  if (action_in_progress.value) return;

  action_in_progress.value = true;

  try {
    const url = `${ServerUrl}/download-file/${jobId}`;

    const a = document.createElement("a");
    a.href = url;
    // No need to set a.download — the server already sends the correct filename!
    document.body.appendChild(a);
    a.click();
    a.remove();
  } catch (error) {
    console.error("Download error:", error);
    alert("Something went wrong while downloading.");
  } finally {
    action_in_progress.value = false;
  }
}


// src/utils/handleNetworkError.js
let lastNetworkErrorTime = 0;
const NETWORK_ERROR_PAUSE_MS = 30000; // 30 seconds



function handleNetworkError(error, context = "action") {
  const isNetworkError = error instanceof TypeError && error.message.includes("fetch");
  const message =
    isNetworkError || error.message.includes("Failed to fetch")
      ? "🚨 Server unreachable. Try again later."
      : error.message || "Unknown error occurred.";

  const now = Date.now();
  if (!lastNetworkErrorTime || now - lastNetworkErrorTime > NETWORK_ERROR_PAUSE_MS) {
    alert(message);
    lastNetworkErrorTime = now;
  }
  console.error(`Error during ${context}:`, error.message);
}

// track which jobs are in “cool-down” (i.e. buttons disabled)
const jobCooling = reactive({});

// keep track of timeouts so we can clear them later
const timeoutMap = {};

// remember the progress threshold we need to see
const initialProgress = reactive({});

// track active watcher cleanup functions
const unwatchMap = {};

function watchForProgress(id) {
  // If there's already a watcher for this job, tear it down
  if (unwatchMap[id]) {
    unwatchMap[id]();
    clearTimeout(timeoutMap[id]);
  }

  unwatchMap[id] = watch(
    () => {
      const job = jobs.value.find((j) => j.id === id);
      return {
        progress: job?.progress,
        status: job?.status,
      };
    },
    (newVal, oldVal) => {
      // End cooling if progress changes, or status becomes fail-err/failed
      if (
        (oldVal && newVal.progress !== oldVal.progress) ||
        newVal.status === "fail-err" ||
        newVal.status === "failed"
      ) {
        jobCooling[id] = false;
        unwatchMap[id](); // stop watching
        clearTimeout(timeoutMap[id]);
        delete unwatchMap[id];
      }
    },
    { immediate: false, deep: true }
  );

  timeoutMap[id] = setTimeout(() => {
    jobCooling[id] = false;
    unwatchMap[id](); // stop watching
    delete unwatchMap[id];
  }, 30_000);
}
</script>

<style scoped>
* {
  box-sizing: border-box;
}

.heading {
  font-size: clamp(1rem, 2vw, 3rem);
  font-weight: bold;
  color: #dcb7ff;
  text-align: center;
}

.download-card {
  background: #1b1b2f;
  border-radius: 16px;
  padding: 12px;
  width: 65vw;
  margin: auto auto 2rem;
  display: flex;
  gap: 12px;
  box-shadow: 0 0 10px #a855f755;
  position: relative;
  flex-wrap: wrap;
  transition: transform .3s ease-in;
  animation: fadeIn 0.5s ease-in;
}

.download-card:hover{
  cursor:pointer;
}

.thumbnail {
  width: 90px;
  height: 90px;
  background: #333;
  border-radius: 8px;
  flex-shrink: 0;
  object-fit: cover;
}

button {
  cursor: pointer;
}

.info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.top-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
}

.title {
  font-size: 16px;
  font-weight: bold;
  color: #dcb7ff;
}

.right-controls {
  display: flex;
  flex-direction: column-reverse;
  align-items: center;
  row-gap: 10px;
  flex-wrap: wrap;
}

.icon-button {
  background: none;
  border: none;
  color: #c084fc;
  cursor: pointer;
  font-size: 14px;
  display: flex;
  align-items: center;
  gap: 4px;
}

.icon-button:hover {
  color: #fff;
}

.icon-delete-ui {
  color: #f87171;
}

.icon-delete-file {
  color: #facc15;
}

.progress-bar-container {
  background: #333;
  border-radius: 10px;
  height: 0.8rem;
  overflow: hidden;
}

.progress-bar {
  background: linear-gradient(to right, #a855f7, #d946ef);
  height: 100%;
  transition: width 0.3s linear;
}

.details {
  font-size: 13px;
  color: #aaa;
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
}

.buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 6px;
}

.btn {
  flex: 1 1 100px;
  padding: 6px 8px;
  font-size: 13px;
  background: #2a2a40;
  border: none;
  border-radius: 8px;
  color: #ccc;
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 6px;
  transition: background 0.2s;
}

.btn i {
  color: #c084fc;
}

.btn:hover {
  background: #3a3a55;
  color: #fff;
}

/* hide duplicate on desktop */
.desktop-only {
  display: flex;
}

.small-overlay {
  display: none;
}

/* Responsive tweaks */
@media (max-width: 1400px) {
  .download-card {
    width: 80vw;
  }
}

@media (max-width: 1000px) {
  .download-card {
    width: 90vw;
  }
}

@media (max-width: 600px) {
  .download-card {
    width: 90vw;
    flex-direction: column;
    align-items: stretch;
  }

  .desktop-only {
    display: none;
  }

  .small-overlay {
    display: flex;
    flex-direction: column-reverse;
    position: absolute;
    right: 12px;
  }

  .right-controls {
    /* ensure the internal gap applies */
    gap: 15px;
  }

  .details {
    flex-direction: column;
    gap: 4px;
  }

  .buttons {
    flex-direction: column;
  }

  .btn {
    flex: 1 1 auto;
  }
}

/* Disabled state for all your .btn buttons */
.btn:disabled {
  background-color: rgba(255, 255, 255, 0.1);
  /* subtle light overlay */
  color: #777;
  /* medium-gray text */
  cursor: not-allowed;
  /* show forbidden cursor */
  opacity: 0.7;
  /* slightly faded */
  transition: none;
  /* no hover transitions */
}

/* Prevent hover styles from applying when disabled */
.btn:disabled:hover {
  background-color: rgba(255, 255, 255, 0.1);
}

/* If you also want your .icon-button to gray-out */
.icon-button:disabled {
  color: #777;
  cursor: not-allowed;
  opacity: 0.7;
}
</style>
