/**
 * CatClaw Music Server — Main App
 * Vue 3 SPA — 现代化音乐流媒体界面
 * 主导航 / 音乐库 / 个人中心 / 设置，深色主题，可折叠侧边栏
 */
import { createApp, ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { createRouter, createWebHashHistory, useRoute, useRouter } from 'vue-router';
import { store, login, logout, loadUser, loadOverview, playSong, addToQueue, toggleFavorite, isFavorite, flushScrobble, showToast, formatTime, formatDuration, setTheme } from './store.js';
import { api } from './api.js';
import { player } from './player.js';

// ════════════════════════════════════════════════════════════
// 组件
// ════════════════════════════════════════════════════════════

const AppSidebar = {
  name: 'AppSidebar',
  setup() {
    return { store };
  },
  template: `
    <aside class="sidebar" :class="{ collapsed: store.sidebarCollapsed }">
      <div class="sidebar-header">
        <router-link to="/" class="sidebar-logo">
          <span class="sidebar-logo-badge">🐱</span>
          <span class="sidebar-logo-text">Cat<em>Claw</em></span>
        </router-link>
      </div>
      <nav class="sidebar-nav">
        <div class="nav-section">
          <div class="nav-section-title">主导航</div>
          <router-link to="/" class="nav-item" active-class="active">
            <span class="nav-icon">🏠</span> <span>首页</span>
          </router-link>
          <router-link to="/search" class="nav-item" active-class="active">
            <span class="nav-icon">🔍</span> <span>搜索</span>
          </router-link>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">音乐库</div>
          <router-link to="/songs" class="nav-item" active-class="active">
            <span class="nav-icon">🎵</span> <span>歌曲</span>
          </router-link>
          <router-link to="/artists" class="nav-item" active-class="active">
            <span class="nav-icon">🎤</span> <span>艺术家</span>
          </router-link>
          <router-link to="/albums" class="nav-item" active-class="active">
            <span class="nav-icon">💿</span> <span>专辑</span>
          </router-link>
          <router-link to="/playlists" class="nav-item" active-class="active">
            <span class="nav-icon">📋</span> <span>播放列表</span>
          </router-link>
          <router-link to="/genres" class="nav-item" active-class="active">
            <span class="nav-icon">🎼</span> <span>流派</span>
          </router-link>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">个人中心</div>
          <router-link to="/favorites" class="nav-item" active-class="active">
            <span class="nav-icon">❤️</span> <span>收藏</span>
          </router-link>
          <router-link to="/history" class="nav-item" active-class="active">
            <span class="nav-icon">🕐</span> <span>历史</span>
          </router-link>
          <router-link to="/stats" class="nav-item" active-class="active">
            <span class="nav-icon">📊</span> <span>统计</span>
          </router-link>
        </div>
        <div class="nav-section" v-if="store.user?.role === 'admin'">
          <div class="nav-section-title">管理</div>
          <router-link to="/admin" class="nav-item" active-class="active">
            <span class="nav-icon">🛠️</span> <span>管理面板</span>
          </router-link>
        </div>
      </nav>
      <div class="sidebar-footer">
        <div class="sidebar-user" @click="$router.push('/settings')" title="设置">
          <div class="sidebar-user-avatar">{{ store.user?.displayName?.[0] || store.user?.username?.[0] || '?' }}</div>
          <div class="sidebar-user-info">
            <div class="sidebar-user-name">{{ store.user?.displayName || store.user?.username }}</div>
            <div class="sidebar-user-role">⚙️ 设置</div>
          </div>
        </div>
      </div>
    </aside>
  `
};

// ── 顶栏 ──
const TopBar = {
  name: 'TopBar',
  setup() {
    const kw = ref('');
    const router = useRouter();

    function doSearch() {
      if (kw.value.trim()) {
        router.push({ path: '/search', query: { q: kw.value.trim() } });
      }
    }

    return { store, kw, doSearch, setTheme };
  },
  template: `
    <header class="topbar">
      <button class="topbar-sidebar-toggle" @click="store.sidebarCollapsed = !store.sidebarCollapsed" title="折叠侧边栏">
        {{ store.sidebarCollapsed ? '☰' : '◧' }}
      </button>
      <div class="topbar-search">
        <span class="topbar-search-icon">🔍</span>
        <input :value="kw" @input="kw = $event.target.value" @keyup.enter="doSearch"
               placeholder="搜索歌曲、艺术家、专辑、播放列表..." />
      </div>
      <div class="topbar-actions">
        <button class="topbar-btn" :class="{ active: store.showLyrics }" @click="store.showLyrics = !store.showLyrics" title="歌词">📝</button>
        <button class="topbar-btn" :class="{ active: store.showQueue }" @click="store.showQueue = !store.showQueue" title="播放队列">
          🎧 <span v-if="store.queue.length" class="badge accent" style="position:absolute;top:-2px;right:-2px;padding:1px 5px;font-size:9px;">{{ store.queue.length }}</span>
        </button>
        <button class="topbar-btn" :class="{ active: store.theme !== 'dark' }" @click="store.theme === 'dark' ? setTheme('light') : setTheme('dark')" title="切换主题">
          {{ store.theme === 'dark' ? '🌙' : '☀️' }}
        </button>
      </div>
    </header>
  `
};

// ── 播放器 ──
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
    <div class="player-bar" :style="{ left: store.sidebarCollapsed ? 'var(--sidebar-w-collapsed)' : 'var(--sidebar-w)' }">
      <div class="player-left">
        <div class="player-cover" :class="{ 'playing-anim': store.isPlaying }" @click="store.nowPlayingFull = true">
          <img v-if="store.currentSong?.album_cover" :src="coverUrl(store.currentSong.id)" />
          <div v-else class="player-cover-placeholder" style="font-size:22px;">🎵</div>
        </div>
        <div class="player-info">
          <div class="player-title">{{ store.currentSong?.title || '未在播放' }}</div>
          <div class="player-artist">{{ store.currentSong?.artist || '' }}</div>
        </div>
      </div>
      <div class="player-center">
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
          <span class="player-time">{{ formatTime(store.currentTime) }}</span>
          <div class="progress-bar" @mousedown="onProgressMouseDown">
            <div class="progress-bar-fill" :style="{ width: (store.progress * 100) + '%' }"></div>
          </div>
          <span class="player-time right">{{ formatTime(store.duration) }}</span>
        </div>
      </div>
      <div class="player-right">
        <button class="btn-icon" @click="player.toggleStreamMode()" :title="store.streamMode === 'hls' ? 'HLS模式' : '直连模式'">
          <span class="player-quality" :class="{ hls: store.streamMode === 'hls' }">{{ store.streamMode === 'hls' ? 'HLS' : 'RAW' }}</span>
        </button>
        <div class="player-volume">
          <button class="btn-icon" @click="player.toggleMute()">{{ store.isMuted || store.volume === 0 ? '🔇' : store.volume < 0.5 ? '🔉' : '🔊' }}</button>
          <input type="range" class="volume-slider" min="0" max="1" step="0.01" :value="store.volume" @input="player.setVolume(parseFloat($event.target.value))" />
        </div>
      </div>
    </div>
  `
};

// ── 歌词面板 ──
const LyricsPanel = {
  name: 'LyricsPanel',
  setup() {
    const lyricsBody = ref(null);

    watch(() => store.currentLyricLine, (line) => {
      if (line >= 0 && lyricsBody.value) {
        const el = lyricsBody.value.querySelector('.lyrics-line.active');
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    });

    watch(() => store.currentTime, (time) => {
      if (!store.lyrics?.lines?.length) return;
      const timeMs = time * 1000;
      let currentLine = -1;
      for (let i = store.lyrics.lines.length - 1; i >= 0; i--) {
        if (timeMs >= store.lyrics.lines[i].startTimeMs) { currentLine = i; break; }
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
        <h3>📝 歌词</h3>
        <button class="btn-icon" @click="store.showLyrics = false">✕</button>
      </div>
      <div class="lyrics-body" ref="lyricsBody">
        <div v-if="!store.lyrics?.lines?.length" class="lyrics-empty">
          {{ store.currentSong ? '暂无歌词' : '未在播放' }}
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

// ── 播放队列面板 ──
const QueuePanel = {
  name: 'QueuePanel',
  setup() {
    function playAt(i) { player.playIndex(i); }
    return { store, playAt };
  },
  template: `
    <div class="queue-panel" :class="{ open: store.showQueue }">
      <div class="queue-header">
        <h3>🎧 播放队列</h3>
        <button class="btn-icon" @click="store.showQueue = false">✕</button>
      </div>
      <div class="queue-body">
        <div v-if="!store.queue.length" class="lyrics-empty">队列为空</div>
        <div v-for="(song, i) in store.queue" :key="i" class="queue-item" :class="{ current: i === store.queueIndex }" @click="playAt(i)">
          <span class="queue-idx">{{ i === store.queueIndex ? '▶' : i + 1 }}</span>
          <div class="flex-1" style="min-width:0;">
            <div class="queue-title">{{ song.title }}</div>
            <div class="queue-sub">{{ song.artist }}</div>
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

// ════════════════════════════════════════════════════════════
// 视图
// ════════════════════════════════════════════════════════════

// ── 首页：仪表盘 ──
const HomeView = {
  name: 'HomeView',
  setup() {
    const overview = ref(null);
    const recommend = ref([]);
    const recentSongs = ref([]);
    const topSongs = ref([]);
    const loading = ref(true);

    onMounted(async () => {
      try {
        const [ov, rec, recent, top] = await Promise.all([
          api.getOverview().catch(() => null),
          api.getDailyRecommend(10).catch(() => []),
          api.getRecentlyPlayed(10).catch(() => []),
          api.getTopPlayed(10, 30).catch(() => [])
        ]);
        overview.value = ov;
        recommend.value = rec.items || rec || [];
        recentSongs.value = recent.items || recent || [];
        topSongs.value = top.items || top || [];
      } catch {}
      loading.value = false;
    });

    function playAll(songs) {
      if (songs.length) playSong(songs[0], songs, 0);
    }

    return { overview, recommend, recentSongs, topSongs, loading, playSong, playAll, formatTime, store, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">👋 欢迎回来{{ store.user?.displayName ? '，' + store.user.displayName : '' }}</h1>
        <p class="page-subtitle">发现好音乐，享受你的私人音乐库</p>
      </div>

      <div v-if="overview" class="stats-grid">
        <div class="stat-card">
          <span class="stat-card-icon">🎵</span>
          <div class="stat-card-label">歌曲</div>
          <div class="stat-card-value accent">{{ overview.song_count || 0 }}</div>
        </div>
        <div class="stat-card">
          <span class="stat-card-icon">🎤</span>
          <div class="stat-card-label">艺术家</div>
          <div class="stat-card-value accent">{{ overview.artist_count || 0 }}</div>
        </div>
        <div class="stat-card">
          <span class="stat-card-icon">💿</span>
          <div class="stat-card-label">专辑</div>
          <div class="stat-card-value accent">{{ overview.album_count || 0 }}</div>
        </div>
        <div class="stat-card">
          <span class="stat-card-icon">📋</span>
          <div class="stat-card-label">播放列表</div>
          <div class="stat-card-value accent">{{ overview.playlist_count || 0 }}</div>
        </div>
      </div>

      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <template v-else>
        <div v-if="recommend.length" class="section">
          <div class="section-header">
            <h2 class="section-title">✨ 每日推荐</h2>
            <span class="section-link">为你精选</span>
          </div>
          <div class="card-grid">
            <div class="card" v-for="(song, i) in recommend" :key="'r' + song.id + i" @click="playSong(song, recommend, i)">
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

        <div class="section" style="display:grid;grid-template-columns:1fr 1fr;gap:24px;" v-if="topSongs.length || recentSongs.length">
          <div v-if="topSongs.length">
            <div class="section-header">
              <h2 class="section-title">🔥 热门榜单</h2>
              <span class="section-link">30 天最常播放</span>
            </div>
            <div class="rank-list" style="background:var(--bg-2);border:1px solid var(--border);border-radius:14px;padding:8px;">
              <div v-for="(s, i) in topSongs.slice(0, 8)" :key="'t' + s.id" class="rank-item" @click="playSong(s, topSongs, i)">
                <span class="rank-num">{{ i + 1 }}</span>
                <div class="rank-info">
                  <div class="rank-title">{{ s.title }}</div>
                  <div class="rank-sub">{{ s.artist }}</div>
                </div>
                <span class="rank-count">{{ formatTime(s.duration) }}</span>
              </div>
            </div>
          </div>
          <div v-if="recentSongs.length">
            <div class="section-header">
              <h2 class="section-title">🕐 最近播放</h2>
              <span class="section-link">继续收听</span>
            </div>
            <div class="rank-list" style="background:var(--bg-2);border:1px solid var(--border);border-radius:14px;padding:8px;">
              <div v-for="(song, i) in recentSongs.slice(0, 8)" :key="'c' + song.id + i" class="rank-item" @click="playSong(song, recentSongs, i)">
                <span class="rank-num">{{ i + 1 }}</span>
                <div class="rank-info">
                  <div class="rank-title">{{ song.title }}</div>
                  <div class="rank-sub">{{ song.artist }}</div>
                </div>
                <span class="rank-count">{{ formatTime(song.duration) }}</span>
              </div>
            </div>
          </div>
        </div>
      </template>
    </div>
  `
};

// ── 歌曲列表（排序/筛选/批量操作）──
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
    const genres = ref([]);
    const genreFilter = ref('');
    const selected = ref(new Set());
    const showBatch = ref(false);
    const showAddPlaylist = ref(false);
    const playlists = ref([]);
    const targetPlaylist = ref('');
    const allChecked = ref(false);

    async function loadSongs() {
      loading.value = true;
      try {
        const data = await api.getSongs({
          page: page.value, page_size: pageSize.value,
          sort: sortBy.value, order: sortOrder.value,
          genre: genreFilter.value
        });
        songs.value = data.items || [];
        total.value = data.total || 0;
      } catch (e) { showToast('加载失败: ' + e.message, 'error'); }
      loading.value = false;
    }

    async function loadGenres() {
      try {
        const data = await api.getGenres();
        genres.value = (data.items || []).map(g => g.name || g.Name).filter(Boolean);
      } catch {}
    }

    async function loadPlaylists() {
      try {
        const data = await api.getPlaylists({ page: 1, page_size: 100 });
        playlists.value = data.items || [];
      } catch {}
    }

    function onPage(p) { page.value = p; loadSongs(); }
    function onSort(field) {
      if (sortBy.value === field) sortOrder.value = sortOrder.value === 'asc' ? 'desc' : 'asc';
      else { sortBy.value = field; sortOrder.value = 'asc'; }
      loadSongs();
    }
    function onGenre(g) { genreFilter.value = genreFilter.value === g ? '' : g; page.value = 1; loadSongs(); }

    function toggleSelect(id) {
      const s = new Set(selected.value);
      if (s.has(id)) s.delete(id); else s.add(id);
      selected.value = s;
      showBatch.value = s.size > 0;
    }
    function toggleAll() {
      if (allChecked.value) { selected.value = new Set(); allChecked.value = false; }
      else { selected.value = new Set(songs.value.map(s => s.id)); allChecked.value = true; }
      showBatch.value = selected.value.size > 0;
    }
    function clearSelect() { selected.value = new Set(); showBatch.value = false; allChecked.value = false; }

    async function batchFavorite() {
      try {
        for (const id of selected.value) { if (!isFavorite(id)) await api.addFavorite(id); }
        showToast(`已收藏 ${selected.value.size} 首歌曲`, 'success');
        clearSelect();
      } catch (e) { showToast('批量收藏失败: ' + e.message, 'error'); }
    }

    async function batchAddToPlaylist() {
      if (!targetPlaylist.value) { showToast('请选择播放列表', 'error'); return; }
      try {
        await api.addSongsToPlaylist(targetPlaylist.value, [...selected.value]);
        showToast(`已添加到播放列表`, 'success');
        showAddPlaylist.value = false;
        clearSelect();
      } catch (e) { showToast('添加失败: ' + e.message, 'error'); }
    }

    const totalPages = computed(() => Math.ceil(total.value / pageSize.value));
    const selectedList = computed(() => songs.value.filter(s => selected.value.has(s.id)));

    onMounted(() => { loadSongs(); loadGenres(); loadPlaylists(); });

    return { songs, total, page, pageSize, totalPages, loading, sortBy, sortOrder, onPage, onSort,
      playSong, formatTime, store, isFavorite, toggleFavorite, genres, genreFilter, onGenre,
      selected, showBatch, toggleSelect, toggleAll, allChecked, clearSelect, batchFavorite,
      showAddPlaylist, playlists, targetPlaylist, batchAddToPlaylist, selectedList };
  },
  template: `
    <div class="page-content">
      <div class="page-header page-header-row">
        <div>
          <h1 class="page-title">🎵 歌曲</h1>
          <p class="page-subtitle">共 {{ total }} 首</p>
        </div>
        <div class="flex gap-8">
          <button class="btn btn-secondary btn-sm" @click="showBatch = !showBatch">批量操作</button>
        </div>
      </div>

      <div class="filter-bar">
        <span class="muted" style="font-size:12.5px;">流派:</span>
        <button class="chip" :class="{ active: genreFilter === '' }" @click="onGenre('')">全部</button>
        <button v-for="g in genres.slice(0, 12)" :key="g" class="chip" :class="{ active: genreFilter === g }" @click="onGenre(g)">{{ g }}</button>
      </div>

      <div class="song-list">
        <div class="song-list-header">
          <span v-if="showBatch">
            <span class="checkbox" :class="{ checked: allChecked }" @click="toggleAll">{{ allChecked ? '✓' : '' }}</span>
          </span>
          <span v-else>#</span>
          <span style="cursor:pointer" @click="onSort('title')">标题 {{ sortBy === 'title' ? (sortOrder === 'asc' ? '↑' : '↓') : '' }}</span>
          <span>艺术家</span>
          <span>专辑</span>
          <span style="cursor:pointer" @click="onSort('year')">年份 {{ sortBy === 'year' ? (sortOrder === 'asc' ? '↑' : '↓') : '' }}</span>
          <span style="cursor:pointer;text-align:right" @click="onSort('duration')">时长 {{ sortBy === 'duration' ? (sortOrder === 'asc' ? '↑' : '↓') : '' }}</span>
          <span></span>
        </div>
        <div v-for="(song, i) in songs" :key="song.id" class="song-row"
             :class="{ playing: store.currentSong?.id === song.id }"
             @click="playSong(song, songs, i)">
          <span v-if="showBatch" @click.stop>
            <span class="checkbox" :class="{ checked: selected.has(song.id) }" @click.stop="toggleSelect(song.id)">{{ selected.has(song.id) ? '✓' : '' }}</span>
          </span>
          <span v-else class="song-idx">{{ (page - 1) * pageSize + i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ song.title }}</div>
            <div class="song-artist-sub">{{ song.artist }}</div>
          </div>
          <span class="song-album">{{ song.album }}</span>
          <span class="song-genre">{{ song.genre || '—' }}</span>
          <span class="song-duration">{{ formatTime(song.duration) }}</span>
          <div class="song-actions" @click.stop>
            <button class="btn-icon" style="font-size:14px;" @click="toggleFavorite(song.id)" :class="{ active: isFavorite(song.id) }">
              {{ isFavorite(song.id) ? '❤️' : '🤍' }}
            </button>
          </div>
        </div>
      </div>

      <div class="pagination" v-if="totalPages > 1">
        <button class="btn btn-secondary btn-sm" :disabled="page <= 1" @click="onPage(page - 1)">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button class="btn btn-secondary btn-sm" :disabled="page >= totalPages" @click="onPage(page + 1)">下一页</button>
      </div>

      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>

      <!-- 批量操作栏 -->
      <div v-if="showBatch" class="batch-bar">
        <span class="batch-bar-info">已选 {{ selected.size }} 首</span>
        <div class="batch-bar-actions">
          <button class="btn btn-secondary btn-sm" @click="batchFavorite">❤️ 批量收藏</button>
          <button class="btn btn-primary btn-sm" @click="showAddPlaylist = true">📋 加入播放列表</button>
          <button class="btn btn-secondary btn-sm" @click="clearSelect">取消</button>
        </div>
      </div>

      <!-- 加入播放列表弹窗 -->
      <div v-if="showAddPlaylist" class="modal-overlay" @click.self="showAddPlaylist = false">
        <div class="modal">
          <h3>加入播放列表</h3>
          <div class="form-group">
            <label>选择播放列表（{{ selected.size }} 首歌曲）</label>
            <select class="form-control" v-model="targetPlaylist">
              <option value="">-- 选择 --</option>
              <option v-for="p in playlists" :key="p.id" :value="p.id">{{ p.name }}</option>
            </select>
          </div>
          <div class="modal-actions">
            <button class="btn btn-secondary" @click="showAddPlaylist = false">取消</button>
            <button class="btn btn-primary" @click="batchAddToPlaylist">添加</button>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 艺术家网格 ──
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
    function onPage(p) { page.value = p; load(); }
    onMounted(load);

    return { artists, total, page, totalPages, loading, onPage, artistCoverUrl: (id) => api.getArtistCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">🎤 艺术家</h1>
        <p class="page-subtitle">共 {{ total }} 位艺术家</p>
      </div>
      <div class="card-grid wide">
        <router-link v-for="a in artists" :key="a.id" :to="'/artists/' + a.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover" style="border-radius:14px 14px 0 0;">
            <img v-if="a.cover" :src="artistCoverUrl(a.id)" />
            <div v-else class="card-cover-placeholder">🎤</div>
          </div>
          <div class="card-body">
            <div class="card-title">{{ a.name }}</div>
            <div class="card-subtitle">{{ a.song_count || 0 }} 首歌曲</div>
            <div v-if="a.description" class="card-subtitle" style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">{{ a.description }}</div>
          </div>
        </router-link>
      </div>
      <div class="pagination" v-if="totalPages > 1">
        <button class="btn btn-secondary btn-sm" :disabled="page <= 1" @click="onPage(page - 1)">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button class="btn btn-secondary btn-sm" :disabled="page >= totalPages" @click="onPage(page + 1)">下一页</button>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
    </div>
  `
};

// ── 专辑封面墙（排序）──
const AlbumsView = {
  name: 'AlbumsView',
  setup() {
    const albums = ref([]);
    const total = ref(0);
    const page = ref(1);
    const loading = ref(false);
    const pageSize = 48;
    const sort = ref('name'); // name | year

    async function load() {
      loading.value = true;
      try {
        const data = await api.getAlbums({ page: page.value, page_size: pageSize, sort: sort.value });
        albums.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    }

    const totalPages = computed(() => Math.ceil(total.value / pageSize));
    function onPage(p) { page.value = p; load(); }
    function onSort(s) { sort.value = s; page.value = 1; load(); }
    onMounted(load);

    return { albums, total, page, totalPages, loading, sort, onPage, onSort, albumCoverUrl: (id) => api.getAlbumCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header page-header-row">
        <div>
          <h1 class="page-title">💿 专辑</h1>
          <p class="page-subtitle">共 {{ total }} 张专辑</p>
        </div>
        <div class="flex gap-8">
          <button class="chip" :class="{ active: sort === 'name' }" @click="onSort('name')">按字母</button>
          <button class="chip" :class="{ active: sort === 'year' }" @click="onSort('year')">按年份</button>
        </div>
      </div>
      <div class="card-grid">
        <router-link v-for="a in albums" :key="a.id" :to="'/albums/' + a.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover">
            <img v-if="a.cover" :src="albumCoverUrl(a.id)" />
            <div v-else class="card-cover-placeholder">💿</div>
            <div class="card-cover-overlay"><div class="card-play-btn">▶</div></div>
          </div>
          <div class="card-body">
            <div class="card-title">{{ a.title }}</div>
            <div class="card-subtitle">{{ a.artist }} · {{ a.song_count || 0 }} 首</div>
          </div>
        </router-link>
      </div>
      <div class="pagination" v-if="totalPages > 1">
        <button class="btn btn-secondary btn-sm" :disabled="page <= 1" @click="onPage(page - 1)">上一页</button>
        <span class="page-info">{{ page }} / {{ totalPages }}</span>
        <button class="btn btn-secondary btn-sm" :disabled="page >= totalPages" @click="onPage(page + 1)">下一页</button>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
    </div>
  `
};

// ── 专辑详情 ──
const AlbumDetailView = {
  name: 'AlbumDetailView',
  setup() {
    const route = useRoute();
    const album = ref(null);
    const loading = ref(true);

    onMounted(async () => {
      try {
        album.value = await api.getAlbum(route.params.id);
      } catch {}
      loading.value = false;
    });

    return { album, loading, playSong, formatTime, store, albumCoverUrl: (id) => api.getAlbumCoverUrl(id), toggleFavorite, isFavorite };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="album">
        <div class="detail-hero">
          <div class="detail-cover">
            <img v-if="album.cover" :src="albumCoverUrl(album.id)" />
            <div v-else style="font-size:64px;opacity:0.4;">💿</div>
          </div>
          <div class="detail-info">
            <div class="detail-type">专辑</div>
            <h1 class="detail-title">{{ album.title }}</h1>
            <div class="detail-sub">{{ album.artist }} · {{ album.songs?.length || 0 }} 首歌曲</div>
            <div class="detail-actions">
              <button class="btn btn-primary" @click="playSong(album.songs[0], album.songs, 0)" v-if="album.songs?.length">
                ▶ 播放全部
              </button>
            </div>
          </div>
        </div>
        <div class="song-list" v-if="album.songs?.length">
          <div class="song-list-header cols-6" style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;">
            <span>#</span><span>标题</span><span>艺术家</span><span style="text-align:right;">时长</span><span></span>
          </div>
          <div v-for="(song, i) in album.songs" :key="song.id" class="song-row"
               :class="{ playing: store.currentSong?.id === song.id }"
               style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;"
               @click="playSong(song, album.songs, i)">
            <span class="song-idx">{{ song.track_number || i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-genre">{{ song.genre || '—' }}</span>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
            <div class="song-actions" @click.stop>
              <button class="btn-icon" @click="toggleFavorite(song.id)" :class="{ active: isFavorite(song.id) }">{{ isFavorite(song.id) ? '❤️' : '🤍' }}</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 艺术家详情 ──
const ArtistDetailView = {
  name: 'ArtistDetailView',
  setup() {
    const route = useRoute();
    const artist = ref(null);
    const songs = ref([]);
    const albums = ref([]);
    const loading = ref(true);

    onMounted(async () => {
      try {
        const [a, s, al] = await Promise.all([
          api.getArtist(route.params.id),
          api.getArtistSongs(route.params.id, { page: 1, page_size: 200 }),
          api.getArtistAlbums(route.params.id)
        ]);
        artist.value = a;
        songs.value = s.items || [];
        albums.value = al.items || [];
      } catch {}
      loading.value = false;
    });

    return { artist, songs, albums, loading, playSong, formatTime, store, artistCoverUrl: (id) => api.getArtistCoverUrl(id), albumCoverUrl: (id) => api.getAlbumCoverUrl(id), toggleFavorite, isFavorite };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="artist">
        <div class="detail-hero">
          <div class="detail-cover round">
            <img v-if="artist.cover" :src="artistCoverUrl(artist.id)" />
            <div v-else style="font-size:56px;opacity:0.4;">🎤</div>
          </div>
          <div class="detail-info">
            <div class="detail-type">艺术家</div>
            <h1 class="detail-title">{{ artist.name }}</h1>
            <div class="detail-sub">
              {{ artist.song_count || 0 }} 首歌曲 · {{ artist.album_count || albums.length || 0 }} 张专辑
              <span v-if="artist.gender"> · {{ artist.gender }}</span>
              <span v-if="artist.region"> · {{ artist.region }}</span>
            </div>
            <div v-if="artist.description" class="detail-desc">{{ artist.description }}</div>
            <div class="detail-actions">
              <button class="btn btn-primary" @click="playSong(songs[0], songs, 0)" v-if="songs.length">▶ 播放热门</button>
            </div>
          </div>
        </div>

        <div v-if="albums.length" class="section">
          <div class="section-header"><h2 class="section-title">💿 专辑作品</h2></div>
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

        <div class="section">
          <div class="section-header"><h2 class="section-title">🎵 全部歌曲</h2></div>
          <div class="song-list">
            <div class="song-list-header cols-6" style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;">
              <span>#</span><span>标题</span><span>专辑</span><span style="text-align:right;">时长</span><span></span>
            </div>
            <div v-for="(song, i) in songs" :key="song.id" class="song-row"
                 :class="{ playing: store.currentSong?.id === song.id }"
                 style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;"
                 @click="playSong(song, songs, i)">
              <span class="song-idx">{{ i + 1 }}</span>
              <div class="song-info"><div class="song-title">{{ song.title }}</div></div>
              <span class="song-album">{{ song.album }}</span>
              <span class="song-duration">{{ formatTime(song.duration) }}</span>
              <div class="song-actions" @click.stop>
                <button class="btn-icon" @click="toggleFavorite(song.id)" :class="{ active: isFavorite(song.id) }">{{ isFavorite(song.id) ? '❤️' : '🤍' }}</button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 搜索（分类 tab）──
const SearchView = {
  name: 'SearchView',
  setup() {
    const route = useRoute();
    const query = ref(route.query.q || '');
    const activeTab = ref('all'); // all | songs | artists | albums | playlists
    const results = ref({ songs: [], artists: [], albums: [], playlists: [] });
    const loading = ref(false);
    let debounceTimer = null;

    async function doSearch() {
      const q = query.value.trim();
      if (!q) { results.value = { songs: [], artists: [], albums: [], playlists: [] }; return; }
      loading.value = true;
      try {
        const data = await api.search(q);
        results.value = {
          songs: data.songs || [],
          artists: data.artists || [],
          albums: data.albums || [],
          playlists: []
        };
        // 播放列表在前端过滤
        try {
          const pl = await api.getPlaylists({ page: 1, page_size: 100 });
          const all = pl.items || [];
          results.value.playlists = all.filter(p => (p.name || '').toLowerCase().includes(q.toLowerCase()));
        } catch {}
      } catch {}
      loading.value = false;
    }

    function onInput(e) {
      query.value = e.target.value;
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(doSearch, 300);
    }

    onMounted(() => { if (query.value) doSearch(); });

    const count = computed(() =>
      results.value.songs.length + results.value.artists.length +
      results.value.albums.length + results.value.playlists.length);

    return { query, activeTab, results, loading, count, onInput, doSearch, playSong, formatTime, store, coverUrl: (id) => api.getCoverUrl(id), albumCoverUrl: (id) => api.getAlbumCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">🔍 搜索</h1>
        <p class="page-subtitle">找到你的音乐</p>
      </div>
      <div style="margin-bottom:24px;max-width:560px;">
        <div class="topbar-search" style="max-width:100%;height:46px;padding:0 16px;">
          <span>🔍</span>
          <input type="text" :value="query" @input="onInput" placeholder="搜索歌曲、艺术家、专辑、播放列表..." style="font-size:15px;" />
        </div>
      </div>

      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>

      <template v-else-if="query.trim()">
        <div class="tabs">
          <button class="tab" :class="{ active: activeTab === 'all' }" @click="activeTab = 'all'">全部 ({{ count }})</button>
          <button class="tab" :class="{ active: activeTab === 'songs' }" @click="activeTab = 'songs'">歌曲 ({{ results.songs.length }})</button>
          <button class="tab" :class="{ active: activeTab === 'artists' }" @click="activeTab = 'artists'">艺术家 ({{ results.artists.length }})</button>
          <button class="tab" :class="{ active: activeTab === 'albums' }" @click="activeTab = 'albums'">专辑 ({{ results.albums.length }})</button>
          <button class="tab" :class="{ active: activeTab === 'playlists' }" @click="activeTab = 'playlists'">播放列表 ({{ results.playlists.length }})</button>
        </div>

        <div v-if="!count" class="empty-state">
          <div class="empty-state-icon">🔍</div>
          <p class="empty-state-text">未找到「{{ query }}」</p>
          <p class="empty-state-hint">换个关键词试试</p>
        </div>

        <!-- 艺术家 -->
        <div v-if="(activeTab === 'all' || activeTab === 'artists') && results.artists?.length" class="section">
          <div class="section-header"><h2 class="section-title">🎤 艺术家</h2></div>
          <div class="card-grid">
            <router-link v-for="a in results.artists" :key="a.id" :to="'/artists/' + a.id" class="card" style="text-decoration:none;color:inherit;">
              <div class="card-cover"><div class="card-cover-placeholder">🎤</div></div>
              <div class="card-body"><div class="card-title">{{ a.name }}</div></div>
            </router-link>
          </div>
        </div>

        <!-- 专辑 -->
        <div v-if="(activeTab === 'all' || activeTab === 'albums') && results.albums?.length" class="section">
          <div class="section-header"><h2 class="section-title">💿 专辑</h2></div>
          <div class="card-grid">
            <router-link v-for="a in results.albums" :key="a.id" :to="'/albums/' + a.id" class="card" style="text-decoration:none;color:inherit;">
              <div class="card-cover">
                <img v-if="a.cover" :src="albumCoverUrl(a.id)" />
                <div v-else class="card-cover-placeholder">💿</div>
              </div>
              <div class="card-body"><div class="card-title">{{ a.title }}</div><div class="card-subtitle">{{ a.artist }}</div></div>
            </router-link>
          </div>
        </div>

        <!-- 播放列表 -->
        <div v-if="(activeTab === 'all' || activeTab === 'playlists') && results.playlists?.length" class="section">
          <div class="section-header"><h2 class="section-title">📋 播放列表</h2></div>
          <div class="card-grid">
            <router-link v-for="p in results.playlists" :key="p.id" :to="'/playlists/' + p.id" class="card" style="text-decoration:none;color:inherit;">
              <div class="card-cover"><div class="card-cover-placeholder">📋</div></div>
              <div class="card-body"><div class="card-title">{{ p.name }}</div><div class="card-subtitle">{{ p.song_count || 0 }} 首</div></div>
            </router-link>
          </div>
        </div>

        <!-- 歌曲 -->
        <div v-if="(activeTab === 'all' || activeTab === 'songs') && results.songs?.length" class="section">
          <div class="section-header"><h2 class="section-title">🎵 歌曲</h2></div>
          <div class="song-list">
            <div v-for="(song, i) in results.songs" :key="song.id" class="song-row" @click="playSong(song, results.songs, i)">
              <span class="song-idx">{{ i + 1 }}</span>
              <div class="song-info">
                <div class="song-title">{{ song.title }}</div>
                <div class="song-artist-sub">{{ song.artist }}</div>
              </div>
              <span class="song-album">{{ song.album }}</span>
              <span class="song-genre">{{ song.genre || '—' }}</span>
              <span class="song-duration">{{ formatTime(song.duration) }}</span>
              <span></span>
            </div>
          </div>
        </div>
      </template>
    </div>
  `
};

// ── 播放列表 ──
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
      <div class="page-header page-header-row">
        <div>
          <h1 class="page-title">📋 播放列表</h1>
          <p class="page-subtitle">{{ playlists.length }} 个列表</p>
        </div>
        <button class="btn btn-primary" @click="showCreate = true">+ 新建</button>
      </div>

      <div v-if="!playlists.length" class="empty-state">
        <div class="empty-state-icon">📋</div>
        <p class="empty-state-text">还没有播放列表</p>
        <p class="empty-state-hint">创建你的第一个播放列表</p>
      </div>

      <div class="card-grid">
        <router-link v-for="p in playlists" :key="p.id" :to="'/playlists/' + p.id" class="card" style="text-decoration:none;color:inherit;">
          <div class="card-cover">
            <div class="card-cover-placeholder" style="background:linear-gradient(145deg, var(--accent-soft), var(--bg-3));font-size:38px;">📋</div>
            <div class="card-cover-overlay"><div class="card-play-btn">▶</div></div>
          </div>
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
            <input type="text" class="form-control" v-model="newName" placeholder="播放列表名称" />
          </div>
          <div class="form-group">
            <label>描述（可选）</label>
            <textarea class="form-control" v-model="newDesc" placeholder="描述..."></textarea>
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

// ── 播放列表详情 ──
const PlaylistDetailView = {
  name: 'PlaylistDetailView',
  setup() {
    const route = useRoute();
    const playlist = ref(null);
    const loading = ref(true);
    const showAdd = ref(false);
    const searchQ = ref('');
    const searchResults = ref([]);
    const isOwner = ref(false);

    async function load() {
      try {
        playlist.value = await api.getPlaylist(route.params.id);
        isOwner.value = playlist.value?.owner_id === store.user?.id ||
                        playlist.value?.is_owner === true ||
                        playlist.value?.owner === store.user?.username;
      } catch {}
      loading.value = false;
    }

    async function removeSong(songId) {
      try {
        await api.removeSongFromPlaylist(route.params.id, songId);
        await load();
        showToast('已移除', 'success');
      } catch (e) { showToast('移除失败: ' + e.message, 'error'); }
    }

    async function doSearch() {
      if (!searchQ.value.trim()) { searchResults.value = []; return; }
      try {
        const data = await api.search(searchQ.value.trim());
        searchResults.value = data.songs || [];
      } catch {}
    }

    async function addSong(songId) {
      try {
        await api.addSongsToPlaylist(route.params.id, [songId]);
        showToast('已添加', 'success');
        await load();
      } catch (e) { showToast('添加失败: ' + e.message, 'error'); }
    }

    onMounted(load);

    return { playlist, loading, playSong, formatTime, removeSong, store, showAdd, searchQ, searchResults, doSearch, addSong };
  },
  template: `
    <div class="page-content">
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="playlist">
        <div class="detail-hero">
          <div class="detail-cover">
            <div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;font-size:64px;background:linear-gradient(145deg, var(--accent-soft), var(--bg-3));">📋</div>
          </div>
          <div class="detail-info">
            <div class="detail-type">播放列表</div>
            <h1 class="detail-title">{{ playlist.name }}</h1>
            <div class="detail-sub">{{ playlist.songs?.length || 0 }} 首歌曲</div>
            <div v-if="playlist.description" class="detail-desc">{{ playlist.description }}</div>
            <div class="detail-actions">
              <button class="btn btn-primary" @click="playSong(playlist.songs[0], playlist.songs, 0)" v-if="playlist.songs?.length">
                ▶ 播放全部
              </button>
              <button class="btn btn-secondary" @click="showAdd = true">+ 添加歌曲</button>
            </div>
          </div>
        </div>

        <div class="song-list" v-if="playlist.songs?.length">
          <div class="song-list-header cols-6" style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;">
            <span>#</span><span>标题</span><span>专辑</span><span style="text-align:right;">时长</span><span></span>
          </div>
          <div v-for="(song, i) in playlist.songs" :key="song.id" class="song-row"
               :class="{ playing: store.currentSong?.id === song.id }"
               style="grid-template-columns:36px 1.6fr 1.2fr 0.8fr 80px 44px;"
               @click="playSong(song, playlist.songs, i)">
            <span class="song-idx">{{ i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-album">{{ song.album }}</span>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
            <div class="song-actions" @click.stop>
              <button class="btn-icon" @click="removeSong(song.id)" title="移除" style="color:var(--danger);">✕</button>
            </div>
          </div>
        </div>
        <div v-else class="empty-state">
          <div class="empty-state-icon">🎵</div>
          <p class="empty-state-text">列表为空</p>
          <p class="empty-state-hint">点击「添加歌曲」搜索并添加</p>
        </div>
      </div>

      <!-- 添加歌曲弹窗 -->
      <div v-if="showAdd" class="modal-overlay" @click.self="showAdd = false">
        <div class="modal" style="max-width:560px;">
          <h3>添加歌曲</h3>
          <div class="form-group">
            <input type="text" class="form-control" v-model="searchQ" @input="doSearch" placeholder="搜索歌曲..." />
          </div>
          <div style="max-height:320px;overflow-y:auto;">
            <div v-for="song in searchResults" :key="song.id"
                 style="display:flex;align-items:center;gap:10px;padding:8px;border-radius:8px;cursor:pointer;"
                 @mouseenter="$event.currentTarget.style.background='var(--bg-3)'"
                 @mouseleave="$event.currentTarget.style.background=''">
              <div class="flex-1" style="min-width:0;">
                <div style="font-size:13.5px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">{{ song.title }}</div>
                <div style="font-size:12px;color:var(--text-2);">{{ song.artist }} · {{ song.album }}</div>
              </div>
              <button class="btn btn-primary btn-sm" @click="addSong(song.id)">添加</button>
            </div>
            <div v-if="searchQ && !searchResults.length" class="muted text-center" style="padding:20px;">无结果</div>
          </div>
          <div class="modal-actions">
            <button class="btn btn-secondary" @click="showAdd = false">关闭</button>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 流派 ──
const GenresView = {
  name: 'GenresView',
  setup() {
    const genres = ref([]);
    const loading = ref(true);
    const palette = ['#7c8cff', '#22d3ee', '#f472b6', '#fb923c', '#4ade80', '#facc15', '#a78bfa', '#38bdf8', '#f87171', '#34d399', '#e879f9', '#fbbf24'];

    onMounted(async () => {
      try {
        const data = await api.getGenres();
        genres.value = (data.items || []).map((g, i) => ({
          name: g.name || g.Name,
          songCount: g.songCount ?? g.SongCount ?? 0,
          albumCount: g.albumCount ?? g.AlbumCount ?? 0,
          color: palette[i % palette.length]
        }));
      } catch {}
      loading.value = false;
    });

    return { genres, loading };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">🎼 流派</h1>
        <p class="page-subtitle">按音乐流派分类浏览</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!genres.length" class="empty-state">
        <div class="empty-state-icon">🎼</div>
        <p class="empty-state-text">暂无流派信息</p>
        <p class="empty-state-hint">扫描音乐库后将自动提取流派标签</p>
      </div>
      <div v-else class="genre-grid">
        <router-link v-for="g in genres" :key="g.name" :to="'/genres/' + encodeURIComponent(g.name)"
          class="genre-card" :style="{ background: 'linear-gradient(145deg, ' + g.color + 'CC, ' + g.color + '44)' }">
          <div class="genre-card-name">{{ g.name }}</div>
          <div class="genre-card-count">{{ g.songCount }} 首 · {{ g.albumCount }} 张专辑</div>
        </router-link>
      </div>
    </div>
  `
};

// ── 流派详情 ──
const GenreDetailView = {
  name: 'GenreDetailView',
  setup() {
    const route = useRoute();
    const genreName = ref('');
    const songs = ref([]);
    const total = ref(0);
    const loading = ref(true);

    onMounted(async () => {
      genreName.value = route.params.name;
      try {
        const data = await api.getGenreSongs(route.params.name, { page: 1, page_size: 200 });
        songs.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    });

    return { genreName, songs, total, loading, playSong, formatTime, store, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <router-link to="/genres" style="font-size:13px;color:var(--accent);text-decoration:none;">← 返回流派列表</router-link>
        <h1 class="page-title" style="margin-top:8px;">🎼 {{ genreName }}</h1>
        <p class="page-subtitle">{{ total }} 首歌曲</p>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!songs.length" class="empty-state"><p class="empty-state-text">该流派暂无歌曲</p></div>
      <div v-else class="song-list">
        <div v-for="(song, i) in songs" :key="song.id" class="song-row"
             :class="{ playing: store.currentSong?.id === song.id }"
             @click="playSong(song, songs, i)">
          <span class="song-idx">{{ i + 1 }}</span>
          <div class="song-info">
            <div class="song-title">{{ song.title }}</div>
            <div class="song-artist-sub">{{ song.artist }}</div>
          </div>
          <span class="song-album">{{ song.album }}</span>
          <span class="song-genre">{{ song.genre || genreName }}</span>
          <span class="song-duration">{{ formatTime(song.duration) }}</span>
          <span></span>
        </div>
      </div>
    </div>
  `
};

// ── 收藏（汇总视图）──
const FavoritesView = {
  name: 'FavoritesView',
  setup() {
    const tab = ref('songs'); // songs | albums | artists
    const songs = ref([]);
    const total = ref(0);
    const loading = ref(true);

    async function load() {
      loading.value = true;
      try {
        const data = await api.getFavorites({ page: 1, page_size: 200 });
        songs.value = data.items || [];
        total.value = data.total || 0;
      } catch {}
      loading.value = false;
    }

    onMounted(load);

    return { tab, songs, total, loading, playSong, formatTime, store, toggleFavorite, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">❤️ 收藏</h1>
        <p class="page-subtitle">你的音乐收藏</p>
      </div>
      <div class="tabs">
        <button class="tab" :class="{ active: tab === 'songs' }" @click="tab = 'songs'">歌曲 ({{ songs.length }})</button>
        <button class="tab" :class="{ active: tab === 'albums' }" @click="tab = 'albums'">专辑</button>
        <button class="tab" :class="{ active: tab === 'artists' }" @click="tab = 'artists'">艺术家</button>
      </div>

      <div v-if="tab === 'songs'">
        <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
        <div v-else-if="!songs.length" class="empty-state">
          <div class="empty-state-icon">❤️</div>
          <p class="empty-state-text">还没有收藏</p>
          <p class="empty-state-hint">在播放时点击 ❤️ 来收藏歌曲</p>
        </div>
        <div v-else class="song-list">
          <div v-for="(song, i) in songs" :key="song.id" class="song-row"
               :class="{ playing: store.currentSong?.id === song.id }"
               @click="playSong(song, songs, i)">
            <span class="song-idx">{{ i + 1 }}</span>
            <div class="song-info">
              <div class="song-title">{{ song.title }}</div>
              <div class="song-artist-sub">{{ song.artist }}</div>
            </div>
            <span class="song-album">{{ song.album }}</span>
            <span class="song-genre">{{ song.genre || '—' }}</span>
            <span class="song-duration">{{ formatTime(song.duration) }}</span>
            <div class="song-actions" @click.stop>
              <button class="btn-icon" @click="toggleFavorite(song.id)" style="color:var(--danger);">❤️</button>
            </div>
          </div>
        </div>
      </div>
      <div v-else class="empty-state">
        <div class="empty-state-icon">💿</div>
        <p class="empty-state-text">{{ tab === 'albums' ? '专辑收藏' : '艺术家收藏' }}即将上线</p>
        <p class="empty-state-hint">目前支持收藏歌曲</p>
      </div>
    </div>
  `
};

// ── 历史（按日期分组时间线）──
const HistoryView = {
  name: 'HistoryView',
  setup() {
    const groups = ref([]);
    const loading = ref(true);
    const page = ref(1);
    const pageSize = 100;
    const total = ref(0);

    function groupByDate(items) {
      const map = new Map();
      for (const item of items) {
        const d = new Date(item.timestamp);
        if (isNaN(d)) continue;
        const key = d.toLocaleDateString('zh-CN', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' });
        if (!map.has(key)) map.set(key, []);
        map.get(key).push({ ...item, _date: d });
      }
      return Array.from(map.entries()).map(([date, list]) => ({ date, items: list }));
    }

    async function load() {
      loading.value = true;
      try {
        const data = await api.getHistory({ page: page.value, page_size: pageSize });
        total.value = data.total || 0;
        const items = data.items || [];
        groups.value = groupByDate(items);
      } catch {}
      loading.value = false;
    }

    function formatClock(d) {
      return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
    }

    function formatTime2(ms) { return formatTime(ms / 1000); }

    function playItem(item, groupItems) {
      const songs = groupItems.map(it => ({ id: it.song_id, title: it.title, artist: it.artist, album_cover: it.album_cover }));
      const idx = groupItems.indexOf(item);
      playSong(songs[idx], songs, idx);
    }

    function loadMore() {
      page.value++;
      load();
    }

    onMounted(load);

    return { groups, loading, total, page, pageSize, formatClock, formatTime2, playItem, store, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">🕐 播放历史</h1>
        <p class="page-subtitle">共 {{ total }} 条记录</p>
      </div>
      <div v-if="loading && !groups.length" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="!groups.length" class="empty-state">
        <div class="empty-state-icon">🕐</div>
        <p class="empty-state-text">暂无播放记录</p>
        <p class="empty-state-hint">播放歌曲后将自动记录</p>
      </div>
      <div v-else class="timeline">
        <div v-for="group in groups" :key="group.date" class="timeline-group">
          <div class="timeline-date">{{ group.date }}</div>
          <div v-for="item in group.items" :key="item.timestamp" class="timeline-item"
               @click="playItem(item, group.items)">
            <span class="timeline-time">{{ formatClock(item._date) }}</span>
            <div class="timeline-cover">
              <img v-if="item.album_cover" :src="coverUrl(item.song_id)" />
              <span v-else>🎵</span>
            </div>
            <div class="timeline-info">
              <div style="font-size:13.5px;font-weight:600;">{{ item.title }}</div>
              <div style="font-size:12px;color:var(--text-2);">{{ item.artist }}</div>
            </div>
            <span class="timeline-dur">{{ formatTime2(item.duration_played_ms) }}</span>
          </div>
        </div>
        <div style="text-align:center;margin-top:12px;" v-if="groups.length && page * pageSize < total">
          <button class="btn btn-secondary btn-sm" @click="loadMore">加载更多</button>
        </div>
      </div>
    </div>
  `
};

// ── 统计（数据可视化）──
const StatsView = {
  name: 'StatsView',
  setup() {
    const stats = ref(null);
    const loading = ref(true);
    const days = ref(30);

    async function load() {
      loading.value = true;
      try {
        stats.value = await api.getStats(days.value);
      } catch {}
      loading.value = false;
    }

    onMounted(load);

    // 听歌趋势（dailyPlays）
    const trend = computed(() => {
      if (!stats.value?.dailyPlays?.length) return [];
      const max = Math.max(...stats.value.dailyPlays.map(d => d.count), 1);
      return stats.value.dailyPlays.map(d => ({
        label: new Date(d.date).toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' }),
        value: d.count,
        pct: (d.count / max) * 100
      }));
    });

    // 时段分布（hourlyDistribution）
    const hourly = computed(() => {
      if (!stats.value?.hourlyDistribution?.length) return [];
      const max = Math.max(...stats.value.hourlyDistribution.map(d => d.count), 1);
      return stats.value.hourlyDistribution.map(d => ({
        label: d.hour + '时',
        value: d.count,
        pct: (d.count / max) * 100
      }));
    });

    // 曲风分布（genreBreakdown）
    const genres = computed(() => {
      if (!stats.value?.genreBreakdown?.length) return [];
      const max = Math.max(...stats.value.genreBreakdown.map(d => d.count), 1);
      return stats.value.genreBreakdown.map(d => ({ name: d.genre || '未知', count: d.count, pct: (d.count / max) * 100 }));
    });

    // 时长显示
    const hours = computed(() => {
      const h = stats.value?.totalDurationHours || 0;
      const whole = Math.floor(h);
      const min = Math.round((h - whole) * 60);
      return { whole, min };
    });

    function playTopSong(s) {
      playSong({ id: s.song_id || s.songId, title: s.title, artist: s.artist_name || s.artistName, album_cover: s.album_cover }, [s], 0);
    }

    function topSongId(s) { return s.song_id || s.songId; }

    return { stats, loading, days, load, trend, hourly, genres, hours, playTopSong, topSongId, coverUrl: (id) => api.getCoverUrl(id) };
  },
  template: `
    <div class="page-content">
      <div class="page-header page-header-row">
        <div>
          <h1 class="page-title">📊 统计</h1>
          <p class="page-subtitle">你的听歌数据</p>
        </div>
        <div class="flex gap-8">
          <button class="chip" :class="{ active: days === 7 }" @click="days = 7; load()">7 天</button>
          <button class="chip" :class="{ active: days === 30 }" @click="days = 30; load()">30 天</button>
          <button class="chip" :class="{ active: days === 90 }" @click="days = 90; load()">90 天</button>
        </div>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else-if="stats">
        <div class="stats-grid">
          <div class="stat-card">
            <span class="stat-card-icon">▶️</span>
            <div class="stat-card-label">总播放次数</div>
            <div class="stat-card-value accent">{{ stats.totalPlays || 0 }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">⏱️</span>
            <div class="stat-card-label">总收听时长</div>
            <div class="stat-card-value accent">{{ hours.whole }}<span style="font-size:16px;">h</span> <span style="font-size:14px;" class="muted">{{ hours.min }}m</span></div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">🔥</span>
            <div class="stat-card-label">日均播放</div>
            <div class="stat-card-value accent">{{ (stats.totalPlays / (days || 30)).toFixed(1) }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">🎼</span>
            <div class="stat-card-label">常听曲风</div>
            <div class="stat-card-value" style="font-size:20px;padding-top:8px;">{{ genres[0]?.name || '—' }}</div>
          </div>
        </div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px;">
          <div class="chart-card" v-if="trend.length">
            <div class="chart-card-title">📈 听歌趋势</div>
            <div class="chart-bars">
              <div v-for="(d, i) in trend" :key="i" class="chart-bar-wrap">
                <span class="chart-value">{{ d.value }}</span>
                <div class="chart-bar" :style="{ height: d.pct + '%' }" :title="d.label + ': ' + d.value + ' 次'"></div>
                <span class="chart-bar-label">{{ d.label }}</span>
              </div>
            </div>
          </div>
          <div class="chart-card" v-if="hourly.length">
            <div class="chart-card-title">🕐 时段分布</div>
            <div class="chart-bars">
              <div v-for="(d, i) in hourly" :key="i" class="chart-bar-wrap">
                <span class="chart-value">{{ d.value }}</span>
                <div class="chart-bar" :style="{ height: d.pct + '%' }" :title="d.label + ': ' + d.value + ' 次'"></div>
                <span class="chart-bar-label">{{ d.label }}</span>
              </div>
            </div>
          </div>
        </div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px;">
          <div class="chart-card" v-if="genres.length">
            <div class="chart-card-title">🎼 热门曲风</div>
            <div class="hbar-list">
              <div v-for="(g, i) in genres.slice(0, 8)" :key="i" class="hbar-item">
                <div class="hbar-head">
                  <span class="hbar-name">{{ g.name }}</span>
                  <span class="hbar-val">{{ g.count }} 次</span>
                </div>
                <div class="hbar-track"><div class="hbar-fill" :style="{ width: g.pct + '%' }"></div></div>
              </div>
            </div>
          </div>
          <div class="chart-card" v-if="stats.topSongs?.length">
            <div class="chart-card-title">🏆 最常播放</div>
            <div class="rank-list" style="max-height:280px;overflow-y:auto;">
              <div v-for="(s, i) in stats.topSongs.slice(0, 8)" :key="s.songId || s.song_id" class="rank-item" @click="playTopSong(s)">
                <span class="rank-num">{{ i + 1 }}</span>
                <div class="rank-cover"><img v-if="s.album_cover" :src="coverUrl(topSongId(s))" /><span v-else>🎵</span></div>
                <div class="rank-info">
                  <div class="rank-title">{{ s.title }}</div>
                  <div class="rank-sub">{{ s.artistName }}</div>
                </div>
                <span class="rank-count">{{ s.playCount }} 次</span>
              </div>
            </div>
          </div>
        </div>

        <div class="chart-card" v-if="stats.topArtists?.length">
          <div class="chart-card-title">🎤 常听艺术家</div>
          <div class="rank-list" style="display:grid;grid-template-columns:1fr 1fr;gap:4px;">
            <div v-for="(a, i) in stats.topArtists.slice(0, 10)" :key="a.artistId" class="rank-item">
              <span class="rank-num">{{ i + 1 }}</span>
              <div class="rank-info">
                <div class="rank-title">{{ a.artistName }}</div>
              </div>
              <span class="rank-count">{{ a.playCount }} 次</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 设置 ──
const SettingsView = {
  name: 'SettingsView',
  setup() {
    const tab = ref('account');
    const store2 = store;

    async function doLogout() {
      await logout();
    }

    async function doChangePassword() {
      // 简化：提示用户该功能通过 API 支持，前端暂不做完整表单
      showToast('密码修改接口已就绪（PUT /auth/password）', 'info');
    }

    // 通知开关（请求浏览器权限）
    async function toggleNotify() {
      store.notifyOnNewSong = !store.notifyOnNewSong;
      if (store.notifyOnNewSong && 'Notification' in window && Notification.permission === 'default') {
        try { await Notification.requestPermission(); } catch {}
      }
      showToast(store.notifyOnNewSong ? '通知已开启' : '通知已关闭', 'success');
    }

    return { store, tab, doLogout, doChangePassword, setTheme, player, toggleNotify };
  },
  template: `
    <div class="page-content">
      <div class="page-header">
        <h1 class="page-title">⚙️ 设置</h1>
        <p class="page-subtitle">账户、外观、播放偏好</p>
      </div>
      <div class="settings-layout">
        <div class="settings-nav">
          <button class="settings-nav-item" :class="{ active: tab === 'account' }" @click="tab = 'account'">👤 账户</button>
          <button class="settings-nav-item" :class="{ active: tab === 'appearance' }" @click="tab = 'appearance'">🎨 外观</button>
          <button class="settings-nav-item" :class="{ active: tab === 'playback' }" @click="tab = 'playback'">🎧 音质偏好</button>
          <button class="settings-nav-item" :class="{ active: tab === 'notify' }" @click="tab = 'notify'">🔔 通知</button>
          <button class="settings-nav-item" :class="{ active: tab === 'about' }" @click="tab = 'about'">ℹ️ 关于</button>
        </div>

        <div>
          <!-- 账户 -->
          <div v-if="tab === 'account'" class="settings-card">
            <div class="settings-card-title">👤 账户信息</div>
            <div style="display:flex;align-items:center;gap:16px;margin-bottom:20px;">
              <div style="width:56px;height:56px;border-radius:50%;background:var(--accent-grad);color:#fff;display:flex;align-items:center;justify-content:center;font-size:22px;font-weight:700;">
                {{ store.user?.displayName?.[0] || store.user?.username?.[0] || '?' }}
              </div>
              <div>
                <div style="font-size:17px;font-weight:700;">{{ store.user?.displayName || store.user?.username }}</div>
                <div class="muted" style="font-size:13px;margin-top:2px;">
                  @{{ store.user?.username }} · {{ store.user?.role === 'admin' ? '管理员' : '普通用户' }}
                </div>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">用户名</div>
                <div class="setting-desc">{{ store.user?.username }}</div>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">用户 ID</div>
                <div class="setting-desc">{{ store.user?.id }}</div>
              </div>
              <span class="badge" :class="store.user?.role === 'admin' ? 'accent' : ''">{{ store.user?.role === 'admin' ? '管理员' : '用户' }}</span>
            </div>
            <div style="margin-top:20px;">
              <button class="btn btn-danger" @click="doLogout">退出登录</button>
            </div>
          </div>

          <!-- 外观 -->
          <div v-if="tab === 'appearance'" class="settings-card">
            <div class="settings-card-title">🎨 主题</div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">外观模式</div>
                <div class="setting-desc">选择你喜欢的界面主题</div>
              </div>
              <div class="theme-picker">
                <div class="theme-option" :class="{ active: store.theme === 'dark' }" @click="setTheme('dark')">
                  <div class="theme-swatch dark">🌙</div>
                  <span>深色</span>
                </div>
                <div class="theme-option" :class="{ active: store.theme === 'light' }" @click="setTheme('light')">
                  <div class="theme-swatch light">☀️</div>
                  <span>浅色</span>
                </div>
                <div class="theme-option" :class="{ active: store.theme === 'system' }" @click="setTheme('system')">
                  <div class="theme-swatch" style="background:linear-gradient(135deg,#161927 50%,#f4f5fb 50%);">🖥️</div>
                  <span>跟随系统</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 音质偏好 -->
          <div v-if="tab === 'playback'" class="settings-card">
            <div class="settings-card-title">🎧 音质偏好</div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">流模式</div>
                <div class="setting-desc">直连模式兼容性好；HLS 模式支持自适应码率</div>
              </div>
              <div class="setting-control">
                <button class="chip" :class="{ active: store.streamMode === 'direct' }" @click="store.streamMode = 'direct'">直连</button>
                <button class="chip" :class="{ active: store.streamMode === 'hls' }" @click="store.streamMode = 'hls'">HLS</button>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">转码格式</div>
                <div class="setting-desc">将音频转码为指定格式（需要 FFmpeg 支持）</div>
              </div>
              <div class="setting-control">
                <button class="chip" :class="{ active: store.streamTranscode === 'off' }" @click="store.streamTranscode = 'off'">原始</button>
                <button class="chip" :class="{ active: store.streamTranscode === 'mp3' }" @click="store.streamTranscode = 'mp3'">MP3</button>
                <button class="chip" :class="{ active: store.streamTranscode === 'aac' }" @click="store.streamTranscode = 'aac'">AAC</button>
                <button class="chip" :class="{ active: store.streamTranscode === 'opus' }" @click="store.streamTranscode = 'opus'">Opus</button>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">目标码率</div>
                <div class="setting-desc">转码时的目标码率（kbps）</div>
              </div>
              <div class="setting-control">
                <button class="chip" :class="{ active: store.streamBitrate === 0 }" @click="store.streamBitrate = 0">自动</button>
                <button class="chip" :class="{ active: store.streamBitrate === 128 }" @click="store.streamBitrate = 128">128</button>
                <button class="chip" :class="{ active: store.streamBitrate === 192 }" @click="store.streamBitrate = 192">192</button>
                <button class="chip" :class="{ active: store.streamBitrate === 320 }" @click="store.streamBitrate = 320">320</button>
              </div>
            </div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">播放模式</div>
                <div class="setting-desc">切换随机 / 单曲循环 / 顺序播放</div>
              </div>
              <div class="setting-control">
                <button class="btn btn-secondary btn-sm" @click="player.togglePlayMode()">
                  {{ store.shuffle ? '🔀 随机' : store.repeat === 'one' ? '🔂 单曲' : '➡️ 顺序' }}
                </button>
              </div>
            </div>
          </div>

          <!-- 通知 -->
          <div v-if="tab === 'notify'" class="settings-card">
            <div class="settings-card-title">🔔 通知管理</div>
            <div class="setting-row">
              <div class="setting-info">
                <div class="setting-name">播放通知</div>
                <div class="setting-desc">切换歌曲时显示系统通知（需要浏览器授权）</div>
              </div>
              <div class="setting-control">
                <button class="switch" :class="{ on: store.notifyOnNewSong }" @click="toggleNotify"></button>
              </div>
            </div>
          </div>

          <!-- 关于 -->
          <div v-if="tab === 'about'" class="settings-card">
            <div class="settings-card-title">ℹ️ 关于</div>
            <div style="text-align:center;padding:24px 0;">
              <div style="font-size:52px;margin-bottom:12px;">🐱</div>
              <div style="font-size:20px;font-weight:800;">Cat<span style="background:var(--accent-grad);-webkit-background-clip:text;-webkit-text-fill-color:transparent;">Claw</span> Music</div>
              <div class="muted" style="font-size:13px;margin-top:8px;">私人音乐流媒体服务器 · C# ASP.NET Core</div>
              <div style="margin-top:20px;">
                <span class="badge accent">Web UI v2.0</span>
                <span class="badge">API v1</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 管理面板 ──
const AdminView = {
  name: 'AdminView',
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
        showToast('扫描已启动', 'success');
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
        <h1 class="page-title">🛠️ 管理面板</h1>
      </div>
      <div v-if="loading" class="loading"><div class="loading-spinner"></div></div>
      <div v-else>
        <div class="stats-grid" v-if="serverStats">
          <div class="stat-card">
            <span class="stat-card-icon">🎵</span>
            <div class="stat-card-label">歌曲</div>
            <div class="stat-card-value accent">{{ serverStats.songCount }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">🎤</span>
            <div class="stat-card-label">艺术家</div>
            <div class="stat-card-value accent">{{ serverStats.artistCount }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">💿</span>
            <div class="stat-card-label">专辑</div>
            <div class="stat-card-value accent">{{ serverStats.albumCount }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">👥</span>
            <div class="stat-card-label">用户</div>
            <div class="stat-card-value accent">{{ serverStats.userCount }}</div>
          </div>
          <div class="stat-card">
            <span class="stat-card-icon">▶️</span>
            <div class="stat-card-label">总播放</div>
            <div class="stat-card-value accent">{{ serverStats.totalPlays }}</div>
          </div>
        </div>

        <div class="settings-card">
          <div class="settings-card-title">🔍 扫描</div>
          <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;">
            <button class="btn btn-primary" :disabled="scanning" @click="triggerScan(false)">
              {{ scanning ? '扫描中...' : '开始扫描' }}
            </button>
            <button class="btn btn-secondary" :disabled="scanning" @click="triggerScan(true)">增量扫描</button>
            <button class="btn btn-secondary" @click="doCleanup">清理失效记录</button>
          </div>
          <div v-if="scanStatus?.last_result" style="margin-top:12px;font-size:13px;color:var(--text-2);">
            上次扫描: 处理 {{ scanStatus.last_result.processed }} · 新增 {{ scanStatus.last_result.added }} · 更新 {{ scanStatus.last_result.updated }} · 跳过 {{ scanStatus.last_result.skipped }} · 错误 {{ scanStatus.last_result.errors }}
          </div>
          <div v-if="scanStatus?.scanning" style="margin-top:12px;">
            <div style="font-size:13px;color:var(--text-2);">扫描中... {{ scanStatus.processed }}/{{ scanStatus.total }}</div>
          </div>
        </div>

        <div class="settings-card" v-if="systemInfo">
          <div class="settings-card-title">🖥️ 系统信息</div>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px;font-size:13px;">
            <div><span class="muted">版本: </span>{{ systemInfo.server?.version }}</div>
            <div><span class="muted">运行时: </span>{{ systemInfo.server?.runtime }}</div>
            <div><span class="muted">操作系统: </span>{{ systemInfo.system?.os }}</div>
            <div><span class="muted">CPU 核心: </span>{{ systemInfo.system?.processor_count }}</div>
            <div><span class="muted">内存: </span>{{ systemInfo.system?.working_set_mb }} MB</div>
            <div><span class="muted">数据库: </span>{{ systemInfo.database?.size_mb }} MB</div>
          </div>
        </div>

        <div class="settings-card" v-if="users.length">
          <div class="settings-card-title">👥 用户管理</div>
          <div v-for="u in users" :key="u.id" style="display:flex;align-items:center;gap:12px;padding:10px 0;border-bottom:1px solid var(--border);">
            <div style="width:34px;height:34px;border-radius:50%;background:var(--accent-grad);color:#fff;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:700;">
              {{ u.display_name?.[0] || u.username?.[0] || '?' }}
            </div>
            <div style="flex:1;">
              <div style="font-weight:600;font-size:14px;">{{ u.display_name || u.username }}</div>
              <div style="font-size:12px;color:var(--text-3);">{{ u.role }} · 注册 {{ new Date(u.created_at).toLocaleDateString() }}</div>
            </div>
            <button class="btn-icon" @click="doDeleteUser(u.id)" title="删除用户" style="color:var(--danger);">🗑</button>
          </div>
        </div>
      </div>
    </div>
  `
};

// ── 登录/注册 ──
const LoginView = {
  name: 'LoginView',
  setup() {
    const isRegister = ref(false);
    const username = ref('');
    const password = ref('');
    const displayName = ref('');
    const error = ref('');
    const loading = ref(false);
    const router = useRouter();

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
        }
        const data = await login(username.value, password.value);
        store.user = data.user;
        store.isAuthenticated = true;
        await loadOverview();
        router.push('/');
      } catch (e) {
        error.value = e.message || (isRegister.value ? '注册失败' : '登录失败');
      }
      loading.value = false;
    }

    return { isRegister, username, password, displayName, error, loading, handleSubmit };
  },
  template: `
    <div style="display:flex;align-items:center;justify-content:center;flex:1;width:100%;min-height:100vh;background:var(--bg-0);">
      <div style="width:400px;max-width:90vw;background:var(--bg-2);border:1px solid var(--border);border-radius:18px;padding:36px;box-shadow:var(--shadow-3);">
        <div style="text-align:center;margin-bottom:28px;">
          <div style="font-size:46px;margin-bottom:10px;">🐱</div>
          <h1 style="font-size:24px;font-weight:800;letter-spacing:-0.02em;">Cat<span style="background:var(--accent-grad);-webkit-background-clip:text;-webkit-text-fill-color:transparent;">Claw</span> Music</h1>
          <p style="color:var(--text-2);font-size:13.5px;margin-top:6px;">{{ isRegister ? '创建管理员账户' : '登录你的音乐库' }}</p>
        </div>

        <div v-if="error" style="background:var(--danger-soft);border:1px solid var(--danger);border-radius:10px;padding:11px 14px;margin-bottom:18px;color:var(--danger);font-size:13px;">
          {{ error }}
        </div>

        <div class="form-group" v-if="isRegister">
          <label>显示名称</label>
          <input type="text" class="form-control" v-model="displayName" placeholder="你的名称" />
        </div>
        <div class="form-group">
          <label>用户名</label>
          <input type="text" class="form-control" v-model="username" placeholder="用户名" @keyup.enter="handleSubmit" />
        </div>
        <div class="form-group">
          <label>密码</label>
          <input type="password" class="form-control" v-model="password" placeholder="至少 6 位" @keyup.enter="handleSubmit" />
        </div>

        <button class="btn btn-primary" style="width:100%;padding:13px;font-size:15px;margin-top:8px;" :disabled="loading" @click="handleSubmit">
          {{ loading ? '处理中...' : (isRegister ? '创建账户' : '登录') }}
        </button>

        <div style="text-align:center;margin-top:18px;">
          <a style="color:var(--accent);font-size:13.5px;cursor:pointer;text-decoration:none;" @click="isRegister = !isRegister; error = '';">
            {{ isRegister ? '已有账户？去登录' : '没有账户？创建管理员' }}
          </a>
        </div>
      </div>
    </div>
  `
};

// ════════════════════════════════════════════════════════════
// 路由
// ════════════════════════════════════════════════════════════
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
  { path: '/admin', component: AdminView },
  { path: '/settings', component: SettingsView },
];

const router = createRouter({
  history: createWebHashHistory(),
  routes,
  scrollBehavior() { return { top: 0 }; }
});

const App = {
  components: { AppSidebar, TopBar, PlayerBar, LyricsPanel, QueuePanel, ToastContainer, LoginView },
  setup() {
    const ready = ref(false);

    onMounted(async () => {
      await loadUser();
      ready.value = true;
      if (store.isAuthenticated) {
        loadOverview();
        // 预取收藏状态（当前页面歌曲列表会用到）
        try {
          const fav = await api.getFavorites({ page: 1, page_size: 200 });
          (fav.items || []).forEach(s => store.favoriteIds.add(s.id));
        } catch {}
      }
      player.init();
      window.addEventListener('beforeunload', () => { flushScrobble(); });
    });

    // 登录成功 → 回首页；未登录 → 跳登录页
    watch(() => store.isAuthenticated, (authed) => {
      if (!ready.value) return;
      if (!authed) {
        window.location.hash = '#/login';
      } else if (window.location.hash === '#/login' || window.location.hash === '#') {
        window.location.hash = '#/';
      }
    });

    // 通知（可选）
    watch(() => store.currentSong?.id, () => {
      if (store.notifyOnNewSong && store.currentSong) {
        try {
          if (Notification.permission === 'granted') {
            new Notification(store.currentSong.title, { body: store.currentSong.artist });
          }
        } catch {}
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
      <div class="main-wrap">
        <top-bar />
        <main class="main-content">
          <router-view />
        </main>
      </div>
      <player-bar />
      <lyrics-panel />
      <queue-panel />
      <toast-container />
    </div>
  `
};

const app = createApp(App);
app.use(router);

try {
  app.mount('#app');
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
