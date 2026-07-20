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
