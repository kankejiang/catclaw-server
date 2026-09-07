/**
 * CatClaw Music Server — Reactive Store
 * Vue 3 reactive() based state management.
 */
import { reactive, watch } from 'vue';
import { api } from './api.js';

// ── 持久化偏好（localStorage）──
const PREFS_KEY = 'catclaw_prefs';

function loadPrefs() {
  try {
    const raw = localStorage.getItem(PREFS_KEY);
    if (raw) return JSON.parse(raw);
  } catch { /* ignore */ }
  return {};
}

function savePrefs() {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify({
      theme: store.theme,
      streamMode: store.streamMode,
      streamTranscode: store.streamTranscode,
      streamBitrate: store.streamBitrate,
      notifyOnNewSong: store.notifyOnNewSong,
      volume: store.volume
    }));
  } catch { /* ignore */ }
}

const prefs = loadPrefs();

export const store = reactive({
  // Auth
  user: null,
  isAuthenticated: api.isAuthenticated,

  // Library
  overview: null,

  // Player state
  currentSong: null,
  isPlaying: false,
  queue: [],
  queueIndex: -1,
  progress: 0,       // 0-1
  currentTime: 0,    // seconds
  duration: 0,       // seconds
  volume: prefs.volume ?? 0.8,
  isMuted: false,
  shuffle: false,
  repeat: 'off',     // 'off' | 'all' | 'one'
  streamMode: prefs.streamMode || 'direct', // 'direct' | 'hls'
  streamTranscode: prefs.streamTranscode || 'off', // 'off' | 'mp3' | 'aac' | 'opus'
  streamBitrate: prefs.streamBitrate || 0, // 0 = 原码率, 128/192/320

  // UI
  showLyrics: false,
  showQueue: false,
  nowPlayingFull: false,
  sidebarCollapsed: localStorage.getItem('catclaw_sidebar_collapsed') === '1',
  searchQuery: '',

  // Preferences
  theme: prefs.theme || 'dark', // 'dark' | 'light' | 'system'
  notifyOnNewSong: prefs.notifyOnNewSong ?? false,

  // Lyrics
  lyrics: null,
  currentLyricLine: -1,

  // Favorites cache
  favoriteIds: new Set(),

  // Toast notifications
  toasts: [],

  // scrobble
  _scrobbled: false
});

// ── 主题应用 ──
export function applyTheme(theme) {
  let resolved = theme;
  if (theme === 'system') {
    resolved = window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }
  document.documentElement.setAttribute('data-theme', resolved);
}

applyTheme(store.theme);

export function setTheme(theme) {
  store.theme = theme;
  applyTheme(theme);
  savePrefs();
}

watch(() => store.volume, () => savePrefs());
watch(() => store.streamMode, () => savePrefs());
watch(() => store.streamTranscode, () => savePrefs());
watch(() => store.streamBitrate, () => savePrefs());

// 监听系统主题变化（system 模式）
if (window.matchMedia) {
  window.matchMedia('(prefers-color-scheme: light)').addEventListener('change', (e) => {
    if (store.theme === 'system') {
      applyTheme('system');
    }
  });
}

// ── 侧边栏折叠持久化 ──
watch(() => store.sidebarCollapsed, (v) => {
  localStorage.setItem('catclaw_sidebar_collapsed', v ? '1' : '0');
});

// ── Actions ──

export async function login(username, password) {
  const data = await api.login(username, password);
  store.user = data.user;
  store.isAuthenticated = true;
  return data;
}

export async function logout() {
  await api.logout();
  store.user = null;
  store.isAuthenticated = false;
}

export async function loadUser() {
  if (!api.isAuthenticated) return;
  try {
    store.user = await api.getMe();
    store.isAuthenticated = true;
  } catch {
    store.isAuthenticated = false;
  }
}

export async function loadOverview() {
  try {
    store.overview = await api.getOverview();
  } catch { /* ignore */ }
}

export function addToQueue(song) {
  store.queue.push(song);
}

export function playSong(song, queue, index) {
  // 提交上一首歌的 scrobble（切歌时，基于实际播放时长）
  _flushScrobble();

  store.currentSong = song;
  store.queue = queue || [song];
  store.queueIndex = index ?? 0;
  store.isPlaying = true;
  store.lyrics = null;
  store.currentLyricLine = -1;
  store._scrobbled = false;  // 重置当前歌曲的 scrobble 标记

  // Load lyrics in background
  loadLyrics(song.id);
}

/// 提交当前歌曲的 scrobble（播放超过 10 秒才记录，每首歌仅提交一次）
export function flushScrobble() {
  if (store._scrobbled) return;
  const songId = store.currentSong?.id;
  if (!songId) return;
  const playedMs = Math.floor((store.currentTime || 0) * 1000);
  if (playedMs >= 10000) {
    api.scrobble(songId, playedMs, 'library').catch(() => {});
    store._scrobbled = true;
  }
}

// 切歌前提交旧歌的 scrobble
function _flushScrobble() {
  if (store._scrobbled) return;
  const songId = store.currentSong?.id;
  if (!songId) return;
  const playedMs = Math.floor((store.currentTime || 0) * 1000);
  if (playedMs >= 10000) {
    api.scrobble(songId, playedMs, 'library').catch(() => {});
  }
  store._scrobbled = true;  // 标记已提交，防止重复
}

export async function loadLyrics(songId) {
  try {
    const data = await api.getLyrics(songId);
    if (store.currentSong?.id === songId) {
      store.lyrics = data;
    }
  } catch {
    store.lyrics = null;
  }
}

export function toggleFavorite(songId) {
  if (store.favoriteIds.has(songId)) {
    store.favoriteIds.delete(songId);
    api.removeFavorite(songId).catch(() => store.favoriteIds.add(songId));
  } else {
    store.favoriteIds.add(songId);
    api.addFavorite(songId).catch(() => store.favoriteIds.delete(songId));
  }
}

export function isFavorite(songId) {
  return store.favoriteIds.has(songId);
}

export function showToast(message, type = 'info') {
  const toast = { id: Date.now() + Math.random(), message, type };
  store.toasts.push(toast);
  setTimeout(() => {
    const idx = store.toasts.indexOf(toast);
    if (idx >= 0) store.toasts.splice(idx, 1);
  }, 3000);
}

export function formatTime(seconds) {
  if (!seconds || isNaN(seconds)) return '0:00';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export function formatDuration(ms) {
  return formatTime(ms / 1000);
}
