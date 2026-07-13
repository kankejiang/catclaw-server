/**
 * CatClaw Music Server — Reactive Store
 * Vue 3 reactive() based state management.
 */
import { reactive, watch } from 'vue';
import { api } from './api.js';

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
  volume: 0.8,
  isMuted: false,
  shuffle: false,
  repeat: 'off',     // 'off' | 'all' | 'one'
  streamMode: 'direct', // 'direct' | 'hls'

  // UI
  showLyrics: false,
  showQueue: false,
  nowPlayingFull: false,
  sidebarCollapsed: false,
  searchQuery: '',

  // Lyrics
  lyrics: null,
  currentLyricLine: -1,

  // Favorites cache
  favoriteIds: new Set(),

  // Toast notifications
  toasts: []
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
  const toast = { id: Date.now(), message, type };
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
