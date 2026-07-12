/**
 * CatClaw Music Server — Audio Player
 * Supports direct streaming and HLS adaptive bitrate via hls.js
 */
import { watch } from 'vue';
import { store, playSong, loadLyrics, showToast } from './store.js';
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
    this.audio.addEventListener('pause', () => { store.isPlaying = false; });
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
        enableWorker: true
      });
      this.hls.loadSource(api.getHlsUrl(song.id));
      this.hls.attachMedia(this.audio);
      this.hls.on(Hls.Events.ERROR, (_, data) => {
        if (data.fatal) {
          console.warn('HLS fatal error, falling back to direct stream');
          this.hls.destroy();
          this.hls = null;
          this.audio.src = api.getStreamUrl(song.id);
          this.audio.play().catch(() => {});
        }
      });
    } else {
      this.audio.src = api.getStreamUrl(song.id);
    }

    if (store.isPlaying) {
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

  toggleShuffle() {
    store.shuffle = !store.shuffle;
  }

  toggleRepeat() {
    const modes = ['off', 'all', 'one'];
    const idx = modes.indexOf(store.repeat);
    store.repeat = modes[(idx + 1) % 3];
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

    if (store.repeat === 'one') {
      this.audio.currentTime = 0;
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
