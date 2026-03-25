#!/usr/bin/env bash
set -u
set -o pipefail

# Colors
C_RESET="\033[0m"
C_BOLD="\033[1m"
C_GREEN="\033[92m"
C_YELLOW="\033[93m"
C_RED="\033[91m"
C_CYAN="\033[96m"
C_MAGENTA="\033[95m"

banner() {
    echo -e "${C_GREEN}${C_BOLD}╔════════════════════════════════════════════════════════════╗"
    echo                       "║                                                            ║"
    echo                       "║         🚀 Universal Media Downloader Dependencies         ║"
    echo                       "║                                                            ║"
    echo                       "╚════════════════════════════════════════════════════════════╝"
}

TOOLS="tools"
mkdir -p "$TOOLS"/{linux/{arm64,x64},mac/{arm64,x64}}

# Human readable size
human_size() {
    local n=${1:-0}
    if (( n >= 1073741824 )); then
        echo "$((n/1073741824))GB"
    elif (( n >= 1048576 )); then
        echo "$((n/1048576))MB"
    else
        echo "$((n/1024))KB"
    fi
}

# Get content-length
get_size() {
    curl -I -s -L -A "Mozilla/5.0" "$1" 2>/dev/null \
        | grep -i Content-Length | tail -1 | awk '{print $2}' | tr -d '\r' || echo 0
}

# Check if binary exists and is > 500 KB
is_real_binary() {
    local f="$1"
    if [[ -f "$f" ]]; then
        local sz
        sz=$(stat -c %s "$f" 2>/dev/null || stat -f %z "$f" 2>/dev/null || echo 0)
        (( sz > 500000 ))
    else
        return 1
    fi
}

# Detect host OS
detect_host_os() {
    local sys
    sys=$(uname -s | tr '[:upper:]' '[:lower:]')
    if [[ "$sys" == "linux" ]]; then
        echo "linux"
    elif [[ "$sys" == "darwin" ]]; then
        echo "mac"
    else
        echo "windows"
    fi
}

HOST_OS=$(detect_host_os)

# Extractor directory
EXTRACTOR_DIR="/tmp/extractors"
mkdir -p "$EXTRACTOR_DIR"

# Download modern 7z extractor (ALWAYS)
download_modern_7z() {
    local exe=""

    echo "Downloading Dependencies..."

    if [[ "$HOST_OS" == "linux" ]]; then
        local LINUX_ARCH
        LINUX_ARCH=$(uname -m)

        if [[ "$LINUX_ARCH" == "x86_64" ]]; then
            EXTRACTOR_URL="https://www.7-zip.org/a/7z2408-linux-x64.tar.xz"
        elif [[ "$LINUX_ARCH" == "aarch64" ]]; then
            EXTRACTOR_URL="https://www.7-zip.org/a/7z2408-linux-arm64.tar.xz"
        else
            echo "Unsupported Linux architecture: $LINUX_ARCH"
            exit 1
        fi

        echo "Linux arch detected: $LINUX_ARCH"

        mkdir -p "$EXTRACTOR_DIR/linux"
        curl -L --fail -o "$EXTRACTOR_DIR/linux/7z.tar.xz" "$EXTRACTOR_URL" || return 1
        tar -xf "$EXTRACTOR_DIR/linux/7z.tar.xz" -C "$EXTRACTOR_DIR/linux"

        chmod +x "$EXTRACTOR_DIR/linux/7zz"
        chmod +x "$EXTRACTOR_DIR/linux/7zzs"

        exe="$EXTRACTOR_DIR/linux/7zz"
        echo "$exe"
        return 0
    fi

    if [[ "$HOST_OS" == "mac" ]]; then
        local pkg="$EXTRACTOR_DIR/7z-mac.tar.xz"
        local url="https://www.7-zip.org/a/7z2301-mac.tar.xz"

        if [[ ! -f "$pkg" ]]; then
            echo -e "${C_YELLOW}⬇️  Downloading modern 7z extractor for macOS${C_RESET}"
            curl -L --fail -o "$pkg" "$url" || return 1
        fi

        mkdir -p "$EXTRACTOR_DIR/mac"
        tar -xf "$pkg" -C "$EXTRACTOR_DIR/mac"

        exe=$(find "$EXTRACTOR_DIR/mac" -type f -name "7zz" | head -1)
        chmod +x "$exe"

        echo "$exe"
        return 0
    fi
}


# Get extractor path
MODERN_7Z=$(download_modern_7z)
if [[ -z "$MODERN_7Z" ]]; then
    echo -e "${C_RED}❌ Could not prepare modern 7z extractor. Exiting.${C_RESET}"
    exit 1
fi


# Extract archive using modern 7z only
extract_archive() {
    local archive="$1"
    local dest="$2"

    case "$archive" in
        *.zip)
            unzip -q "$archive" -d "$dest"
            ;;
        *.tar.xz)
            tar -xf "$archive" -C "$dest"
            ;;
        *.7z)
            "$MODERN_7Z" x "$archive" -o"$dest" -y >/dev/null 2>&1 || return 1
            ;;
        *)
            return 1
            ;;
    esac
}




###############################################
# PART 2 — DOWNLOAD RETRY + TASK PROCESSOR
###############################################

declare -A RETRY_COUNTS
FAILED=()
FAILED_REASON=()

# Download with retry + resume
download_with_retry() {
    local url="$1"
    local dest="$2"
    local label="$3"
    local max_retries=10
    local attempt=0

    RETRY_COUNTS["$label"]=0

    while (( attempt < max_retries )); do
        attempt=$((attempt + 1))

        if [[ -f "$dest" ]]; then
            echo -e "${C_YELLOW}   Resuming (attempt $attempt/$max_retries)…${C_RESET}"
            if curl -L --fail --progress-bar -C - -o "$dest" "$url"; then
                if (( attempt > 1 )); then
                    RETRY_COUNTS["$label"]=$((attempt - 1))
                fi
                return 0
            fi
        else
            echo -e "${C_YELLOW}   Downloading (attempt $attempt/$max_retries)…${C_RESET}"
            if curl -L --fail --progress-bar -o "$dest" "$url"; then
                if (( attempt > 1 )); then
                    RETRY_COUNTS["$label"]=$((attempt - 1))
                fi
                return 0
            fi
        fi

        echo -e "${C_RED}   Attempt $attempt failed for $label${C_RESET}"
    done

    echo -e "${C_RED}❌ Failed after $max_retries attempts → $label${C_RESET}"
    return 1
}

# Find binary inside extracted folder
find_binary_for_type() {
    local root="$1"
    local type="$2"
    local os="$3"

    # Map type to binary name
    case "$type" in
        node|deno|ffmpeg)
            find "$root" -type f -name "$type" -print -quit
            ;;
        *)
            echo ""
            ;;
    esac
}


# Process a single download/extract task
process_task() {
    local task="$1"
    local current_os="$2"
    local current_arch="$3"

    IFS='|' read -r url final_path type tos tarch <<< "$task"

    # Skip if OS/arch mismatch
    [[ "$tos" != "$current_os" ]] && return
    [[ "$tarch" != "$current_arch" ]] && return

    local label="[$current_os/$current_arch] $final_path"

    # Skip if already exists
    if is_real_binary "$final_path"; then
        echo -e "${C_GREEN}✅ $label already OK${C_RESET}"
        return
    fi

    # Working directory
    local work="/tmp/umd_work_$$"
    rm -rf "$work"
    mkdir -p "$work"

    echo -e "${C_YELLOW}⬇️  $label${C_RESET}"
    local size
    size=$(get_size "$url")
    echo -e "${C_YELLOW}   ~$(human_size "$size") from $url${C_RESET}"

    local filename
    filename=$(basename "$url")
    local download_path="$work/$filename"

    # Download with retry
    if ! download_with_retry "$url" "$download_path" "$label"; then
        FAILED+=("$final_path")
        FAILED_REASON+=("download")
        rm -rf "$work"
        return
    fi

    mkdir -p "$(dirname "$final_path")"

    # Direct download (no extraction)
    if [[ "$type" == "direct" ]]; then
        mv "$download_path" "$final_path"
        [[ "$final_path" != *.exe ]] && chmod +x "$final_path"

        local rc=${RETRY_COUNTS["$label"]}
        if (( rc > 0 )); then
            echo -e "${C_YELLOW}⚠️  $label placed (direct) after $rc retries${C_RESET}"
        else
            echo -e "${C_GREEN}✅ $label placed (direct)${C_RESET}"
        fi

        rm -rf "$work"
        return
    fi

    # Extraction
    local extracted="$work/extracted"
    mkdir -p "$extracted"

    echo -e "${C_YELLOW}📦 Extracting archive for $label${C_RESET}"
    if ! extract_archive "$download_path" "$extracted"; then
        echo -e "${C_RED}❌ Extraction failed → $label${C_RESET}"
        FAILED+=("$final_path")
        FAILED_REASON+=("extraction")
        rm -rf "$work"
        return
    fi

    # Find binary
    local bin
    bin=$(find_binary_for_type "$extracted" "$type" "$current_os")

    if [[ -z "$bin" ]]; then
        echo -e "${C_RED}❌ Could not find $type binary inside archive for → $label${C_RESET}"
        FAILED+=("$final_path")
        FAILED_REASON+=("extraction")
        rm -rf "$work"
        return
    fi

    mv "$bin" "$final_path"
    [[ "$final_path" != *.exe ]] && chmod +x "$final_path"

    local rc=${RETRY_COUNTS["$label"]}
    if (( rc > 0 )); then
        echo -e "${C_YELLOW}⚠️  $label placed (extracted) after $rc retries${C_RESET}"
    else
        echo -e "${C_GREEN}✅ $label placed (extracted)${C_RESET}"
    fi

    rm -rf "$work"
}

###############################################
# PART 3 — OS/ARCH SELECTION + TASK LIST + MAIN LOOP
###############################################

banner
echo -e "${C_MAGENTA}📍 Working in current folder (should contain your project, e.g. .csproj)${C_RESET}\n"

echo -e "1 → All OS (linux + mac, both arch)"
echo -e "2 → Current OS only"
echo -e "3 → Specific OS (linux/mac)"
read -r -p "→ " mode

declare -a OS_LIST=()
declare -a ARCH_LIST=()

# Ask for architecture
select_arches() {
    local choice
    echo -e "\nSelect architecture:"
    echo "1 → x64"
    echo "2 → arm64"
    echo "3 → both"
    read -r -p "→ " choice

    case "$choice" in
        1) ARCH_LIST=(x64) ;;
        2) ARCH_LIST=(arm64) ;;
        3) ARCH_LIST=(x64 arm64) ;;
        *) ARCH_LIST=(x64) ;;
    esac
}

case "$mode" in
    2)
        sys=$(uname -s | tr '[:upper:]' '[:lower:]')
        if [[ "$sys" == "linux" ]]; then
            OS_LIST=(linux)
        elif [[ "$sys" == "darwin" ]]; then
            OS_LIST=(mac)
        fi
        select_arches
        ;;
    3)
        read -r -p "OS (linux/mac): " o
        case "$o" in
            linux|mac)
                OS_LIST=("$o")
                ;;
            *)
                echo -e "${C_RED}Invalid OS. Using all.${C_RESET}"
                OS_LIST=(linux mac)
                ;;
        esac
        select_arches
        ;;
    *)
        OS_LIST=(linux mac)
        ARCH_LIST=(x64 arm64)
        ;;
esac

echo -e "\nSelected OS targets: ${OS_LIST[*]}"
echo -e "Selected architectures: ${ARCH_LIST[*]}"
read -r -p "Continue? (y/N): " c
[[ "$c" != [yY] ]] && exit 0

# Latest Node version
NODE=$(curl -s https://nodejs.org/dist/index.json | grep -o '"version":"[^"]*"' | head -1 | cut -d'"' -f4 | sed 's/v//')

# Get latest Deno version (returns e.g. 2.7.7)
DENO=$(curl -fsSL https://dl.deno.land/release-latest.txt | tr -d 'v' || echo "2.7.7")


###############################################
# TASK LIST
###############################################
tasks=(
    # yt-dlp (direct)
    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux|tools/linux/x64/yt_dlp_linux_x64|direct|linux|x64"
    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64|tools/linux/arm64/yt_dlp_linux_arm64|direct|linux|arm64"
    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos|tools/mac/yt_dlp_mac_universal|direct|mac|x64"

    # Node
    "https://nodejs.org/dist/v${NODE}/node-v${NODE}-linux-x64.tar.xz|tools/linux/x64/node_linux_x64|node|linux|x64"
    "https://nodejs.org/dist/v${NODE}/node-v${NODE}-linux-arm64.tar.xz|tools/linux/arm64/node_linux_arm64|node|linux|arm64"
    "https://nodejs.org/dist/v${NODE}/node-v${NODE}-darwin-x64.tar.xz|tools/mac/x64/node_mac_x64|node|mac|x64"
    "https://nodejs.org/dist/v${NODE}/node-v${NODE}-darwin-arm64.tar.xz|tools/mac/arm64/node_mac_arm64|node|mac|arm64"

    # Deno
    "https://github.com/denoland/deno/releases/download/v${DENO}/deno-x86_64-unknown-linux-gnu.zip|tools/linux/x64/deno_linux_x64|deno|linux|x64"
    "https://github.com/denoland/deno/releases/download/v${DENO}/deno-aarch64-unknown-linux-gnu.zip|tools/linux/arm64/deno_linux_arm64|deno|linux|arm64"
    "https://github.com/denoland/deno/releases/download/v${DENO}/deno-x86_64-apple-darwin.zip|tools/mac/x64/deno_mac_x64|deno|mac|x64"
    "https://github.com/denoland/deno/releases/download/v${DENO}/deno-aarch64-apple-darwin.zip|tools/mac/arm64/deno_mac_arm64|deno|mac|arm64"

    # FFmpeg
    "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz|tools/linux/x64/ffmpeg_linux_x64|ffmpeg|linux|x64"
    "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz|tools/linux/arm64/ffmpeg_linux_arm64|ffmpeg|linux|arm64"
    "https://github.com/AR1Ablock/ffmpeg-macos-universal2/releases/download/latest/ffmpeg-universal-macos.zip|tools/mac/ffmpeg_mac_universal|ffmpeg|mac|x64"
)

###############################################
# MAIN LOOP
###############################################
for os in "${OS_LIST[@]}"; do
    for arch in "${ARCH_LIST[@]}"; do
        echo -e "\n${C_BOLD}${C_CYAN}▶ Processing OS: $os (arch: $arch)${C_RESET}"
        for t in "${tasks[@]}"; do
            process_task "$t" "$os" "$arch"
        done
    done
done

###############################################
# FINAL SUMMARY
###############################################
echo -e "\n${C_BOLD}${C_CYAN}═══════════════════════════════════════"
echo "✅ VERIFICATION"
echo "═══════════════════════════════════════${C_RESET}"

if command -v tree >/dev/null 2>&1; then
    tree tools | head -50
else
    find tools -maxdepth 3 -type f | sort
fi

echo

# Retry summary
declare -a RETRY_SUMMARY=()
for key in "${!RETRY_COUNTS[@]}"; do
    if (( RETRY_COUNTS["$key"] > 0 )); then
        RETRY_SUMMARY+=("$key (${RETRY_COUNTS[$key]} retries)")
    fi
done

if ((${#RETRY_SUMMARY[@]} > 0)); then
    echo -e "${C_YELLOW}⚠️ Succeeded after retries:${C_RESET}"
    for r in "${RETRY_SUMMARY[@]}"; do
        echo " - $r"
    done
    echo
fi

# Failed summary
if ((${#FAILED[@]} > 0)); then
    echo -e "${C_RED}❌ FAILED:${C_RESET}"
    for i in "${!FAILED[@]}"; do
        echo " - ${FAILED[$i]} (${FAILED_REASON[$i]})"
    done
else
    echo -e "${C_GREEN}All requested tools downloaded and placed successfully.${C_RESET}"
fi

echo -e "\n${C_MAGENTA}✨ Done. Your tools folder is now production-ready.${C_RESET}"


