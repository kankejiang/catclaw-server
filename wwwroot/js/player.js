/**
 * CatClaw Music Server — Audio Player
 * Supports direct streaming and HLS adaptive bitrate via hls.js
 */
import { watch } from 'vue';
import { store, playSong, loadLyrics, flushScrobble, showToast } from './store.js';
import { api } from './api.js';

class AudioPlayer {
  constructor() {
    this.audio = new Audio();
    this.hls = null;
    this._animFrame = null;
    this._initialized = false;
  }

  init() {
    if (this._initialized) return;
    this._initialized = true;

    this.audio.volume = store.volume;
    this.audio.preload = 'auto';

    this.audio.addEventListener('play', () => { store.isPlaying = true; });
    this.audio.addEventListener('pause', () => {
      // 切歌时设置新 src 会触发 pause，此时不应改变 isPlaying 状态
      if (!this._loading) store.isPlaying = false;
    });
    this.audio.addEventListener('ended', () => { this._onEnded(); });
    this.audio.addEventListener('error', (e) => {
      console.error('Audio error:', e);
      showToast('播放出错，尝试切换流模式', 'error');
    });

    this.audio.addEventListener('loadedmetadata', () => {
      store.duration = this.audio.duration;
    });

    // Progress tracking via requestAnimationFrame
    const tick = () => {
      if (this.audio.duration && isFinite(this.audio.duration)) {
        store.currentTime = this.audio.currentTime;
        store.duration = this.audio.duration;
        store.progress = this.audio.currentTime / this.audio.duration;
      }
      this._animFrame = requestAnimationFrame(tick);
    };
    this._animFrame = requestAnimationFrame(tick);

    // Watch store state
    watch(() => store.currentSong, (song) => {
      if (song) this._loadSong(song);
    });

    watch(() => store.volume, (v) => {
      this.audio.volume = v;
      store.isMuted = v === 0;
    });
  }

  _loadSong(song) {
    // 保存期望的播放状态 — 设置新 src 会触发 pause 事件，
    // 导致 store.isPlaying 被置为 false，需在加载完成后恢复
    const shouldPlay = store.isPlaying;
    this._loading = true;

    // Destroy previous HLS instance
    if (this.hls) {
      this.hls.destroy();
      this.hls = null;
    }

    const useHls = store.streamMode === 'hls' && typeof Hls !== 'undefined' && Hls.isSupported();

    if (useHls) {
      this.hls = new Hls({
        maxBufferLength: 30,
        maxMaxBufferLength: 60,
        enableWorker: true,
        // 为所有 HLS 子请求（index.m3u8 + 分片）附加 JWT 头，
        // 避免 401 导致分片加载失败。
        xhrSetup: (xhr) => {
          if (api.accessToken) {
            xhr.setRequestHeader('Authorization', `Bearer ${api.accessToken}`);
          }
        }
      });
      this.hls.loadSource(api.getHlsUrl(song.id));
      this.hls.attachMedia(this.audio);
      this.hls.on(Hls.Events.ERROR, (_, data) => {
        if (data.fatal) {
          console.warn('HLS fatal error, falling back to direct stream');
          this.hls.destroy();
          this.hls = null;
          this.audio.src = api.getStreamUrl(song.id);
          if (shouldPlay) this.audio.play().catch(() => {});
        }
      });
    } else {
      this.audio.src = api.getStreamUrl(song.id);
    }

    this._loading = false;
    if (shouldPlay) {
      store.isPlaying = true;  // 恢复期望状态（pause 事件可能已置 false）
      this.audio.play().catch(() => {});
    }
  }

  play() {
    if (!store.currentSong && store.queue.length > 0) {
      this.playIndex(0);
      return;
    }
    this.audio.play().catch(() => {});
  }

  pause() {
    this.audio.pause();
  }

  toggle() {
    if (store.isPlaying) this.pause();
    else this.play();
  }

  playIndex(index) {
    if (index < 0 || index >= store.queue.length) return;
    const song = store.queue[index];
    playSong(song, store.queue, index);
  }

  next() {
    if (store.repeat === 'one') {
      this.audio.currentTime = 0;
      this.audio.play().catch(() => {});
      return;
    }

    let nextIndex = store.queueIndex + 1;
    if (store.shuffle) {
      nextIndex = Math.floor(Math.random() * store.queue.length);
    }
    if (nextIndex >= store.queue.length) {
      if (store.repeat === 'all') nextIndex = 0;
      else return;
    }
    this.playIndex(nextIndex);
  }

  prev() {
    if (this.audio.currentTime > 3) {
      this.audio.currentTime = 0;
      return;
    }
    let prevIndex = store.queueIndex - 1;
    if (prevIndex < 0) {
      if (store.repeat === 'all') prevIndex = store.queue.length - 1;
      else prevIndex = 0;
    }
    this.playIndex(prevIndex);
  }

  seek(fraction) {
    if (this.audio.duration) {
      this.audio.currentTime = fraction * this.audio.duration;
    }
  }

  setVolume(v) {
    store.volume = Math.max(0, Math.min(1, v));
  }

  toggleMute() {
    if (store.isMuted) {
      store.volume = store._prevVolume || 0.8;
      store.isMuted = false;
    } else {
      store._prevVolume = store.volume;
      store.volume = 0;
      store.isMuted = true;
    }
  }

  // 统一播放模式切换：顺序播放 → 单曲循环 → 随机播放 → 顺序播放
  togglePlayMode() {
    if (!store.shuffle && store.repeat === 'off') {
      // 顺序播放 → 单曲循环
      store.repeat = 'one';
      store.shuffle = false;
    } else if (!store.shuffle && store.repeat === 'one') {
      // 单曲循环 → 随机播放
      store.repeat = 'off';
      store.shuffle = true;
    } else {
      // 随机播放（或其他）→ 顺序播放
      store.repeat = 'off';
      store.shuffle = false;
    }
  }

  toggleStreamMode() {
    store.streamMode = store.streamMode === 'direct' ? 'hls' : 'direct';
    if (store.currentSong) {
      const wasPlaying = store.isPlaying;
      this._loadSong(store.currentSong);
      if (wasPlaying) this.audio.play().catch(() => {});
    }
  }

  _onEnded() {
    store.isPlaying = false;
    // 播放结束时提交 scrobble（每首歌仅一次）
    flushScrobble();

    if (store.repeat === 'one') {
      this.audio.currentTime = 0;
      store.isPlaying = true;
      this.audio.play().catch(() => {});
      return;
    }

    this.next();
  }

  destroy() {
    if (this._animFrame) cancelAnimationFrame(this._animFrame);
    if (this.hls) this.hls.destroy();
    this.audio.pause();
  }
}

export const player = new AudioPlayer();
