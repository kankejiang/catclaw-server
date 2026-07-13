/**
 * CatClaw Music Server — API Client
 * Handles JWT auth, token refresh, and all API calls.
 */

const API_BASE = '/api/v1';

class ApiClient {
  constructor() {
    this.accessToken = null;
    this.refreshToken = null;
    this.refreshPromise = null;
    this._loadTokens();
  }

  _loadTokens() {
    // Use cookie-based storage (simple key=value in a single cookie)
    const match = document.cookie.match(/catclaw_tokens=([^;]+)/);
    if (match) {
      try {
        const data = JSON.parse(decodeURIComponent(match[1]));
        this.accessToken = data.access;
        this.refreshToken = data.refresh;
      } catch { /* ignore */ }
    }
  }

  _saveTokens(access, refresh) {
    this.accessToken = access;
    if (refresh) this.refreshToken = refresh;
    const data = JSON.stringify({ access: this.accessToken, refresh: this.refreshToken });
    document.cookie = `catclaw_tokens=${encodeURIComponent(data)}; path=/; max-age=${30 * 86400}; SameSite=Strict`;
  }

  clearTokens() {
    this.accessToken = null;
    this.refreshToken = null;
    document.cookie = 'catclaw_tokens=; path=/; max-age=0';
  }

  get isAuthenticated() {
    return !!this.accessToken;
  }

  async _fetch(url, options = {}) {
    if (!options.headers) options.headers = {};

    if (this.accessToken) {
      options.headers['Authorization'] = `Bearer ${this.accessToken}`;
    }

    let response = await fetch(url, options);

    // If 401 and we have a refresh token, try refreshing
    if (response.status === 401 && this.refreshToken && !options._retried) {
      const refreshed = await this._doRefresh();
      if (refreshed) {
        options._retried = true;
        options.headers['Authorization'] = `Bearer ${this.accessToken}`;
        response = await fetch(url, options);
      }
    }

    return response;
  }

  async _doRefresh() {
    // Deduplicate concurrent refresh calls
    if (this.refreshPromise) return this.refreshPromise;

    this.refreshPromise = (async () => {
      try {
        const res = await fetch(`${API_BASE}/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: this.refreshToken })
        });

        if (!res.ok) {
          this.clearTokens();
          window.location.href = '/login.html';
          return false;
        }

        const json = await res.json();
        if (json.code !== 0) {
          this.clearTokens();
          window.location.href = '/login.html';
          return false;
        }

        this._saveTokens(json.data.access_token, json.data.refresh_token);
        return true;
      } catch {
        this.clearTokens();
        window.location.href = '/login.html';
        return false;
      } finally {
        this.refreshPromise = null;
      }
    })();

    return this.refreshPromise;
  }

  async get(path, params = {}) {
    const url = new URL(API_BASE + path, window.location.origin);
    Object.entries(params).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, v);
    });

    const res = await this._fetch(url.toString());
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const text = await res.text();
    try {
      const json = JSON.parse(text);
      if (json.code !== undefined && json.code !== 0) throw new Error(json.message || 'API error');
      return json.data !== undefined ? json.data : json;
    } catch (e) {
      if (e.message?.startsWith('API error') || e.message?.includes('API error')) throw e;
      throw new Error('Invalid JSON response');
    }
  }

  async post(path, body = null) {
    const res = await this._fetch(API_BASE + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : null
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const text = await res.text();
    try {
      const json = JSON.parse(text);
      if (json.code !== undefined && json.code !== 0) throw new Error(json.message || 'API error');
      return json.data !== undefined ? json.data : json;
    } catch (e) {
      if (e.message?.startsWith('API error') || e.message?.includes('API error')) throw e;
      throw new Error('Invalid JSON response');
    }
  }

  async put(path, body = null) {
    const res = await this._fetch(API_BASE + path, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : null
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const text = await res.text();
    try {
      const json = JSON.parse(text);
      if (json.code !== undefined && json.code !== 0) throw new Error(json.message || 'API error');
      return json.data !== undefined ? json.data : json;
    } catch (e) {
      if (e.message?.includes('API error')) throw e;
      return null;
    }
  }

  async del(path) {
    const res = await this._fetch(API_BASE + path, { method: 'DELETE' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const text = await res.text();
    try {
      const json = JSON.parse(text);
      if (json.code !== undefined && json.code !== 0) throw new Error(json.message || 'API error');
      return json.data !== undefined ? json.data : json;
    } catch (e) {
      if (e.message?.includes('API error')) throw e;
      return null;
    }
  }

  // ── Auth ──
  async login(username, password, deviceName) {
    const data = await this.post('/auth/login', {
      username, password,
      deviceName: deviceName || navigator.userAgent.substring(0, 50),
      platform: 'web'
    });
    this._saveTokens(data.access_token, data.refresh_token);
    return data;
  }

  async register(username, password, displayName) {
    return this.post('/auth/register', { username, password, displayName });
  }

  async logout() {
    try { await this.post('/auth/logout', { refreshToken: this.refreshToken }); } catch {}
    this.clearTokens();
    window.location.href = '/login.html';
  }

  async getMe() {
    return this.get('/auth/me');
  }

  // ── Library ──
  async getOverview() { return this.get('/library/overview'); }

  // ── Songs ──
  async getSongs(params) { return this.get('/songs', params); }
  async getSong(id) { return this.get(`/songs/${id}`); }
  async getRandomSongs(count) { return this.get('/songs/random', { count }); }
  // 媒体端点（audio/img/hls 无法设置 Authorization 头）通过 access_token 查询参数鉴权
  _withToken(url) {
    if (!this.accessToken) return url;
    const sep = url.includes('?') ? '&' : '?';
    return `${url}${sep}access_token=${encodeURIComponent(this.accessToken)}`;
  }
  getStreamUrl(id) { return this._withToken(`${API_BASE}/songs/${id}/stream`); }
  getHlsUrl(id) { return this._withToken(`${API_BASE}/hls/${id}/master.m3u8`); }
  getCoverUrl(id, size) {
    let url = `${API_BASE}/songs/${id}/cover`;
    if (size) url += `?size=${size}`;
    return this._withToken(url);
  }
  getAlbumCoverUrl(id, size) {
    let url = `${API_BASE}/albums/${id}/cover`;
    if (size) url += `?size=${size}`;
    return this._withToken(url);
  }
  getArtistCoverUrl(id, size) {
    let url = `${API_BASE}/artists/${id}/cover`;
    if (size) url += `?size=${size}`;
    return this._withToken(url);
  }
  async getLyrics(id) { return this.get(`/songs/${id}/lyrics`); }

  // ── Artists ──
  async getArtists(params) { return this.get('/artists', params); }
  async getArtist(id) { return this.get(`/artists/${id}`); }
  async getArtistSongs(id, params) { return this.get(`/artists/${id}/songs`, params); }
  async getArtistAlbums(id) { return this.get(`/artists/${id}/albums`); }

  // ── Albums ──
  async getAlbums(params) { return this.get('/albums', params); }
  async getAlbum(id) { return this.get(`/albums/${id}`); }
  async getAlbumSongs(id) { return this.get(`/albums/${id}/songs`); }

  // ── Search ──
  async search(query) { return this.get('/search', { q: query }); }

  // ── Playlists ──
  async getPlaylists(params) { return this.get('/playlists', params); }
  async getPlaylist(id) { return this.get(`/playlists/${id}`); }
  async createPlaylist(data) { return this.post('/playlists', data); }
  async updatePlaylist(id, data) { return this.put(`/playlists/${id}`, data); }
  async deletePlaylist(id) { return this.del(`/playlists/${id}`); }
  async addSongsToPlaylist(id, songIds, position) {
    return this.post(`/playlists/${id}/songs`, { songIds, position });
  }
  async removeSongFromPlaylist(id, songId) {
    return this.del(`/playlists/${id}/songs/${songId}`);
  }
  async reorderPlaylist(id, songIds) {
    return this.put(`/playlists/${id}/reorder`, { songIds });
  }

  // ── Favorites ──
  async getFavorites(params) { return this.get('/favorites', params); }
  async addFavorite(songId) { return this.post(`/favorites/${songId}`); }
  async removeFavorite(songId) { return this.del(`/favorites/${songId}`); }
  async checkFavorites(songIds) {
    return this.get('/favorites/check', { song_ids: songIds.join(',') });
  }

  // ── Ratings ──
  async setRating(songId, rating) { return this.post(`/ratings/${songId}`, { rating }); }
  async removeRating(songId) { return this.del(`/ratings/${songId}`); }
  async getRatings(params) { return this.get('/ratings', params); }

  // ── Scrobble / History ──
  async scrobble(songId, durationPlayedMs, source) {
    return this.post('/scrobble', { songId, durationPlayedMs, source: source || 'library' });
  }
  async getHistory(params) { return this.get('/history', params); }

  // ── Play Queue ──
  async getPlayQueue() { return this.get('/playqueue'); }
  async savePlayQueue(songIds, currentIndex, positionMs) {
    return this.put('/playqueue', { songIds, currentIndex, positionMs });
  }
  async clearPlayQueue() { return this.del('/playqueue'); }

  // ── Stats ──
  async getStats(days) { return this.get('/stats', { days }); }
  async getServerStats() { return this.get('/stats/overview'); }

  // ── Recommend ──
  async getDailyRecommend(count) { return this.get('/recommend/daily', { count }); }
  async getRecentlyPlayed(count) { return this.get('/recommend/recent', { count }); }
  async getTopPlayed(count, days) { return this.get('/recommend/top', { count, days }); }
  async getDiscover(count) { return this.get('/recommend/discover', { count }); }
  async getArtistMix(artistId, count) { return this.get('/recommend/artist-mix', { artist_id: artistId, count }); }

  // ── Admin ──
  async triggerScan() { return this.post('/admin/scan'); }
  async getScanStatus() { return this.get('/admin/scan/status'); }
  async triggerIncrementalScan() { return this.post('/admin/scan/incremental'); }
  async getSystemInfo() { return this.get('/admin/system'); }
  async getUsers() { return this.get('/admin/users'); }
  async deleteUser(id) { return this.del(`/admin/users/${id}`); }
  async cleanup() { return this.post('/admin/cleanup'); }

  // ── Genres ──
  async getGenres() { return this.get('/genres'); }
  async getGenreSongs(name, params) { return this.get(`/genres/${encodeURIComponent(name)}/songs`, params); }

  // ── ClawCircle P2P ──
  async getP2PPeers() { return this._getRaw('/api/clawcircle/peers'); }
  async getP2PStats() { return this._getRaw('/api/clawcircle/stats'); }
  async getDhtNodes() { return this._getRaw('/api/clawcircle/dht/nodes'); }
  async getReputation() { return this._getRaw('/api/clawcircle/reputation'); }
  async toggleDht(enabled) {
    const res = await this._fetch('/api/clawcircle/dht/toggle', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled })
    });
    return res.ok ? res.json() : null;
  }
  async bootstrapDht(address) {
    const res = await this._fetch('/api/clawcircle/dht/bootstrap', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ address })
    });
    return res.ok ? res.json() : null;
  }

  // Raw GET without /api/v1 prefix
  async _getRaw(path) {
    const res = await this._fetch(path);
    if (!res.ok) return null;
    try { return await res.json(); } catch { return null; }
  }
}

export const api = new ApiClient();
