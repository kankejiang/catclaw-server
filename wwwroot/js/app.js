/**
 * CatClaw Music Server — Main App
 * Vue 3 SPA with all views and components.
 */
import { createApp, ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { createRouter, createWebHashHistory } from 'vue-router';
import { store, login, logout, loadUser, loadOverview, playSong, addToQueue, toggleFavorite, isFavorite, flushScrobble, showToast, formatTime, formatDuration } from './store.js';
import { api } from './api.js';
import { player } from './player.js';

// ────────────────────────────────────────
// Components
// ────────────────────────────────────────

const AppSidebar = {
  name: 'AppSidebar',
  setup() {
    return { store };
  },
  template: `
    <aside class="sidebar">
      <div class="sidebar-header">
        <router-link to="/" class="sidebar-logo">
          <span class="sidebar-logo-icon">🐱</span>
          <span class="sidebar-logo-text">Cat<span>Claw</span></span>
        </router-link>
      </div>
      <nav class="sidebar-nav">
        <div class="nav-section">
          <div class="nav-section-title">发现</div>
          <router-link to="/" class="nav-item" active-class="active">
            <span class="nav-icon">🏠</span> 首页
          </router-link>
          <router-link to="/search" class="nav-item" active-class="active">
            <span class="nav-icon">🔍</span> 搜索
          </router-link>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">音乐库</div>
          <router-link to="/songs" class="nav-item" active-class="active">
            <span class="nav-icon">🎵</span> 歌曲
          </router-link>
          <router-link to="/artists" class="nav-item" active-class="active">
            <span class="nav-icon">🎤</span> 艺术家
          </router-link>
          <router-link to="/albums" class="nav-item" active-class="active">
            <span class="nav-icon">💿</span> 专辑
          </router-link>
          <router-link to="/playlists" class="nav-item" active-class="active">
            <span class="nav-icon">📋</span> 播放列表
          </router-link>
          <router-link to="/genres" class="nav-item" active-class="active">
            <span class="nav-icon">🎼</span> 流派
          </router-link>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">个人</div>
          <router-link to="/favorites" class="nav-item" active-class="active">
            <span class="nav-icon">❤️</span> 收藏
          </router-link>
          <router-link to="/history" class="nav-item" active-class="active">
            <span class="nav-icon">🕐</span> 历史
          </router-link>
          <router-link to="/stats" class="nav-item" active-class="active">
            <span class="nav-icon">📊</span> 统计
          </router-link>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">猫爪驿站</div>
          <router-link to="/p2p" class="nav-item" active-class="active">
            <span class="nav-icon">🐾</span> P2P 网络
          </router-link>
        </div>
        <div class="nav-section" v-if="store.user?.role === 'admin'">
          <div class="nav-section-title">管理</div>
          <router-link to="/admin" class="nav-item" active-class="active">
            <span class="nav-icon">⚙️</span> 管理面板
          </router-link>
        </div>
      </nav>
      <div class="sidebar-footer">
        <div class="sidebar-user" @click="$router.push('/settings')">
          <div class="sidebar-user-avatar">{{ store.user?.displayName?.[0] || store.user?.username?.[0] || '?' }}</div>
          <div class="sidebar-user-info">
            <div class="sidebar-user-name">{{ store.user?.displayName || store.user?.username }}</div>
            <div class="sidebar-user-role">{{ store.user?.role === 'admin' ? '管理员' : '用户' }}</div>
          </div>
        </div>
      </div>
    </aside>
  `
};

const PlayerBar = {
  name: 'PlayerBar',
  setup() {
    const isDragging = ref(false);

    function onProgressClick(e) {
      const rect = e.currentTarget.getBoundingClientRect();
      const fraction = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      player.seek(fraction);
    }

    function onProgressMouseDown(e) {
      isDragging.value = true;
      onProgressClick(e);
      const onMove = (ev) => onProgressClick(ev);
      const onUp = () => {
        isDragging.value = false;
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', onUp);
      };
      document.addEventListener('mousemove', onMove);
      document.addEventListener('mouseup', onUp);
    }

    // 播放模式图标与标题（顺序播放 → 单曲循环 → 随机播放）
    const playModeIcon = computed(() => {
      if (store.shuffle) return '🔀';
      if (store.repeat === 'one') return '🔂';
      return '➡️';
    });
    const playModeTitle = computed(() => {
      if (store.shuffle) return '随机播放（点击切换：顺序播放）';
      if (store.repeat === 'one') return '单曲循环（点击切换：随机播放）';
      return '顺序播放（点击切换：单曲循环）';
    });

    return { store, player, formatTime, onProgressClick, onProgressMouseDown, isFavorite, toggleFavorite, playModeIcon, playModeTitle, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="player-bar">
      <div class="player-cover" @click="store.nowPlayingFull = true">
        <img v-if="store.currentSong?.album_cover" :src="coverUrl(store.currentSong.id)" />
        <div v-else class="player-cover-placeholder">🎵</div>
      </div>
      <div class="player-info">
        <div class="player-title">{{ store.currentSong?.title || '未在播放' }}</div>
        <div class="player-artist">{{ store.currentSong?.artist || '' }}</div>
      </div>
      <div class="player-controls">
        <button class="player-btn" :class="{ active: store.currentSong && isFavorite(store.currentSong.id) }"
          :disabled="!store.currentSong"
          @click="store.currentSong && toggleFavorite(store.currentSong.id)"
          :title="store.currentSong ? (isFavorite(store.currentSong.id) ? '取消收藏' : '收藏') : '收藏'">
          {{ (store.currentSong && isFavorite(store.currentSong.id)) ? '❤️' : '🤍' }}
        </button>
        <button class="player-btn" @click="player.prev()" title="上一首">⏮</button>
        <button class="player-btn play-pause" @click="player.toggle()">{{ store.isPlaying ? '⏸' : '▶' }}</button>
        <button class="player-btn" @click="player.next()" title="下一首">⏭</button>
        <button class="player-btn" :class="{ active: store.shuffle || store.repeat !== 'off' }" @click="player.togglePlayMode()" :title="playModeTitle">
          {{ playModeIcon }}
        </button>
      </div>
      <div class="player-progress">
        <span class="player-time left">{{ formatTime(store.currentTime) }}</span>
        <div class="progress-bar" @mousedown="onProgressMouseDown">
          <div class="progress-bar-fill" :style="{ width: (store.progress * 100) + '%' }"></div>
        </div>
        <span class="player-time">{{ formatTime(store.duration) }}</span>
      </div>
      <div class="player-extra">
        <button class="btn-icon" :class="{ active: store.showLyrics }" @click="store.showLyrics = !store.showLyrics" title="歌词">📝</button>
        <button class="btn-icon" @click="player.toggleStreamMode()" :title="store.streamMode === 'hls' ? 'HLS模式' : '直连模式'">
          <span class="player-quality">{{ store.streamMode === 'hls' ? 'HLS' : 'RAW' }}</span>
        </button>
        <div class="player-volume">
          <button class="btn-icon" @click="player.toggleMute()">{{ store.isMuted || store.volume === 0 ? '🔇' : store.volume < 0.5 ? '🔉' : '🔊' }}</button>
          <input type="range" class="volume-slider" min="0" max="1" step="0.01" :value="store.volume" @input="player.setVolume(parseFloat($event.target.value))" />
        </div>
      </div>
    </div>
  `
};

const LyricsPanel = {
  name: 'LyricsPanel',
  setup() {
    const lyricsBody = ref(null);

    watch(() => store.currentLyricLine, (line) => {
      if (line >= 0 && lyricsBody.value) {
        const el = lyricsBody.value.querySelector('.lyrics-line.active');
        if (el) {
          el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
      }
    });

    // Update current lyric line based on playback time
    watch(() => store.currentTime, (time) => {
      if (!store.lyrics?.lines?.length) return;
      const timeMs = time * 1000;
      let currentLine = -1;
      for (let i = store.lyrics.lines.length - 1; i >= 0; i--) {
        if (timeMs >= store.lyrics.lines[i].startTimeMs) {
          currentLine = i;
          break;
        }
      }
      store.currentLyricLine = currentLine;
    });

    function seekToLine(line) {
      if (line.startTimeMs >= 0) {
        player.seek(line.startTimeMs / (store.duration * 1000));
      }
    }

    return { store, seekToLine, lyricsBody };
  },
  template: `
    <div class="lyrics-panel" :class="{ open: store.showLyrics }">
      <div class="lyrics-header">
        <h3>歌词</h3>
        <button class="btn-icon" @click="store.showLyrics = false">✕</button>
      </div>
      <div class="lyrics-body" ref="lyricsBody">
        <div v-if="!store.lyrics?.lines?.length" class="lyrics-empty">
          暂无歌词
        </div>
        <div v-else>
          <div v-for="(line, i) in store.lyrics.lines" :key="i"
               class="lyrics-line"
               :class="{ active: i === store.currentLyricLine, past: i < store.currentLyricLine }"
               @click="seekToLine(line)">
            {{ line.text }}
          </div>
        </div>
      </div>
    </div>
  `
};

const ToastContainer = {
  name: 'ToastContainer',
  setup() { return { store }; },
  template: `
    <div class="toast-container">
      <div v-for="toast in store.toasts" :key="toast.id" class="toast" :class="toast.type">
        {{ toast.message }}
      </div>
    </div>
  `
};

// ────────────────────────────────────────
// Views
// ────────────────────────────────────────

const HomeView = {
  name: 'HomeView',
  setup() {
    const overview = ref(null);
    const recentSongs = ref([]);
    const recommend = ref([]);

    onMounted(async () => {
      try {
        overview.value = await api.getOverview();
        const recentData = await api.getRecentlyPlayed(10);
        recentSongs.value = recentData.items || recentData || [];
      } catch {}
      try {
        const data = await api.getDailyRecommend(10);
        recommend.value = data.items || data || [];
      } catch {}
    });

    function playAll(songs) {
      if (songs.length > 0) playSong(songs[0], songs, 0);
    }

    return { overview, recentSongs, recommend, playSong, playAll, formatTime, store, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">首页</h1>
        <p class="page-subtitle">欢迎回来</p>
      </div>

      <div v-if="overview" class="stats-grid" style="display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:24px;">
        <div class="stat-card" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:0.06em;">歌曲</div>
          <div style="font-size:24px;font-weight:700;margin-top:4px;">{{ overview.song_count }}</div>
        </div>
        <div class="stat-card" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:0.06em;">艺术家</div>
          <div style="font-size:24px;font-weight:700;margin-top:4px;">{{ overview.artist_count }}</div>
        </div>
        <div class="stat-card" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:0.06em;">专辑</div>
          <div style="font-size:24px;font-weight:700;margin-top:4px;">{{ overview.album_count }}</div>
        </div>
        <div class="stat-card" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;letter-spacing:0.06em;">播放列表</div>
          <div style="font-size:24px;font-weight:700;margin-top:4px;">{{ overview.playlist_count }}</div>
        </div>
      </div>

      <div v-if="recommend.length" style="margin-bottom:32px;">
        <div class="section-header">
          <h2 class="section-title" style="font-size:18px;font-weight:700;">每日推荐</h2>
          <router-link to="/recommend" class="section-link" style="font-size:13px;color:var(--accent);">查看全部</router-link>
        </div>
        <div class="card-grid">
          <div class="card" v-for="song in recommend" :key="song.id" @click="playSong(song, recommend, recommend.indexOf(song))">
            <div class="card-cover">
              <img v-if="song.album_cover" :src="coverUrl(song.id)" />
              <div v-else class="card-cover-placeholder">🎵</div>
              <div class="card-cover-overlay"><div class="card-play-btn">▶</div></div>
            </div>
            <div class="card-body">
              <div class="card-title">{{ song.title }}</div>
              <div class="card-subtitle">{{ song.artist }}</div>
            </div>
          </div>
        </div>
      </div>

      <div v-if="recentSongs.length">
        <div class="section-header">
          <h2 class="section-title" style="font-size:18px;font-weight:700;">最近播放</h2>
        </div>
        <div class="card-grid">
          <div class="card" v-for="song in recentSongs" :key="song.id" @click="playSong(song, recentSongs, recentSongs.indexOf(song))">
            <div class="card-cover">
              <img v-if="song.album_cover" :src="coverUrl(song.id)" />
              <div v-else class="card-cover-placeholder">🎵</div>
              <div class="card-cover-overlay"><div class="card-play-btn">▶</div></div>
            </div>
            <div class="card-body">
              <div class="card-title">{{ song.title }}</div>
              <div class="card-subtitle">{{ song.artist }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

const SongsView = {
  name: 'SongsView',
  setup() {
    const songs = ref([]);
    const total = ref(0);
    const page = ref(1);
    const pageSize = ref(50);
    const loading = ref(false);
    const sortBy = ref('title');
    const sortOrder = ref('asc');

    async function loadSongs() {
      loading.value = true;
      try {
        const data = await api.getSongs({ page: page.value, page_size: pageSize.value, sort: sortBy.value, order: sortOrder.value });
        songs.value = data.items || [];
        total.value = data.total || 0;
      } catch (e) { showToast('加载失败: ' + e.message, 'error'); }
      loading.value = false;
    }

    function onPage(p) { page.value = p; loadSongs(); }
    function onSort(field) {
      if (sortBy.value === field) sortOrder.value = sortOrder.value === 'asc' ? 'desc' : 'asc';
      else { sortBy.value = field; sortOrder.value = 'asc'; }
      loadSongs();
    }

    const totalPages = computed(() => Math.ceil(total.value / pageSize.value));

    onMounted(loadSongs);

    return { songs, total, page, pageSize, totalPages, loading, sortBy, sortOrder, onPage, onSort, playSong, formatTime, store, isFavorite, toggleFavorite };
  },
  template: `
    <div class="page-content">
      <div class="page-header" style="display:flex;justify-content:space-between;align-items:center;">
        <div>
          <h1 class="page-title">歌曲</h1>
          <p class="page-subtitle">共 {{ total }} 首</p>
        </div>
      </div>
      <div class="song-list">
        <div class="song-list-header">
          <span>#</span>
          <span style="cursor:pointer" @click="onSort('title')">标题 {{ sortBy === 'title' ? (sortOrder === 'asc' ? '↑' : '↓') : '' }}</span>
          <span>艺术家</span>
          <span>专辑</span>
          <span style="cursor:pointer;text-align:right" @click="onSort('duration')">时长 {{ sortBy === 'duration' ? (sortOrder === 'asc' ? '↑' : '↓') : '' }}</span>
          <span></span>
        </div>
        <div v-for="(song, i) in songs" :key="song.id" class="song-row" :class="{ playing: store.currentSong?.id === song.id }" @click="playSong(song, songs, i)">
          <span class="song-idx">{{ (page - 1) * pageSize + i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ song.title }}</div>
            <div class="song-artist-sub">{{ song.artist }}</div>
          </div>
          <span class="song-album">{{ song.album }}</span>
          <span class="song-genre">{{ song.genre }}</span>
          <span class="song-duration">{{ formatTime(song.duration) }}</span>
          <div class="song-actions">
            <button class="btn-icon" style="font-size:14px;" @click.stop="toggleFavorite(song.id)" :class="{ active: isFavorite(song.id) }">
              {{ isFavorite(song.id) ? '❤️' : '🤍' }}
            </button>
          </div>
        </div>
      </div>
      <div class="pagination" v-if="totalPages > 1">
        <button :disabled="page <= 1" @click="onPage(page - 1)">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button :disabled="page >= totalPages" @click="onPage(page + 1)">下一页</button>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
    </div>
  `
};

const ArtistsView = {
  name: 'ArtistsView',
  setup() {
    const artists = ref([]);
    const total = ref(0);
    const page = ref(1);
    const loading = ref(false);
    const pageSize = 48;

    async function load() {
      loading.value = true;
      try {
        const data = await api.getArtists({ page: page.value, page_size: pageSize });
        artists.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    }

    const totalPages = computed(() => Math.ceil(total.value / pageSize));
    onMounted(load);

    return { artists, total, page, totalPages, loading, artistCoverUrl: (id) => api.getArtistCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">艺术家</h1>
        <p class="page-subtitle">共 {{ total }} 位</p>
      </div>
      <div class="card-grid">
        <router-link v-for="a in artists" :key="a.id" :to="'/artists/' + a.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover">
            <img v-if="a.cover" :src="artistCoverUrl(a.id)" />
            <div v-else class="card-cover-placeholder">🎤</div>
          </div>
          <div class="card-body">
            <div class="card-title">{{ a.name }}</div>
            <div class="card-subtitle">{{ a.song_count || 0 }} 首歌曲</div>
          </div>
        </router-link>
      </div>
      <div class="pagination" v-if="totalPages > 1">
        <button :disabled="page <= 1" @click="page--; $nextTick(() => $root.$children[0])">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button :disabled="page >= totalPages" @click="page++">下一页</button>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
    </div>
  `
};

const AlbumsView = {
  name: 'AlbumsView',
  setup() {
    const albums = ref([]);
    const total = ref(0);
    const page = ref(1);
    const loading = ref(false);
    const pageSize = 48;

    async function load() {
      loading.value = true;
      try {
        const data = await api.getAlbums({ page: page.value, page_size: pageSize });
        albums.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    }

    const totalPages = computed(() => Math.ceil(total.value / pageSize));
    onMounted(load);

    return { albums, total, page, totalPages, loading, albumCoverUrl: (id) => api.getAlbumCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">专辑</h1>
        <p class="page-subtitle">共 {{ total }} 张</p>
      </div>
      <div class="card-grid">
        <router-link v-for="a in albums" :key="a.id" :to="'/albums/' + a.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover">
            <img v-if="a.cover" :src="albumCoverUrl(a.id)" />
            <div v-else class="card-cover-placeholder">💿</div>
          </div>
          <div class="card-body">
            <div class="card-title">{{ a.title }}</div>
            <div class="card-subtitle">{{ a.artist }} · {{ a.song_count || 0 }} 首</div>
          </div>
        </router-link>
      </div>
      <div class="pagination" v-if="totalPages > 1">
        <button :disabled="page <= 1" @click="page--">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button :disabled="page >= totalPages" @click="page++">下一页</button>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
    </div>
  `
};

const AlbumDetailView = {
  name: 'AlbumDetailView',
  setup() {
    const route = computed(() => null); // placeholder
    const album = ref(null);
    const loading = ref(true);

    onMounted(async () => {
      const id = window.location.hash.split('/').pop();
      try {
        album.value = await api.getAlbum(id);
      } catch {}
      loading.value = false;
    });

    return { album, loading, playSong, formatTime, store, albumCoverUrl: (id) => api.getAlbumCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="album">
        <div style="display:flex;gap:24px;margin-bottom:24px;">
          <div style="width:200px;height:200px;border-radius:12px;overflow:hidden;background:var(--bg-tertiary);flex-shrink:0;">
            <img v-if="album.cover" :src="albumCoverUrl(album.id)" style="width:100%;height:100%;object-fit:cover;" />
            <div v-else style="display:flex;align-items:center;justify-content:center;height:100%;font-size:64px;opacity:0.3;">💿</div>
          </div>
          <div>
            <p style="font-size:12px;color:var(--text-muted);text-transform:uppercase;">专辑</p>
            <h1 class="page-title">{{ album.title }}</h1>
            <p class="page-subtitle">{{ album.artist }} · {{ album.songs?.length || 0 }} 首歌曲</p>
            <button class="btn btn-primary" style="margin-top:16px;" @click="playSong(album.songs[0], album.songs, 0)" v-if="album.songs?.length">
              ▶ 播放全部
            </button>
          </div>
        </div>
        <div class="song-list">
          <div v-for="(song, i) in album.songs" :key="song.id" class="song-row" :class="{ playing: store.currentSong?.id === song.id }" @click="playSong(song, album.songs, i)">
            <span class="song-idx">{{ song.track_number || i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
          </div>
        </div>
      </div>
    </div>
  `
};

const ArtistDetailView = {
  name: 'ArtistDetailView',
  setup() {
    const artist = ref(null);
    const songs = ref([]);
    const albums = ref([]);
    const loading = ref(true);

    onMounted(async () => {
      const id = window.location.hash.split('/').pop();
      try {
        const [a, s, al] = await Promise.all([
          api.getArtist(id),
          api.getArtistSongs(id, { page: 1, page_size: 200 }),
          api.getArtistAlbums(id)
        ]);
        artist.value = a;
        songs.value = s.items || [];
        albums.value = al.items || [];
      } catch {}
      loading.value = false;
    });

    return { artist, songs, albums, loading, playSong, formatTime, store, artistCoverUrl: (id) => api.getArtistCoverUrl(id), albumCoverUrl: (id) => api.getAlbumCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="artist">
        <div style="display:flex;gap:24px;margin-bottom:24px;align-items:center;">
          <div style="width:160px;height:160px;border-radius:50%;overflow:hidden;background:var(--bg-tertiary);flex-shrink:0;">
            <img v-if="artist.cover" :src="artistCoverUrl(artist.id)" style="width:100%;height:100%;object-fit:cover;" />
            <div v-else style="display:flex;align-items:center;justify-content:center;height:100%;font-size:56px;opacity:0.3;">🎤</div>
          </div>
          <div>
            <p style="font-size:12px;color:var(--text-muted);text-transform:uppercase;">艺术家</p>
            <h1 class="page-title">{{ artist.name }}</h1>
            <p class="page-subtitle">{{ artist.song_count || 0 }} 首歌曲 · {{ artist.album_count || 0 }} 张专辑</p>
            <p v-if="artist.description" style="color:var(--text-secondary);font-size:13px;margin-top:8px;max-width:500px;">{{ artist.description }}</p>
          </div>
        </div>

        <div v-if="albums.length" style="margin-bottom:24px;">
          <h2 style="font-size:18px;font-weight:700;margin-bottom:12px;">专辑</h2>
          <div class="card-grid">
            <router-link v-for="a in albums" :key="a.id" :to="'/albums/' + a.id" class="card" style="text-decoration:none;color:inherit;">
              <div class="card-cover">
                <img v-if="a.cover" :src="albumCoverUrl(a.id)" />
                <div v-else class="card-cover-placeholder">💿</div>
              </div>
              <div class="card-body">
                <div class="card-title">{{ a.title }}</div>
                <div class="card-subtitle">{{ a.song_count || 0 }} 首</div>
              </div>
            </router-link>
          </div>
        </div>

        <h2 style="font-size:18px;font-weight:700;margin-bottom:12px;">全部歌曲</h2>
        <div class="song-list">
          <div v-for="(song, i) in songs" :key="song.id" class="song-row" :class="{ playing: store.currentSong?.id === song.id }" @click="playSong(song, songs, i)">
            <span class="song-idx">{{ i + 1 }}</span>
            <div class="song-info"><div class="song-title">{{ song.title }}</div></div>
            <span class="song-album">{{ song.album }}</span>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
          </div>
        </div>
      </div>
    </div>
  `
};

const SearchView = {
  name: 'SearchView',
  setup() {
    const query = ref('');
    const results = ref({ songs: [], artists: [], albums: [] });
    const loading = ref(false);
    let debounceTimer = null;

    async function doSearch() {
      if (!query.value.trim()) { results.value = { songs: [], artists: [], albums: [] }; return; }
      loading.value = true;
      try {
        results.value = await api.search(query.value.trim());
      } catch {}
      loading.value = false;
    }

    function onInput(e) {
      query.value = e.target.value;
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(doSearch, 300);
    }

    return { query, results, loading, onInput, playSong, formatTime, store };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">搜索</h1>
      </div>
      <div style="margin-bottom:24px;">
        <input type="text" :value="query" @input="onInput" placeholder="搜索歌曲、艺术家、专辑..."
               style="width:100%;max-width:500px;padding:12px 16px;border-radius:24px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:15px;outline:none;" />
      </div>

      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>

      <div v-if="results.artists?.length">
        <h2 style="font-size:16px;font-weight:600;margin-bottom:12px;">艺术家</h2>
        <div class="card-grid" style="margin-bottom:24px;">
          <router-link v-for="a in results.artists" :key="a.id" :to="'/artists/' + a.id" class="card" style="text-decoration:none;color:inherit;">
            <div class="card-cover"><div class="card-cover-placeholder">🎤</div></div>
            <div class="card-body"><div class="card-title">{{ a.name }}</div></div>
          </router-link>
        </div>
      </div>

      <div v-if="results.albums?.length">
        <h2 style="font-size:16px;font-weight:600;margin-bottom:12px;">专辑</h2>
        <div class="card-grid" style="margin-bottom:24px;">
          <router-link v-for="a in results.albums" :key="a.id" :to="'/albums/' + a.id" class="card" style="text-decoration:none;color:inherit;">
            <div class="card-cover"><div class="card-cover-placeholder">💿</div></div>
            <div class="card-body"><div class="card-title">{{ a.title }}</div><div class="card-subtitle">{{ a.artist }}</div></div>
          </router-link>
        </div>
      </div>

      <div v-if="results.songs?.length">
        <h2 style="font-size:16px;font-weight:600;margin-bottom:12px;">歌曲</h2>
        <div class="song-list">
          <div v-for="(song, i) in results.songs" :key="song.id" class="song-row" @click="playSong(song, results.songs, i)">
            <span class="song-idx">{{ i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-album">{{ song.album }}</span>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
          </div>
        </div>
      </div>
    </div>
  `
};

const PlaylistsView = {
  name: 'PlaylistsView',
  setup() {
    const playlists = ref([]);
    const showCreate = ref(false);
    const newName = ref('');
    const newDesc = ref('');

    async function load() {
      try {
        const data = await api.getPlaylists({ page: 1, page_size: 100 });
        playlists.value = data.items || [];
      } catch {}
    }

    async function create() {
      if (!newName.value.trim()) return;
      try {
        await api.createPlaylist({ name: newName.value, description: newDesc.value });
        showCreate.value = false;
        newName.value = '';
        newDesc.value = '';
        await load();
        showToast('播放列表已创建', 'success');
      } catch (e) { showToast('创建失败: ' + e.message, 'error'); }
    }

    onMounted(load);

    return { playlists, showCreate, newName, newDesc, create };
  },
  template: `
    <div class="page-content">
      <div class="page-header" style="display:flex;justify-content:space-between;align-items:center;">
        <div>
          <h1 class="page-title">播放列表</h1>
          <p class="page-subtitle">{{ playlists.length }} 个列表</p>
        </div>
        <button class="btn btn-primary" @click="showCreate = true">+ 新建</button>
      </div>

      <div class="card-grid">
        <router-link v-for="p in playlists" :key="p.id" :to="'/playlists/' + p.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover"><div class="card-cover-placeholder">📋</div></div>
          <div class="card-body">
            <div class="card-title">{{ p.name }}</div>
            <div class="card-subtitle">{{ p.song_count || 0 }} 首歌曲</div>
          </div>
        </router-link>
      </div>

      <div v-if="showCreate" class="modal-overlay" @click.self="showCreate = false">
        <div class="modal">
          <h3>新建播放列表</h3>
          <div class="form-group">
            <label>名称</label>
            <input type="text" v-model="newName" placeholder="播放列表名称" />
          </div>
          <div class="form-group">
            <label>描述（可选）</label>
            <textarea v-model="newDesc" placeholder="描述..."></textarea>
          </div>
          <div class="modal-actions">
            <button class="btn btn-secondary" @click="showCreate = false">取消</button>
            <button class="btn btn-primary" @click="create">创建</button>
          </div>
        </div>
      </div>
    </div>
  `
};

const PlaylistDetailView = {
  name: 'PlaylistDetailView',
  setup() {
    const playlist = ref(null);
    const loading = ref(true);

    async function load() {
      const id = window.location.hash.split('/').pop();
      try {
        playlist.value = await api.getPlaylist(id);
      } catch {}
      loading.value = false;
    }

    async function removeSong(songId) {
      const id = window.location.hash.split('/').pop();
      try {
        await api.removeSongFromPlaylist(id, songId);
        await load();
      } catch {}
    }

    onMounted(load);

    return { playlist, loading, playSong, formatTime, removeSong, store };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="playlist">
        <div class="page-header">
          <h1 class="page-title">{{ playlist.name }}</h1>
          <p class="page-subtitle">{{ playlist.songs?.length || 0 }} 首歌曲</p>
        </div>
        <button class="btn btn-primary" style="margin-bottom:16px;" @click="playSong(playlist.songs[0], playlist.songs, 0)" v-if="playlist.songs?.length">
          ▶ 播放全部
        </button>
        <div class="song-list">
          <div v-for="(song, i) in playlist.songs" :key="song.id" class="song-row" @click="playSong(song, playlist.songs, i)">
            <span class="song-idx">{{ i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
            <div class="song-actions">
              <button class="btn-icon" @click.stop="removeSong(song.id)" title="移除" style="color:var(--danger);">✕</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

const FavoritesView = {
  name: 'FavoritesView',
  setup() {
    const songs = ref([]);
    const loading = ref(true);

    async function load() {
      try {
        const data = await api.getFavorites({ page: 1, page_size: 200 });
        songs.value = data.items || [];
      } catch {}
      loading.value = false;
    }

    onMounted(load);

    return { songs, loading, playSong, formatTime, store, toggleFavorite };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">收藏</h1>
        <p class="page-subtitle">{{ songs.length }} 首收藏</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!songs.length" class="empty-state">
        <div class="empty-state-icon">❤️</div>
        <p class="empty-state-text">还没有收藏</p>
        <p class="empty-state-hint">在播放时点击 ❤️ 来收藏歌曲</p>
      </div>
      <div v-else class="song-list">
        <div v-for="(song, i) in songs" :key="song.id" class="song-row" @click="playSong(song, songs, i)">
          <span class="song-idx">{{ i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ song.title }}</div>
            <div class="song-artist-sub">{{ song.artist }}</div>
          </div>
          <span class="song-album">{{ song.album }}</span>
          <span class="song-duration">{{ formatTime(song.duration) }}</span>
          <div class="song-actions">
            <button class="btn-icon" @click.stop="toggleFavorite(song.id)" style="color:var(--danger);">❤️</button>
          </div>
        </div>
      </div>
    </div>
  `
};

const HistoryView = {
  name: 'HistoryView',
  setup() {
    const items = ref([]);
    const loading = ref(true);

    async function load() {
      try {
        const data = await api.getHistory({ page: 1, page_size: 100 });
        items.value = data.items || [];
      } catch {}
      loading.value = false;
    }

    onMounted(load);

    return { items, loading, playSong, formatTime, store };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">播放历史</h1>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!items.length" class="empty-state">
        <div class="empty-state-icon">🕐</div>
        <p class="empty-state-text">暂无播放记录</p>
      </div>
      <div v-else class="song-list">
        <div v-for="(item, i) in items" :key="i" class="song-row">
          <span class="song-idx">{{ i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ item.title }}</div>
            <div class="song-artist-sub">{{ item.artist }}</div>
          </div>
          <span class="song-duration">{{ formatTime(item.duration_played_ms / 1000) }}</span>
        </div>
      </div>
    </div>
  `
};

const StatsView = {
  name: 'StatsView',
  setup() {
    const stats = ref(null);
    const loading = ref(true);

    onMounted(async () => {
      try {
        stats.value = await api.getStats(30);
      } catch {}
      loading.value = false;
    });

    return { stats, loading };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">播放统计</h1>
        <p class="page-subtitle">最近 30 天</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="stats">
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin-bottom:24px;">
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">总播放次数</div>
            <div style="font-size:28px;font-weight:700;color:var(--accent);">{{ stats.totalPlays }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">总收听时长</div>
            <div style="font-size:28px;font-weight:700;">{{ stats.totalDurationHours }}h</div>
          </div>
        </div>

        <div v-if="stats.topSongs?.length" style="margin-bottom:24px;">
          <h2 style="font-size:16px;font-weight:600;margin-bottom:12px;">最常播放的歌曲</h2>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div v-for="(s, i) in stats.topSongs" :key="s.songId" style="display:flex;align-items:center;gap:12px;padding:6px 0;border-bottom:1px solid var(--border);">
              <span style="color:var(--text-muted);width:24px;text-align:center;">{{ i + 1 }}</span>
              <span style="flex:1;">{{ s.title }}</span>
              <span style="color:var(--text-muted);font-size:12px;">{{ s.artistName }}</span>
              <span style="color:var(--accent);font-weight:600;">{{ s.playCount }}</span>
            </div>
          </div>
        </div>

        <div v-if="stats.topArtists?.length">
          <h2 style="font-size:16px;font-weight:600;margin-bottom:12px;">最常听的艺术家</h2>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div v-for="(a, i) in stats.topArtists" :key="a.artistId" style="display:flex;align-items:center;gap:12px;padding:6px 0;border-bottom:1px solid var(--border);">
              <span style="color:var(--text-muted);width:24px;text-align:center;">{{ i + 1 }}</span>
              <span style="flex:1;">{{ a.artistName }}</span>
              <span style="color:var(--accent);font-weight:600;">{{ a.playCount }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

const AdminView = {
  name: 'AdminView',
  setup() {
    const serverStats = ref(null);
    const scanStatus = ref(null);
    const scanning = ref(false);
    const loading = ref(true);

    async function load() {
      try {
        const [stats, scan] = await Promise.all([
          api.getServerStats().catch(() => null),
          api.getScanStatus().catch(() => null)
        ]);
        serverStats.value = stats;
        scanStatus.value = scan;
      } catch {}
      loading.value = false;
    }

    async function triggerScan() {
      scanning.value = true;
      try { await api.triggerScan(); } catch {}
      // Poll status
      const interval = setInterval(async () => {
        try {
          scanStatus.value = await api.getScanStatus();
          if (!scanStatus.value?.scanning) {
            clearInterval(interval);
            scanning.value = false;
            await load();
          }
        } catch { clearInterval(interval); scanning.value = false; }
      }, 2000);
    }

    onMounted(load);

    return { serverStats, scanStatus, scanning, loading, triggerScan };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">管理面板</h1>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else>
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">音乐库</h3>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:12px;">
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">歌曲</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.songCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">艺术家</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.artistCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">专辑</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.albumCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">用户</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.userCount }}</div>
            </div>
          </div>
        </div>

        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">扫描</h3>
          <div style="display:flex;gap:12px;align-items:center;">
            <button class="btn btn-primary" :disabled="scanning" @click="triggerScan">
              {{ scanning ? '扫描中...' : '开始扫描' }}
            </button>
            <button class="btn btn-secondary" :disabled="scanning" @click="triggerScan">增量扫描</button>
          </div>
          <div v-if="scanStatus" style="margin-top:12px;">
            <div style="font-size:13px;color:var(--text-secondary);">
              状态: {{ scanStatus.scanning ? '扫描中...' : '空闲' }}
              <span v-if="scanStatus.total"> | 已处理: {{ scanStatus.processed }} / {{ scanStatus.total }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

const SettingsView = {
  name: 'SettingsView',
  setup() {
    async function doLogout() {
      await logout();
    }
    return { store, doLogout };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">设置</h1>
      </div>
      <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
        <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;">账户</h3>
        <div style="display:flex;align-items:center;gap:16px;margin-bottom:16px;">
          <div style="width:48px;height:48px;border-radius:50%;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:20px;font-weight:700;">
            {{ store.user?.displayName?.[0] || store.user?.username?.[0] || '?' }}
          </div>
          <div>
            <div style="font-weight:600;">{{ store.user?.displayName || store.user?.username }}</div>
            <div style="font-size:12px;color:var(--text-muted);">{{ store.user?.role === 'admin' ? '管理员' : '普通用户' }}</div>
          </div>
        </div>
        <button class="btn btn-danger" @click="doLogout">退出登录</button>
      </div>
    </div>
  `
};

// ── P2P / ClawCircle View ──
const P2PView = {
  name: 'P2PView',
  setup() {
    const stats = ref(null);
    const peers = ref([]);
    const dhtNodes = ref([]);
    const reputation = ref([]);
    const loading = ref(true);
    const activeTab = ref('dht');  // 默认打开 DHT 引导页
    const dhtEnabled = ref(false);
    const bootstrapAddr = ref('');
    const toggling = ref(false);

    // 找歌 + 传输管理
    const transfers = ref([]);
    const searchArtist = ref('');
    const searchTitle = ref('');
    const searchResults = ref(null);
    const searching = ref(false);
    const offerSongId = ref('');
    const offerPeerId = ref('');
    const offering = ref(false);
    // 区块链积分账本
    const balances = ref([]);
    const chainBlocks = ref([]);
    const chainHeight = ref(0);
    const ledgerSize = ref(0);
    const prunedToIndex = ref(0);
    const historyDeviceId = ref('');
    const history = ref(null);
    let pollTimer = null;

    async function load() {
      loading.value = true;
      try {
        const [s, p, d, r, t, b, c] = await Promise.all([
          api.getP2PStats().catch(() => null),
          api.getP2PPeers().catch(() => ({ peers: [] })),
          api.getDhtNodes().catch(() => ({ nodes: [] })),
          api.getReputation().catch(() => []),
          api.getTransfers().catch(() => ({ transfers: [] })),
          api.getAllBalances().catch(() => ({ balances: [] })),
          api.getChain(0, 10).catch(() => ({ blocks: [], height: 0 }))
        ]);
        stats.value = s;
        dhtEnabled.value = s?.dht?.enabled || false;
        peers.value = p?.peers || [];
        dhtNodes.value = d?.nodes || [];
        reputation.value = Array.isArray(r) ? r : [];
        transfers.value = t?.transfers || [];
        balances.value = b?.balances || [];
        chainBlocks.value = c?.blocks || [];
        chainHeight.value = c?.height || 0;
        ledgerSize.value = c?.sizeBytes || 0;
        prunedToIndex.value = c?.prunedToIndex || 0;
      } catch {}
      loading.value = false;
    }

    async function toggleDht() {
      toggling.value = true;
      try {
        const result = await api.toggleDht(!dhtEnabled.value);
        dhtEnabled.value = result.enabled;
        showToast(dhtEnabled.value ? 'DHT 已启用' : 'DHT 已关闭', 'success');
        await load();
      } catch (e) {
        showToast('操作失败: ' + e.message, 'error');
      }
      toggling.value = false;
    }

    async function doBootstrap() {
      if (!bootstrapAddr.value.trim()) return;
      try {
        await api.bootstrapDht(bootstrapAddr.value.trim());
        showToast('Bootstrap 已触发', 'success');
        bootstrapAddr.value = '';
        setTimeout(load, 2000);
      } catch (e) {
        showToast('Bootstrap 失败: ' + e.message, 'error');
      }
    }

    async function searchSong() {
      if (!searchArtist.value.trim() || !searchTitle.value.trim()) return;
      searching.value = true;
      try {
        searchResults.value = await api.findSong(searchArtist.value.trim(), searchTitle.value.trim());
      } catch (e) {
        showToast('搜索失败: ' + e.message, 'error');
      }
      searching.value = false;
    }

    async function doOffer() {
      if (!offerSongId.value.trim() || !offerPeerId.value) {
        showToast('请填写歌曲 ID 并选择目标节点', 'error');
        return;
      }
      offering.value = true;
      try {
        const r = await api.offerTransfer(offerSongId.value.trim(), offerPeerId.value);
        if (r && r.taskId) {
          showToast(`已发起分享，任务 ${r.taskId}`, 'success');
          offerSongId.value = '';
          offerPeerId.value = '';
          await loadTransfers();
          activeTab.value = 'transfers';
        } else {
          showToast('发起失败：节点离线或 STUN 未就绪', 'error');
        }
      } catch (e) {
        showToast('发起失败: ' + e.message, 'error');
      }
      offering.value = false;
    }

    async function loadTransfers() {
      try {
        const t = await api.getTransfers();
        transfers.value = t?.transfers || [];
      } catch {}
    }

    async function cancelTask(taskId) {
      try {
        await api.cancelTransfer(taskId);
        showToast('已取消任务', 'success');
        await loadTransfers();
      } catch (e) {
        showToast('取消失败: ' + e.message, 'error');
      }
    }

    function formatProgress(p) { return Math.round((p || 0) * 100) + '%'; }
    function formatSize(bytes) {
      if (!bytes) return '0 B';
      if (bytes < 1024) return bytes + ' B';
      if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
      return (bytes / 1048576).toFixed(1) + ' MB';
    }

    async function loadHistory() {
      if (!historyDeviceId.value.trim()) return;
      try {
        history.value = await api.getHistory(historyDeviceId.value.trim());
      } catch (e) {
        showToast('查询失败: ' + e.message, 'error');
      }
    }

    function formatTxType(t) {
      return t === 'upload' ? '⬆ 上传赚' : t === 'download' ? '⬇ 下载花' : t === 'reward' ? '🎁 在线奖励' : '⚠ 惩罚';
    }
    function formatTime(ms) {
      if (!ms) return '';
      return new Date(ms).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
    }

    // ── 账号体系 ──
    // clawToken 持久化在 localStorage（多设备 Token，每设备一份）
    const clawToken = ref(localStorage.getItem('clawToken') || '');
    const clawAccount = ref(null);     // 当前账号信息 { accountId, username, displayName, balance, ... }
    const clawDevices = ref([]);       // 设备列表
    const clawLoggingIn = ref(false);
    const clawRegistering = ref(false);
    // 登录表单
    const loginUsername = ref('');
    const loginPassword = ref('');
    // 注册表单
    const regUsername = ref('');
    const regPassword = ref('');
    const regPasswordConfirm = ref('');
    const regDisplayName = ref('');
    // 修改密码表单
    const oldPassword = ref('');
    const newPassword = ref('');
    const changingPwd = ref(false);
    // 账号视图模式：'login' | 'register'
    const authMode = ref('login');

    // 设备 ID（每浏览器一份，持久化）
    function getDeviceId() {
      let id = localStorage.getItem('clawDeviceId');
      if (!id) {
        id = 'web-' + Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
        localStorage.setItem('clawDeviceId', id);
      }
      return id;
    }
    function getDeviceName() {
      const ua = navigator.userAgent;
      let os = 'Web';
      if (/Windows/i.test(ua)) os = 'Windows';
      else if (/Mac/i.test(ua)) os = 'Mac';
      else if (/Android/i.test(ua)) os = 'Android';
      else if (/iPhone|iPad/i.test(ua)) os = 'iOS';
      else if (/Linux/i.test(ua)) os = 'Linux';
      return os + ' 浏览器';
    }

    async function loadClawMe() {
      if (!clawToken.value) return;
      try {
        const me = await api.getClawMe(clawToken.value);
        if (me && !me.error) {
          clawAccount.value = me;
          // 同步拉取设备列表
          await loadClawDevices();
        } else {
          // Token 失效
          clawLogout(false);
        }
      } catch {
        clawLogout(false);
      }
    }

    async function loadClawDevices() {
      if (!clawToken.value) return;
      try {
        const data = await api.listDevices(clawToken.value);
        if (data && data.devices) clawDevices.value = data.devices;
      } catch {}
    }

    async function clawLogin() {
      if (!loginUsername.value.trim() || !loginPassword.value) {
        showToast('请输入用户名和密码', 'error');
        return;
      }
      clawLoggingIn.value = true;
      try {
        const r = await api.loginAccount(
          loginUsername.value.trim(),
          loginPassword.value,
          getDeviceId(),
          getDeviceName()
        );
        if (r && r.token) {
          clawToken.value = r.token;
          localStorage.setItem('clawToken', r.token);
          clawAccount.value = r;
          loginUsername.value = '';
          loginPassword.value = '';
          showToast('登录成功，欢迎回来 ' + (r.displayName || r.username), 'success');
          await loadClawDevices();
        } else {
          showToast('登录失败：' + (r?.error || '未知错误'), 'error');
        }
      } catch (e) {
        showToast('登录失败: ' + e.message, 'error');
      }
      clawLoggingIn.value = false;
    }

    async function clawRegister() {
      if (!regUsername.value.trim() || !regPassword.value) {
        showToast('请输入用户名和密码', 'error');
        return;
      }
      if (regPassword.value.length < 6) {
        showToast('密码至少 6 位', 'error');
        return;
      }
      if (regPassword.value !== regPasswordConfirm.value) {
        showToast('两次输入的密码不一致', 'error');
        return;
      }
      clawRegistering.value = true;
      try {
        const r = await api.registerAccount(regUsername.value.trim(), regPassword.value, regDisplayName.value.trim());
        if (r && r.accountId) {
          showToast('注册成功，请登录', 'success');
          authMode.value = 'login';
          loginUsername.value = regUsername.value.trim();
          regUsername.value = '';
          regPassword.value = '';
          regPasswordConfirm.value = '';
          regDisplayName.value = '';
        } else {
          showToast('注册失败：' + (r?.error || '未知错误'), 'error');
        }
      } catch (e) {
        showToast('注册失败: ' + e.message, 'error');
      }
      clawRegistering.value = false;
    }

    function clawLogout(showMsg = true) {
      clawToken.value = '';
      clawAccount.value = null;
      clawDevices.value = [];
      localStorage.removeItem('clawToken');
      if (showMsg) showToast('已退出登录', 'success');
    }

    async function revokeClawDevice(deviceId) {
      if (!clawToken.value) return;
      if (!confirm('确定要退出此设备吗？如果是当前设备，将同时退出登录。')) return;
      try {
        const r = await api.revokeDevice(clawToken.value, deviceId);
        if (r && !r.error) {
          showToast('设备已退出', 'success');
          if (deviceId === getDeviceId()) {
            clawLogout(false);
          } else {
            await loadClawDevices();
          }
        } else {
          showToast('操作失败：' + (r?.error || '未知错误'), 'error');
        }
      } catch (e) {
        showToast('操作失败: ' + e.message, 'error');
      }
    }

    async function clawChangePassword() {
      if (!clawToken.value) return;
      if (!oldPassword.value || !newPassword.value) {
        showToast('请填写原密码和新密码', 'error');
        return;
      }
      if (newPassword.value.length < 6) {
        showToast('新密码至少 6 位', 'error');
        return;
      }
      changingPwd.value = true;
      try {
        const r = await api.changePassword(clawToken.value, oldPassword.value, newPassword.value);
        if (r && !r.error) {
          showToast('密码修改成功，请重新登录', 'success');
          oldPassword.value = '';
          newPassword.value = '';
          // 改密后所有 Token 失效，自动退出
          clawLogout(false);
        } else {
          showToast('修改失败：' + (r?.error || '原密码错误'), 'error');
        }
      } catch (e) {
        showToast('修改失败: ' + e.message, 'error');
      }
      changingPwd.value = false;
    }

    // 刷新当前账号余额（轮询用）
    async function refreshClawBalance() {
      if (!clawToken.value || !clawAccount.value) return;
      try {
        const me = await api.getClawMe(clawToken.value);
        if (me && !me.error && clawAccount.value) {
          clawAccount.value.balance = me.balance;
        }
      } catch {}
    }

    onMounted(() => {
      load();
      pollTimer = setInterval(loadTransfers, 2000);
      // 如果已登录，启动时加载账号信息
      if (clawToken.value) loadClawMe();
    });
    onUnmounted(() => { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } });

    return {
      stats, peers, dhtNodes, reputation, loading, activeTab,
      dhtEnabled, bootstrapAddr, toggling, toggleDht, doBootstrap,
      transfers, searchArtist, searchTitle, searchResults, searching,
      offerSongId, offerPeerId, offering, searchSong, doOffer, cancelTask,
      loadTransfers, formatProgress, formatSize,
      balances, chainBlocks, chainHeight, ledgerSize, prunedToIndex,
      historyDeviceId, history, loadHistory, formatTxType, formatTime,
      // 账号体系
      clawToken, clawAccount, clawDevices, clawLoggingIn, clawRegistering,
      loginUsername, loginPassword, regUsername, regPassword, regPasswordConfirm, regDisplayName,
      oldPassword, newPassword, changingPwd, authMode,
      clawLogin, clawRegister, clawLogout, revokeClawDevice, clawChangePassword,
      loadClawMe, loadClawDevices, refreshClawBalance, getDeviceId
    };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">🐾 猫爪驿站 P2P</h1>
        <p class="page-subtitle">跨节点音乐分享网络</p>
      </div>

      <div style="display:flex;gap:8px;margin-bottom:20px;flex-wrap:wrap;">
        <button class="btn" :class="activeTab === 'dht' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'dht'">🌐 DHT 网络 <span v-if="dhtEnabled" style="color:var(--success);">✓</span></button>
        <button class="btn" :class="activeTab === 'account' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'account'">👤 账号 <span v-if="clawToken" style="color:var(--success);">✓</span></button>
        <button class="btn" :class="activeTab === 'overview' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'overview'">概览</button>
        <button class="btn" :class="activeTab === 'peers' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'peers'" :disabled="!dhtEnabled">在线节点</button>
        <button class="btn" :class="activeTab === 'reputation' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'reputation'" :disabled="!dhtEnabled">节点信誉</button>
        <button class="btn" :class="activeTab === 'search' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'search'" :disabled="!dhtEnabled">🔍 找歌</button>
        <button class="btn" :class="activeTab === 'transfers' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'transfers'" :disabled="!dhtEnabled">📦 传输 <span v-if="transfers.length" style="margin-left:4px;opacity:.8;">({{ transfers.length }})</span></button>
        <button class="btn" :class="activeTab === 'ledger' ? 'btn-primary' : 'btn-secondary'" @click="activeTab = 'ledger'" :disabled="!dhtEnabled">🐟 小鱼干</button>
      </div>

      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>

      <!-- 概览 -->
      <div v-if="!loading && activeTab === 'overview'">
        <!-- DHT 未开启醒目提示 -->
        <div v-if="!dhtEnabled" style="background:var(--accent-dim);border:1px solid var(--accent);border-radius:10px;padding:16px;margin-bottom:16px;display:flex;align-items:center;gap:12px;">
          <span style="font-size:24px;">🌐</span>
          <div style="flex:1;">
            <div style="font-size:14px;font-weight:600;color:var(--accent);">DHT 未启用</div>
            <div style="font-size:12px;color:var(--text-muted);margin-top:2px;">猫爪驿站的核心功能依赖 DHT 网络，请先启用</div>
          </div>
          <button class="btn btn-primary" @click="activeTab = 'dht'">前往启用</button>
        </div>
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px;margin-bottom:24px;">
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">WebSocket 节点</div>
            <div style="font-size:28px;font-weight:700;color:var(--accent);">{{ stats?.tracker?.onlinePeers || 0 }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">DHT 状态</div>
            <div style="font-size:28px;font-weight:700;">{{ stats?.dht?.enabled ? '已启用' : '未启用' }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">DHT 节点</div>
            <div style="font-size:28px;font-weight:700;">{{ stats?.dht?.nodeCount || 0 }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">DHT 存储</div>
            <div style="font-size:28px;font-weight:700;">{{ stats?.dht?.storeCount || 0 }}</div>
          </div>
        </div>
        <div v-if="stats?.dht?.localId" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div style="font-size:12px;color:var(--text-muted);margin-bottom:4px;">本节点 ID</div>
          <code style="font-size:13px;color:var(--accent);word-break:break-all;">{{ stats.dht.localId }}</code>
        </div>
      </div>

      <!-- 在线节点 -->
      <div v-if="!loading && activeTab === 'peers'">
        <div v-if="!peers.length" class="empty-state">
          <div class="empty-state-icon">📡</div>
          <p class="empty-state-text">暂无在线节点</p>
          <p class="empty-state-hint">其他猫爪音乐客户端连接后将显示在这里</p>
        </div>
        <div v-else style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div v-for="p in peers" :key="p.deviceId" style="display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid var(--border);">
            <div style="width:36px;height:36px;border-radius:50%;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:14px;font-weight:700;">
              {{ p.name?.[0] || '?' }}
            </div>
            <div style="flex:1;min-width:0;">
              <div style="font-weight:600;font-size:14px;">{{ p.name }}</div>
              <div style="font-size:12px;color:var(--text-muted);">
                {{ p.wan || '内网' }}{{ p.port ? ':' + p.port : '' }}
                · {{ p.library?.songCount || 0 }} 首歌曲
                <span v-if="p.relayOnly" style="color:var(--warning);"> · 仅中继</span>
              </div>
            </div>
            <div style="font-size:11px;color:var(--text-muted);">{{ new Date(p.connectedAt).toLocaleString() }}</div>
          </div>
        </div>
      </div>

      <!-- DHT 网络 -->
      <div v-if="!loading && activeTab === 'dht'">
        <!-- 引导步骤说明 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--accent);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:12px;">🐾 猫爪驿站使用指南</h3>
          <div style="display:flex;flex-direction:column;gap:10px;">
            <div style="display:flex;align-items:flex-start;gap:10px;">
              <span style="width:24px;height:24px;border-radius:50%;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700;flex-shrink:0;">1</span>
              <div style="font-size:13px;line-height:1.5;">
                <strong>开启 Kademlia DHT</strong>
                <span style="color:var(--text-muted);"> — 去中心化节点发现的基础，未开启时其他功能不可用</span>
                <span v-if="dhtEnabled" style="color:var(--success);margin-left:6px;">✓ 已完成</span>
              </div>
            </div>
            <div style="display:flex;align-items:flex-start;gap:10px;">
              <span :style="{ width:'24px',height:'24px',borderRadius:'50%',display:'flex',alignItems:'center',justifyContent:'center',fontSize:'12px',fontWeight:'700',flexShrink:'0', background: dhtEnabled ? 'var(--accent-dim)' : 'var(--bg-tertiary)', color: dhtEnabled ? 'var(--accent)' : 'var(--text-muted)' }">2</span>
              <div style="font-size:13px;line-height:1.5;" :style="{ color: dhtEnabled ? 'inherit' : 'var(--text-muted)' }">
                <strong>连接 Bootstrap 节点</strong>
                <span style="color:var(--text-muted);"> — 输入其他猫爪驿站服务器的地址，加入 P2P 网络</span>
                <span v-if="dhtNodes.length" style="color:var(--success);margin-left:6px;">✓ 已连接 {{ dhtNodes.length }} 个节点</span>
              </div>
            </div>
            <div style="display:flex;align-items:flex-start;gap:10px;">
              <span :style="{ width:'24px',height:'24px',borderRadius:'50%',display:'flex',alignItems:'center',justifyContent:'center',fontSize:'12px',fontWeight:'700',flexShrink:'0', background: dhtEnabled ? 'var(--accent-dim)' : 'var(--bg-tertiary)', color: dhtEnabled ? 'var(--accent)' : 'var(--text-muted)' }">3</span>
              <div style="font-size:13px;line-height:1.5;" :style="{ color: dhtEnabled ? 'inherit' : 'var(--text-muted)' }">
                <strong>注册/登录猫爪驿站账号</strong>
                <span style="color:var(--text-muted);"> — 记录小鱼干积分，支持跨设备同时挂机</span>
                <span v-if="clawToken" style="color:var(--success);margin-left:6px;">✓ 已登录</span>
              </div>
            </div>
            <div style="display:flex;align-items:flex-start;gap:10px;">
              <span :style="{ width:'24px',height:'24px',borderRadius:'50%',display:'flex',alignItems:'center',justifyContent:'center',fontSize:'12px',fontWeight:'700',flexShrink:'0', background: (dhtEnabled && clawToken) ? 'var(--accent-dim)' : 'var(--bg-tertiary)', color: (dhtEnabled && clawToken) ? 'var(--accent)' : 'var(--text-muted)' }">4</span>
              <div style="font-size:13px;line-height:1.5;" :style="{ color: (dhtEnabled && clawToken) ? 'inherit' : 'var(--text-muted)' }">
                <strong>开始找歌、分享、赚取小鱼干</strong>
                <span style="color:var(--text-muted);"> — 在线 1 小时奖励 10🐟，上传 1GB 奖励 10🐟</span>
              </div>
            </div>
          </div>
        </div>

        <!-- DHT 开关 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:16px;">
            <div>
              <h3 style="font-size:16px;font-weight:600;">Kademlia DHT</h3>
              <p style="font-size:12px;color:var(--text-muted);margin-top:4px;">去中心化节点发现，无需中心服务器</p>
            </div>
            <button class="btn" :class="dhtEnabled ? 'btn-primary' : 'btn-secondary'" :disabled="toggling" @click="toggleDht">
              {{ toggling ? '切换中...' : (dhtEnabled ? '✓ 已启用' : '启用 DHT') }}
            </button>
          </div>

          <div v-if="dhtEnabled" style="display:flex;gap:8px;align-items:center;">
            <input type="text" v-model="bootstrapAddr" placeholder="Bootstrap 节点地址 (如 192.168.1.100:37825)"
              style="flex:1;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;"
              @keyup.enter="doBootstrap" />
            <button class="btn btn-secondary" @click="doBootstrap" :disabled="!bootstrapAddr.trim()">连接</button>
          </div>
        </div>

        <!-- DHT 节点列表 -->
        <div v-if="!dhtEnabled" class="empty-state">
          <div class="empty-state-icon">🌐</div>
          <p class="empty-state-text">DHT 未启用</p>
          <p class="empty-state-hint">点击上方按钮启用去中心化节点发现<br/>开启后才能注册账号、找歌、赚取小鱼干</p>
        </div>
        <div v-else-if="!dhtNodes.length" class="empty-state">
          <div class="empty-state-icon">🔍</div>
          <p class="empty-state-text">暂无已知节点</p>
          <p class="empty-state-hint">输入 Bootstrap 地址连接其他节点，或等待其他节点发现你</p>
        </div>
        <div v-else style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div v-for="n in dhtNodes" :key="n.id" style="display:flex;align-items:center;gap:12px;padding:8px 0;border-bottom:1px solid var(--border);">
            <div :style="{ width:'8px',height:'8px',borderRadius:'50%',background: n.isAlive ? 'var(--success)' : 'var(--danger)' }"></div>
            <code style="font-size:12px;flex:1;color:var(--text-secondary);">{{ n.id?.substring(0,16) }}...</code>
            <span style="font-size:12px;color:var(--text-muted);">{{ n.address }}</span>
            <span style="font-size:12px;">信誉 {{ n.reputation }}</span>
          </div>
        </div>
      </div>

      <!-- 节点信誉 -->
      <div v-if="!loading && activeTab === 'reputation'">
        <div v-if="!reputation.length" class="empty-state">
          <div class="empty-state-icon">⭐</div>
          <p class="empty-state-text">暂无信誉记录</p>
          <p class="empty-state-hint">P2P 传输活动后将自动记录</p>
        </div>
        <div v-else style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <div v-for="r in reputation" :key="r.nodeId" style="display:flex;align-items:center;gap:12px;padding:8px 0;border-bottom:1px solid var(--border);">
            <code style="font-size:12px;flex:1;color:var(--text-secondary);">{{ r.nodeId?.substring(0,16) }}...</code>
            <div style="width:100px;height:6px;background:var(--bg-hover);border-radius:3px;overflow:hidden;">
              <div :style="{ width: r.score + '%', height:'100%', background: r.score >= 80 ? 'var(--success)' : r.score < 10 ? 'var(--danger)' : 'var(--accent)', borderRadius:'3px' }"></div>
            </div>
            <span style="font-size:12px;width:30px;text-align:right;" :style="{ color: r.score >= 80 ? 'var(--success)' : r.score < 10 ? 'var(--danger)' : 'var(--text-primary)' }">{{ r.score }}</span>
            <span style="font-size:11px;color:var(--text-muted);">{{ Math.round(r.successRate * 100) }}%</span>
            <span v-if="r.isBlacklisted" style="font-size:10px;color:var(--danger);background:rgba(255,71,87,0.15);padding:2px 6px;border-radius:4px;">黑名单</span>
            <span v-if="r.isTrusted" style="font-size:10px;color:var(--success);background:rgba(46,213,115,0.15);padding:2px 6px;border-radius:4px;">可信</span>
          </div>
        </div>
      </div>

      <!-- 找歌 -->
      <div v-if="!loading && activeTab === 'search'">
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:15px;font-weight:600;margin-bottom:12px;">🔍 在 P2P 网络中找歌</h3>
          <p style="font-size:12px;color:var(--text-muted);margin-bottom:12px;">通过 DHT 查询哪些节点持有指定歌曲（需先启用 DHT）</p>
          <div style="display:flex;gap:8px;flex-wrap:wrap;">
            <input type="text" v-model="searchArtist" placeholder="艺术家"
              style="flex:1;min-width:120px;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;"
              @keyup.enter="searchSong" />
            <input type="text" v-model="searchTitle" placeholder="标题"
              style="flex:1;min-width:120px;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;"
              @keyup.enter="searchSong" />
            <button class="btn btn-primary" @click="searchSong" :disabled="searching">
              {{ searching ? '搜索中...' : '搜索' }}
            </button>
          </div>

          <div v-if="searchResults" style="margin-top:16px;">
            <div style="font-size:12px;color:var(--text-muted);margin-bottom:8px;">
              查询: {{ searchResults.query }} · 找到 {{ searchResults.totalHolders || 0 }} 个持有者
            </div>
            <div v-if="!searchResults.totalHolders" class="empty-state" style="padding:20px;">
              <div class="empty-state-icon">😢</div>
              <p class="empty-state-text">网络中暂无节点持有此歌</p>
            </div>
            <div v-else style="background:var(--bg-tertiary);border-radius:8px;padding:12px;">
              <div v-for="h in searchResults.holders" :key="h.nodeId" style="display:flex;align-items:center;gap:12px;padding:8px 0;border-bottom:1px solid var(--border);">
                <code style="font-size:12px;flex:1;color:var(--accent);">{{ h.nodeId }}</code>
                <span style="font-size:12px;color:var(--text-muted);">{{ h.address }}</span>
                <span style="font-size:12px;">{{ h.songCount }} 首</span>
                <span v-if="h.isExact" style="font-size:10px;color:var(--success);background:rgba(46,213,115,0.15);padding:2px 6px;border-radius:4px;">精确命中</span>
              </div>
            </div>
          </div>
        </div>

        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;">
          <h3 style="font-size:15px;font-weight:600;margin-bottom:12px;">📤 分享本地歌曲</h3>
          <p style="font-size:12px;color:var(--text-muted);margin-bottom:12px;">将本服务器的歌曲通过 UDP P2P 发送给在线节点（对端需支持猫爪驿站协议）</p>
          <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center;">
            <input type="text" v-model="offerSongId" placeholder="本地歌曲 ID（数字）"
              style="flex:1;min-width:140px;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;" />
            <select v-model="offerPeerId"
              style="flex:1;min-width:140px;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;">
              <option value="">选择在线节点...</option>
              <option v-for="p in peers" :key="p.deviceId" :value="p.deviceId">
                {{ p.name }} ({{ p.library?.songCount || 0 }} 首)
              </option>
            </select>
            <button class="btn btn-primary" @click="doOffer" :disabled="offering">
              {{ offering ? '发起中...' : '发起分享' }}
            </button>
          </div>
          <p v-if="!peers.length" style="font-size:11px;color:var(--warning);margin-top:8px;">当前无在线节点，需对端先连接猫爪驿站 WebSocket</p>
        </div>
      </div>

      <!-- 传输管理 -->
      <div v-if="!loading && activeTab === 'transfers'">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;">
          <h3 style="font-size:15px;font-weight:600;">📦 活跃传输任务</h3>
          <button class="btn btn-secondary" @click="loadTransfers">⟳ 刷新</button>
        </div>

        <div v-if="!transfers.length" class="empty-state">
          <div class="empty-state-icon">📭</div>
          <p class="empty-state-text">暂无传输任务</p>
          <p class="empty-state-hint">在"找歌"tab 发起分享后，任务将显示在这里</p>
        </div>

        <div v-else style="display:flex;flex-direction:column;gap:12px;">
          <div v-for="t in transfers" :key="t.taskId"
            style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div style="display:flex;align-items:center;gap:8px;margin-bottom:8px;">
              <span style="font-size:11px;padding:2px 8px;border-radius:4px;"
                :style="t.role === 'sender' ? 'background:rgba(0,212,255,0.15);color:var(--accent);' : 'background:rgba(46,213,115,0.15);color:var(--success);'">
                {{ t.role === 'sender' ? '发送' : '接收' }}
              </span>
              <span style="font-weight:600;font-size:14px;flex:1;">{{ t.fileName || '未知文件' }}</span>
              <span style="font-size:11px;color:var(--text-muted);">{{ formatSize(t.totalSize) }}</span>
              <button class="btn btn-ghost" style="font-size:11px;padding:4px 10px;" @click="cancelTask(t.taskId)">取消</button>
            </div>
            <div style="display:flex;align-items:center;gap:8px;">
              <div style="flex:1;height:6px;background:var(--bg-hover);border-radius:3px;overflow:hidden;">
                <div :style="{ width: formatProgress(t.progress), height:'100%', background: t.status === 'complete' ? 'var(--success)' : t.status === 'failed' ? 'var(--danger)' : 'var(--accent)', borderRadius:'3px', transition:'width 0.3s' }"></div>
              </div>
              <span style="font-size:12px;width:90px;text-align:right;color:var(--text-secondary);">
                {{ t.receivedChunks }}/{{ t.totalChunks }} · {{ formatProgress(t.progress) }}
              </span>
            </div>
            <div style="font-size:11px;color:var(--text-muted);margin-top:6px;">
              任务 {{ t.taskId }} · 对端 {{ (t.peerDeviceId || '').substring(0,12) }}... · {{ t.status }}
            </div>
          </div>
        </div>
      </div>

      <!-- 小鱼干账本 -->
      <div v-if="!loading && activeTab === 'ledger'">
        <!-- 规则说明 -->
        <div style="font-size:12px;color:var(--text-secondary);margin-bottom:16px;padding:10px 14px;background:var(--bg-secondary);border-radius:8px;border-left:3px solid var(--accent);">
          🐟 <strong>小鱼干规则</strong>：注册赠送 100 🐟 · 在线 1 小时 +10 🐟 · 上传 1 GB +10 🐟 · 下载 1 GB -10 🐟
        </div>

        <!-- 概览 -->
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin-bottom:20px;">
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">链高度</div>
            <div style="font-size:24px;font-weight:700;color:var(--accent);">{{ chainHeight }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">持鱼节点</div>
            <div style="font-size:24px;font-weight:700;">{{ balances.length }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">小鱼干总量</div>
            <div style="font-size:24px;font-weight:700;color:var(--success);">🐟 {{ balances.reduce((s,b) => s + b.balance, 0) }}</div>
          </div>
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <div style="font-size:11px;color:var(--text-muted);text-transform:uppercase;">账本大小</div>
            <div style="font-size:24px;font-weight:700;color:var(--warning);">{{ formatSize(ledgerSize) }}</div>
            <div style="font-size:10px;color:var(--text-muted);margin-top:2px;">已修剪至 #{{ prunedToIndex }}</div>
          </div>
        </div>
        <div style="font-size:11px;color:var(--text-muted);margin-bottom:16px;padding:8px 12px;background:var(--bg-secondary);border-radius:6px;border-left:3px solid var(--success);">
          💾 快照+修剪机制：每 500 块自动生成余额快照并删除旧区块，账本大小恒定，不会像 BTC 那样无限膨胀
        </div>

        <!-- 小鱼干排行 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;margin-bottom:16px;">
          <h3 style="font-size:14px;font-weight:600;margin-bottom:12px;">🏆 小鱼干排行</h3>
          <div v-if="!balances.length" style="font-size:12px;color:var(--text-muted);padding:8px 0;">暂无节点注册</div>
          <div v-else>
            <div v-for="(b, i) in balances" :key="b.deviceId" style="display:flex;align-items:center;gap:10px;padding:6px 0;border-bottom:1px solid var(--border);">
              <span style="width:24px;text-align:center;font-size:13px;font-weight:700;color:var(--text-muted);">{{ i + 1 }}</span>
              <code style="font-size:12px;flex:1;color:var(--text-secondary);">{{ (b.deviceId || '').substring(0,20) }}</code>
              <span style="font-size:14px;font-weight:600;" :style="{ color: b.balance > 100 ? 'var(--success)' : b.balance < 10 ? 'var(--danger)' : 'var(--accent)' }">🐟 {{ b.balance }}</span>
            </div>
          </div>
        </div>

        <!-- 区块链浏览器 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;margin-bottom:16px;">
          <h3 style="font-size:14px;font-weight:600;margin-bottom:12px;">⛓ 区块链浏览器（最近 10 块）</h3>
          <div v-if="!chainBlocks.length" style="font-size:12px;color:var(--text-muted);padding:8px 0;">暂无区块</div>
          <div v-else style="display:flex;flex-direction:column;gap:8px;">
            <div v-for="blk in [...chainBlocks].reverse()" :key="blk.index"
              style="background:var(--bg-tertiary);border-radius:8px;padding:12px;border-left:3px solid var(--accent);">
              <div style="display:flex;align-items:center;gap:8px;margin-bottom:4px;">
                <span style="font-size:11px;color:var(--accent);font-weight:600;">#{{ blk.index }}</span>
                <code style="font-size:10px;color:var(--text-muted);flex:1;">{{ (blk.hash || '').substring(0,24) }}...</code>
                <span style="font-size:10px;color:var(--text-muted);">{{ formatTime(blk.timestamp) }}</span>
                <span style="font-size:10px;color:var(--text-secondary);">{{ blk.transactions.length }} 笔交易</span>
              </div>
              <div v-for="tx in blk.transactions" :key="tx.id" style="font-size:11px;color:var(--text-secondary);padding:2px 0;">
                {{ formatTxType(tx.type) }} · 🐟 {{ tx.amount }} · {{ tx.from === 'SYSTEM' ? '→ ' + (tx.to || '').substring(0,12) : (tx.from || '').substring(0,12) + ' → 系统' }} · {{ tx.remark }}
              </div>
            </div>
          </div>
        </div>

        <!-- 交易历史查询 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
          <h3 style="font-size:14px;font-weight:600;margin-bottom:12px;">📋 交易历史查询</h3>
          <div style="display:flex;gap:8px;margin-bottom:12px;">
            <input type="text" v-model="historyDeviceId" placeholder="输入 deviceId 查询交易历史"
              style="flex:1;padding:8px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:13px;outline:none;"
              @keyup.enter="loadHistory" />
            <button class="btn btn-secondary" @click="loadHistory" :disabled="!historyDeviceId.trim()">查询</button>
          </div>
          <div v-if="history" style="margin-top:12px;">
            <div style="font-size:12px;color:var(--text-muted);margin-bottom:8px;">
              节点余额: <span style="color:var(--success);font-weight:600;">🐟 {{ history.balance }}</span> · 交易总数: {{ history.totalTransactions }}
            </div>
            <div v-if="!history.totalTransactions" style="font-size:12px;color:var(--text-muted);">该节点暂无交易记录</div>
            <div v-else style="background:var(--bg-tertiary);border-radius:8px;padding:10px;">
              <div v-for="tx in history.transactions" :key="tx.id" style="display:flex;align-items:center;gap:8px;padding:4px 0;border-bottom:1px solid var(--border);font-size:11px;">
                <span style="width:70px;" :style="{ color: tx.type === 'upload' ? 'var(--success)' : tx.type === 'download' ? 'var(--warning)' : 'var(--accent)' }">{{ formatTxType(tx.type) }}</span>
                <span :style="{ color: tx.type === 'download' ? 'var(--danger)' : 'var(--success)', width:'60px', fontWeight:600 }">{{ tx.type === 'download' ? '-' : '+' }}🐟 {{ tx.amount }}</span>
                <span style="flex:1;color:var(--text-muted);">{{ tx.remark }}</span>
                <span style="color:var(--text-muted);">{{ formatTime(tx.timestamp) }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 账号 -->
      <div v-if="!loading && activeTab === 'account'">
        <!-- DHT 未开启提示 -->
        <div v-if="!dhtEnabled" class="empty-state">
          <div class="empty-state-icon">🌐</div>
          <p class="empty-state-text">需先开启 DHT</p>
          <p class="empty-state-hint">猫爪驿站账号依赖 P2P 网络，请先到「DHT 网络」tab 启用</p>
          <button class="btn btn-primary" style="margin-top:12px;" @click="activeTab = 'dht'">前往开启 DHT</button>
        </div>
        <!-- 未登录：登录/注册切换 -->
        <div v-else-if="!clawToken" style="max-width:420px;">
          <div style="display:flex;gap:8px;margin-bottom:20px;">
            <button class="btn" :class="authMode === 'login' ? 'btn-primary' : 'btn-secondary'" style="flex:1;" @click="authMode = 'login'">登录</button>
            <button class="btn" :class="authMode === 'register' ? 'btn-primary' : 'btn-secondary'" style="flex:1;" @click="authMode = 'register'">注册新账号</button>
          </div>

          <!-- 登录表单 -->
          <div v-if="authMode === 'login'" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:24px;">
            <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;">🐾 登录猫爪驿站</h3>
            <div style="margin-bottom:12px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">用户名</label>
              <input type="text" v-model="loginUsername" placeholder="3-20 位字符"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;"
                @keyup.enter="clawLogin" />
            </div>
            <div style="margin-bottom:16px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">密码</label>
              <input type="text" class="pwd-field" v-model="loginPassword" placeholder="至少 6 位（支持中文）"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;"
                @keyup.enter="clawLogin" />
            </div>
            <button class="btn btn-primary" style="width:100%;" @click="clawLogin" :disabled="clawLoggingIn">
              {{ clawLoggingIn ? '登录中...' : '登录' }}
            </button>
            <div style="font-size:11px;color:var(--text-muted);margin-top:12px;line-height:1.6;">
              💡 每台设备会生成独立 Token，可在多设备同时登录（如 NAS + 手机）。<br/>
              登录失败 5 次将锁定 15 分钟。
            </div>
          </div>

          <!-- 注册表单 -->
          <div v-else style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:24px;">
            <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;">✨ 注册猫爪驿站账号</h3>
            <div style="margin-bottom:12px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">用户名</label>
              <input type="text" v-model="regUsername" placeholder="3-20 位字符（字母数字下划线）"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;" />
            </div>
            <div style="margin-bottom:12px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">昵称（可选）</label>
              <input type="text" v-model="regDisplayName" placeholder="展示给其他用户看的名字"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;" />
            </div>
            <div style="margin-bottom:12px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">密码</label>
              <input type="text" class="pwd-field" v-model="regPassword" placeholder="至少 6 位（支持中文）"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;" />
            </div>
            <div style="margin-bottom:16px;">
              <label style="display:block;font-size:12px;color:var(--text-muted);margin-bottom:6px;">确认密码</label>
              <input type="text" class="pwd-field" v-model="regPasswordConfirm" placeholder="再次输入密码"
                style="width:100%;padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;"
                @keyup.enter="clawRegister" />
              <div v-if="regPasswordConfirm && regPassword !== regPasswordConfirm" style="font-size:11px;color:var(--danger);margin-top:4px;">两次输入的密码不一致</div>
            </div>
            <button class="btn btn-primary" style="width:100%;" @click="clawRegister" :disabled="clawRegistering">
              {{ clawRegistering ? '注册中...' : '注册（赠送 100 🐟）' }}
            </button>
            <div style="font-size:11px;color:var(--text-muted);margin-top:12px;line-height:1.6;">
              🐟 注册赠送 100 小鱼干 · 在线 1 小时奖励 10🐟 · 上传 1GB 奖励 10🐟 · 下载 1GB 消耗 10🐟<br/>
              🔒 密码使用 PBKDF2-SHA256 加盐哈希存储，即使账本泄露也无法反推密码。
            </div>
          </div>
        </div>

        <!-- 已登录：账号信息 + 设备管理 + 改密 -->
        <div v-else>
          <!-- 账号信息卡片 -->
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
            <div style="display:flex;align-items:center;gap:16px;">
              <div style="width:56px;height:56px;border-radius:50%;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:24px;font-weight:700;">
                {{ clawAccount?.displayName?.[0] || clawAccount?.username?.[0] || '?' }}
              </div>
              <div style="flex:1;">
                <div style="font-size:18px;font-weight:600;">{{ clawAccount?.displayName || clawAccount?.username }}</div>
                <div style="font-size:12px;color:var(--text-muted);">@{{ clawAccount?.username }} · 账号 ID #{{ clawAccount?.accountId }}</div>
              </div>
              <button class="btn btn-secondary" @click="clawLogout">退出登录</button>
            </div>
            <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin-top:16px;">
              <div style="background:var(--bg-tertiary);border-radius:8px;padding:12px;text-align:center;">
                <div style="font-size:11px;color:var(--text-muted);">🐟 小鱼干</div>
                <div style="font-size:22px;font-weight:700;color:var(--success);margin-top:4px;">{{ clawAccount?.balance ?? 0 }}</div>
              </div>
              <div style="background:var(--bg-tertiary);border-radius:8px;padding:12px;text-align:center;">
                <div style="font-size:11px;color:var(--text-muted);">📱 在线设备</div>
                <div style="font-size:22px;font-weight:700;margin-top:4px;">{{ clawDevices.length }}</div>
              </div>
              <div style="background:var(--bg-tertiary);border-radius:8px;padding:12px;text-align:center;">
                <div style="font-size:11px;color:var(--text-muted);">🕐 注册时间</div>
                <div style="font-size:13px;font-weight:600;margin-top:6px;">{{ formatTime(clawAccount?.createdAt ? new Date(clawAccount.createdAt).getTime() : 0) || '-' }}</div>
              </div>
            </div>
            <button class="btn btn-secondary" style="margin-top:12px;font-size:12px;" @click="loadClawMe">⟳ 刷新余额</button>
          </div>

          <!-- 设备管理 -->
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;margin-bottom:16px;">
            <h3 style="font-size:14px;font-weight:600;margin-bottom:12px;">📱 设备管理</h3>
            <div v-if="!clawDevices.length" style="font-size:12px;color:var(--text-muted);padding:8px 0;">暂无设备</div>
            <div v-else>
              <div v-for="d in clawDevices" :key="d.deviceId" style="display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid var(--border);">
                <div style="width:32px;height:32px;border-radius:6px;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:14px;">
                  📱
                </div>
                <div style="flex:1;min-width:0;">
                  <div style="font-size:13px;font-weight:600;">
                    {{ d.deviceName || '未命名设备' }}
                    <span v-if="d.deviceId === getDeviceId()" style="font-size:10px;color:var(--accent);margin-left:6px;padding:2px 6px;background:var(--accent-dim);border-radius:4px;">当前设备</span>
                  </div>
                  <div style="font-size:11px;color:var(--text-muted);margin-top:2px;">
                    <code style="color:var(--text-secondary);">{{ d.deviceId.substring(0, 20) }}{{ d.deviceId.length > 20 ? '...' : '' }}</code>
                    · 最后活跃 {{ formatTime(d.lastActiveAt ? new Date(d.lastActiveAt).getTime() : 0) || '从未' }}
                  </div>
                </div>
                <button class="btn btn-secondary" style="font-size:11px;padding:4px 10px;" @click="revokeClawDevice(d.deviceId)">退出</button>
              </div>
            </div>
            <div style="font-size:11px;color:var(--text-muted);margin-top:12px;line-height:1.6;">
              💡 每台设备登录后获得独立 Token，互不影响。NAS 挂机与手机下载可同时运行，积分统一计入账号。
            </div>
          </div>

          <!-- 修改密码 -->
          <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:16px;">
            <h3 style="font-size:14px;font-weight:600;margin-bottom:12px;">🔑 修改密码</h3>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px;">
              <input type="text" class="pwd-field" v-model="oldPassword" placeholder="原密码"
                style="padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;" />
              <input type="text" class="pwd-field" v-model="newPassword" placeholder="新密码（至少 6 位，支持中文）"
                style="padding:10px 12px;border-radius:6px;border:1px solid var(--border-light);background:var(--bg-tertiary);color:var(--text-primary);font-size:14px;outline:none;box-sizing:border-box;" />
            </div>
            <button class="btn btn-primary" @click="clawChangePassword" :disabled="changingPwd">
              {{ changingPwd ? '修改中...' : '修改密码' }}
            </button>
            <div style="font-size:11px;color:var(--warning);margin-top:8px;">
              ⚠ 修改密码后所有设备 Token 失效，需要重新登录。
            </div>
          </div>
        </div>
      </div>

    </div>
  `
};

// ── Genres View ──
const GenresView = {
  name: 'GenresView',
  setup() {
    const genres = ref([]);
    const loading = ref(true);

    onMounted(async () => {
      try {
        const data = await api.getGenres();
        genres.value = data.items || [];
      } catch {}
      loading.value = false;
    });

    return { genres, loading };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">流派</h1>
        <p class="page-subtitle">按音乐流派浏览</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!genres.length" class="empty-state">
        <div class="empty-state-icon">🎼</div>
        <p class="empty-state-text">暂无流派信息</p>
        <p class="empty-state-hint">扫描音乐库后将自动提取流派标签</p>
      </div>
      <div v-else style="display:flex;flex-wrap:wrap;gap:12px;">
        <router-link v-for="g in genres" :key="g.name" :to="'/genres/' + encodeURIComponent(g.name)"
          style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:12px;padding:16px 24px;text-decoration:none;color:inherit;transition:all 0.2s;min-width:140px;">
          <div style="font-size:16px;font-weight:600;margin-bottom:4px;">{{ g.name }}</div>
          <div style="font-size:12px;color:var(--text-muted);">{{ g.songCount }} 首 · {{ g.albumCount }} 张专辑</div>
        </router-link>
      </div>
    </div>
  `
};

// ── Genre Detail View ──
const GenreDetailView = {
  name: 'GenreDetailView',
  setup() {
    const genreName = ref('');
    const songs = ref([]);
    const total = ref(0);
    const loading = ref(true);

    onMounted(async () => {
      const name = decodeURIComponent(window.location.hash.split('/').pop() || '');
      genreName.value = name;
      try {
        const data = await api.getGenreSongs(name, { page: 1, page_size: 200 });
        songs.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    });

    return { genreName, songs, total, loading, playSong, formatTime, store };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <router-link to="/genres" style="font-size:13px;color:var(--accent);text-decoration:none;">← 返回流派列表</router-link>
        <h1 class="page-title" style="margin-top:8px;">{{ genreName }}</h1>
        <p class="page-subtitle">{{ total }} 首歌曲</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else class="song-list">
        <div v-for="(song, i) in songs" :key="song.id" class="song-row" @click="playSong(song, songs, i)">
          <span class="song-idx">{{ i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ song.title }}</div>
            <div class="song-artist-sub">{{ song.artist }}</div>
          </div>
          <span class="song-album">{{ song.album }}</span>
          <span class="song-duration">{{ formatTime(song.duration) }}</span>
        </div>
      </div>
    </div>
  `
};

// ── Updated Admin View (with system info and user management) ──
const AdminView2 = {
  name: 'AdminView2',
  setup() {
    const serverStats = ref(null);
    const scanStatus = ref(null);
    const systemInfo = ref(null);
    const users = ref([]);
    const scanning = ref(false);
    const loading = ref(true);

    async function load() {
      try {
        const [stats, scan, sys, usrs] = await Promise.all([
          api.getServerStats().catch(() => null),
          api.getScanStatus().catch(() => null),
          api.getSystemInfo().catch(() => null),
          api.getUsers().catch(() => [])
        ]);
        serverStats.value = stats;
        scanStatus.value = scan;
        systemInfo.value = sys;
        users.value = Array.isArray(usrs) ? usrs : [];
      } catch {}
      loading.value = false;
    }

    async function triggerScan(incremental) {
      scanning.value = true;
      try {
        if (incremental) await api.triggerIncrementalScan();
        else await api.triggerScan();
      } catch {}
      const interval = setInterval(async () => {
        try {
          scanStatus.value = await api.getScanStatus();
          if (!scanStatus.value?.scanning) {
            clearInterval(interval);
            scanning.value = false;
            await load();
          }
        } catch { clearInterval(interval); scanning.value = false; }
      }, 2000);
    }

    async function doCleanup() {
      try {
        const result = await api.cleanup();
        showToast('清理完成，移除 ' + (result?.removed || 0) + ' 条失效记录', 'success');
        await load();
      } catch (e) { showToast('清理失败: ' + e.message, 'error'); }
    }

    async function doDeleteUser(id) {
      if (!confirm('确定删除该用户？')) return;
      try {
        await api.deleteUser(id);
        showToast('用户已删除', 'success');
        await load();
      } catch (e) { showToast('删除失败: ' + e.message, 'error'); }
    }

    onMounted(load);

    return { serverStats, scanStatus, systemInfo, users, scanning, loading, triggerScan, doCleanup, doDeleteUser };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">管理面板</h1>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else>
        <!-- 音乐库统计 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">音乐库</h3>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:12px;">
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">歌曲</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.songCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">艺术家</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.artistCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">专辑</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.albumCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">用户</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.userCount }}</div>
            </div>
            <div v-if="serverStats">
              <div style="font-size:11px;color:var(--text-muted);">总播放</div>
              <div style="font-size:22px;font-weight:700;">{{ serverStats.totalPlays }}</div>
            </div>
          </div>
        </div>

        <!-- 扫描 -->
        <div style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">扫描</h3>
          <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;">
            <button class="btn btn-primary" :disabled="scanning" @click="triggerScan(false)">
              {{ scanning ? '扫描中...' : '开始扫描' }}
            </button>
            <button class="btn btn-secondary" :disabled="scanning" @click="triggerScan(true)">增量扫描</button>
            <button class="btn btn-secondary" @click="doCleanup">清理失效记录</button>
          </div>
          <div v-if="scanStatus?.last_result" style="margin-top:12px;font-size:13px;color:var(--text-secondary);">
            上次扫描: 处理 {{ scanStatus.last_result.processed }} · 新增 {{ scanStatus.last_result.added }} · 更新 {{ scanStatus.last_result.updated }} · 跳过 {{ scanStatus.last_result.skipped }} · 错误 {{ scanStatus.last_result.errors }}
          </div>
        </div>

        <!-- 系统信息 -->
        <div v-if="systemInfo" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">系统</h3>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px;font-size:13px;">
            <div><span style="color:var(--text-muted);">版本: </span>{{ systemInfo.server?.version }}</div>
            <div><span style="color:var(--text-muted);">运行时: </span>{{ systemInfo.server?.runtime }}</div>
            <div><span style="color:var(--text-muted);">操作系统: </span>{{ systemInfo.system?.os }}</div>
            <div><span style="color:var(--text-muted);">CPU 核心: </span>{{ systemInfo.system?.processor_count }}</div>
            <div><span style="color:var(--text-muted);">内存占用: </span>{{ systemInfo.system?.working_set_mb }} MB</div>
            <div><span style="color:var(--text-muted);">数据库大小: </span>{{ systemInfo.database?.size_mb }} MB</div>
          </div>
        </div>

        <!-- 用户管理 -->
        <div v-if="users.length" style="background:var(--bg-secondary);border:1px solid var(--border);border-radius:10px;padding:20px;margin-bottom:16px;">
          <h3 style="font-size:16px;font-weight:600;margin-bottom:16px;padding-bottom:12px;border-bottom:1px solid var(--border);">用户管理</h3>
          <div v-for="u in users" :key="u.id" style="display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid var(--border);">
            <div style="width:32px;height:32px;border-radius:50%;background:var(--accent-dim);color:var(--accent);display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:700;">
              {{ u.display_name?.[0] || u.username?.[0] || '?' }}
            </div>
            <div style="flex:1;">
              <div style="font-weight:600;font-size:14px;">{{ u.display_name || u.username }}</div>
              <div style="font-size:12px;color:var(--text-muted);">{{ u.role }} · 注册 {{ new Date(u.created_at).toLocaleDateString() }}</div>
            </div>
            <button class="btn-icon" @click="doDeleteUser(u.id)" title="删除用户" style="color:var(--danger);">🗑</button>
          </div>
        </div>
      </div>
    </div>
  `
};

// ────────────────────────────────────────
// Router & App Init
// ────────────────────────────────────────

// ── Login View ──
const LoginView = {
  name: 'LoginView',
  setup() {
    const isRegister = ref(false);
    const username = ref('');
    const password = ref('');
    const displayName = ref('');
    const error = ref('');
    const loading = ref(false);

    async function handleSubmit() {
      error.value = '';
      if (!username.value || !password.value) {
        error.value = '请输入用户名和密码';
        return;
      }
      loading.value = true;
      try {
        if (isRegister.value) {
          await api.register(username.value, password.value, displayName.value || username.value);
          // Auto login after register
        }
        const data = await login(username.value, password.value);
        store.user = data.user;
        store.isAuthenticated = true;
        await loadOverview();
      } catch (e) {
        error.value = e.message || (isRegister.value ? '注册失败' : '登录失败');
      }
      loading.value = false;
    }

    return { isRegister, username, password, displayName, error, loading, handleSubmit };
  },
  template: `
    <div style="display:flex;align-items:center;justify-content:center;flex:1;width:100%;min-height:100vh;background:var(--bg-primary);">
      <div style="width:400px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:16px;padding:32px;">
        <div style="text-align:center;margin-bottom:24px;">
          <div style="font-size:40px;margin-bottom:8px;">🐱</div>
          <h1 style="font-size:22px;font-weight:700;">Cat<span style="color:var(--accent);">Claw</span> Music</h1>
          <p style="color:var(--text-muted);font-size:13px;margin-top:4px;">{{ isRegister ? '创建管理员账户' : '登录你的账户' }}</p>
        </div>

        <div v-if="error" style="background:rgba(255,71,87,0.1);border:1px solid var(--danger);border-radius:8px;padding:10px 14px;margin-bottom:16px;color:var(--danger);font-size:13px;">
          {{ error }}
        </div>

        <div class="form-group" v-if="isRegister">
          <label>显示名称</label>
          <input type="text" v-model="displayName" placeholder="你的名称" />
        </div>
        <div class="form-group">
          <label>用户名</label>
          <input type="text" v-model="username" placeholder="用户名" @keyup.enter="handleSubmit" />
        </div>
        <div class="form-group">
          <label>密码</label>
          <input type="text" class="pwd-field" v-model="password" placeholder="至少6位" @keyup.enter="handleSubmit" />
        </div>

        <button class="btn btn-primary" style="width:100%;padding:12px;font-size:15px;margin-top:8px;" :disabled="loading" @click="handleSubmit">
          {{ loading ? '处理中...' : (isRegister ? '创建账户' : '登录') }}
        </button>

        <div style="text-align:center;margin-top:16px;">
          <a style="color:var(--accent);font-size:13px;cursor:pointer;text-decoration:none;" @click="isRegister = !isRegister; error = '';">
            {{ isRegister ? '已有账户？去登录' : '没有账户？创建管理员' }}
          </a>
        </div>
      </div>
    </div>
  `
};

const routes = [
  { path: '/login', component: LoginView },
  { path: '/', component: HomeView },
  { path: '/songs', component: SongsView },
  { path: '/artists', component: ArtistsView },
  { path: '/artists/:id', component: ArtistDetailView },
  { path: '/albums', component: AlbumsView },
  { path: '/albums/:id', component: AlbumDetailView },
  { path: '/genres', component: GenresView },
  { path: '/genres/:name', component: GenreDetailView },
  { path: '/playlists', component: PlaylistsView },
  { path: '/playlists/:id', component: PlaylistDetailView },
  { path: '/search', component: SearchView },
  { path: '/favorites', component: FavoritesView },
  { path: '/history', component: HistoryView },
  { path: '/stats', component: StatsView },
  { path: '/p2p', component: P2PView },
  { path: '/admin', component: AdminView2 },
  { path: '/settings', component: SettingsView },
];

const router = createRouter({
  history: createWebHashHistory(),
  routes
});

const App = {
  components: { AppSidebar, PlayerBar, LyricsPanel, ToastContainer, LoginView },
  setup() {
    const ready = ref(false);

    onMounted(async () => {
      await loadUser();
      ready.value = true;
      if (store.isAuthenticated) {
        loadOverview();
      }
      player.init();
      // 页面关闭时提交当前歌曲的 scrobble（使用 sendBeacon 确保请求发出）
      window.addEventListener('beforeunload', () => {
        flushScrobble();
      });
    });

    // Watch auth state and redirect to login if needed
    watch(() => store.isAuthenticated, (authed) => {
      if (ready.value && !authed) {
        window.location.hash = '#/login';
      }
    });

    return { store, ready };
  },
  template: `
    <div v-if="!ready" style="display:flex;align-items:center;justify-content:center;width:100%;height:100vh;">
      <div class="loading-spinner"></div>
    </div>
    <login-view v-else-if="!store.isAuthenticated" />
    <div v-else style="display:flex;width:100%;height:100vh;overflow:hidden;">
      <app-sidebar />
      <main class="main-content">
        <router-view />
      </main>
      <player-bar />
      <lyrics-panel />
      <toast-container />
    </div>
  `
};

const app = createApp(App);
app.use(router);
try {
  app.mount('#app');
  // Hide loader on successful mount
  const loader = document.getElementById('app-loader');
  if (loader) loader.classList.add('hidden');
} catch (e) {
  const errEl = document.getElementById('app-error');
  if (errEl) {
    errEl.style.display = 'block';
    errEl.textContent = '[Mount Error] ' + (e.message || e);
  }
  const loader = document.getElementById('app-loader');
  if (loader) loader.classList.add('hidden');
}
